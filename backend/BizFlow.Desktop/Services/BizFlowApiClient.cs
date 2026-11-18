using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BizFlow.Desktop.Models;

namespace BizFlow.Desktop.Services;

public sealed class ApiClientException(HttpStatusCode statusCode, string code, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class BizFlowApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SessionManager _session;
    private readonly BackendHostSupervisor _supervisor;
    private readonly bool _customHttpClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public BizFlowApiClient(SessionManager session, BackendHostSupervisor supervisor, HttpClient? customHttpClient = null)
    {
        _session = session;
        _supervisor = supervisor;
        _customHttpClient = customHttpClient is not null;
        _http = customHttpClient ?? new HttpClient
        {
            BaseAddress = new Uri(supervisor.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private Task EnsureBackendReadyAsync(CancellationToken ct) =>
        _customHttpClient ? Task.CompletedTask : _supervisor.EnsureBackendRunningAsync(ct);

    // ========================================================================
    // 1. AUTHENTICATION, EMAIL-LINK, PASSWORD RESET & PUBLIC REGISTRATION
    // ========================================================================
    public async Task<LoginResponseDto> LoginAsync(string identifier, string password, string? tenantKey, CancellationToken ct = default)
    {
        await EnsureBackendReadyAsync(ct);
        var payload = new Dictionary<string, object?>
        {
            ["identifier"] = identifier.Trim(),
            ["password"] = password
        };
        if (!string.IsNullOrWhiteSpace(tenantKey))
        {
            payload["tenantKey"] = tenantKey.Trim();
        }

        var response = await SendAsync<LoginResponseDto>(HttpMethod.Post, "api/v1/auth/login", payload, requireAuth: false, ct: ct);
        _session.SetSession(response);
        return response;
    }

    public async Task<EmailLinkRequestResponseDto> RequestEmailLinkAsync(string email, CancellationToken ct = default)
    {
        await EnsureBackendReadyAsync(ct);
        return await SendAsync<EmailLinkRequestResponseDto>(HttpMethod.Post, "api/v1/auth/email-link/request", new { email = email.Trim() }, requireAuth: false, ct: ct);
    }

    public async Task<EmailLinkVerificationResponseDto> VerifyEmailLinkAsync(string token, CancellationToken ct = default)
    {
        await EnsureBackendReadyAsync(ct);
        var res = await SendAsync<EmailLinkVerificationResponseDto>(HttpMethod.Post, "api/v1/auth/email-link/verify", new { token = token.Trim() }, requireAuth: false, ct: ct);
        if (string.Equals(res.Status, "Authenticated", StringComparison.OrdinalIgnoreCase) && res.Authentication is not null)
        {
            _session.SetSession(res.Authentication);
        }
        return res;
    }

    public async Task<EmailLinkVerificationResponseDto> VerifyFirebaseTokenAsync(string idToken, CancellationToken ct = default)
    {
        await EnsureBackendReadyAsync(ct);
        var res = await SendAsync<EmailLinkVerificationResponseDto>(HttpMethod.Post, "api/v1/auth/email-link/verify-firebase", new { idToken }, requireAuth: false, ct: ct);
        if (string.Equals(res.Status, "Authenticated", StringComparison.OrdinalIgnoreCase) && res.Authentication is not null)
        {
            _session.SetSession(res.Authentication);
        }
        return res;
    }

    public async Task<LoginResponseDto> SelectWorkspaceAsync(string ticket, string tenantKey, CancellationToken ct = default)
    {
        var res = await SendAsync<LoginResponseDto>(HttpMethod.Post, "api/v1/auth/email-link/select-workspace", new { ticket, tenantKey }, requireAuth: false, ct: ct);
        _session.SetSession(res);
        return res;
    }

    public async Task<string> ForgotPasswordAsync(string identifier, string? tenantKey, CancellationToken ct = default)
    {
        await EnsureBackendReadyAsync(ct);
        var doc = await SendAsync<JsonElement>(HttpMethod.Post, "api/v1/auth/forgot-password",
            new { identifier = identifier.Trim(), tenantKey = string.IsNullOrWhiteSpace(tenantKey) ? null : tenantKey.Trim() },
            requireAuth: false, ct: ct);
        return doc.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Đã gửi hướng dẫn khôi phục mật khẩu." : "Đã gửi hướng dẫn khôi phục mật khẩu.";
    }

    public async Task<string> ResetPasswordAsync(string identifier, string? tenantKey, string resetToken, string newPassword, CancellationToken ct = default)
    {
        await EnsureBackendReadyAsync(ct);
        var doc = await SendAsync<JsonElement>(HttpMethod.Post, "api/v1/auth/reset-password",
            new
            {
                identifier = identifier.Trim(),
                tenantKey = string.IsNullOrWhiteSpace(tenantKey) ? null : tenantKey.Trim(),
                resetToken = resetToken.Trim(),
                newPassword
            },
            requireAuth: false, ct: ct);
        return doc.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Đã đổi mật khẩu thành công." : "Đã đổi mật khẩu thành công.";
    }

    public Task<CheckCodeResponseDto> CheckWorkspaceCodeAsync(string code, CancellationToken ct = default) =>
        SendAsync<CheckCodeResponseDto>(HttpMethod.Get, $"api/v1/tenants/check-code?code={Uri.EscapeDataString(code.Trim().ToLowerInvariant())}", requireAuth: false, ct: ct);

    public Task<GenerateCodeResponseDto> GenerateWorkspaceCodeAsync(string? baseName, CancellationToken ct = default) =>
        SendAsync<GenerateCodeResponseDto>(HttpMethod.Post, "api/v1/tenants/generate-code", new { baseName }, requireAuth: false, ct: ct);

    public async Task<UploadCompanyDocumentResultDto> UploadCompanyDocumentAsync(string filePath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        await using var fs = File.OpenRead(filePath);
        var streamContent = new StreamContent(fs);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(filePath));
        form.Add(streamContent, "file", Path.GetFileName(filePath));

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/company-registrations/upload-document") { Content = form };
        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessOrThrowAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<UploadCompanyDocumentResultDto>(JsonOptions, ct))!;
    }

    public Task<SubmitCompanyRegistrationResponseDto> SubmitCompanyRegistrationAsync(
        string taxId, string name, string code, string contactPersonName, string contactEmail, string contactPhone,
        string? evidenceDocumentKey, string? evidenceDocumentFileName, CancellationToken ct = default) =>
        SendAsync<SubmitCompanyRegistrationResponseDto>(HttpMethod.Post, "api/v1/company-registrations",
            new
            {
                taxId = taxId.Trim(),
                name = name.Trim(),
                code = code.Trim().ToLowerInvariant(),
                contactPersonName = contactPersonName.Trim(),
                contactEmail = contactEmail.Trim(),
                contactPhone = contactPhone.Trim(),
                evidenceDocumentKey,
                evidenceDocumentFileName
            },
            requireAuth: false, ct: ct);

    // ========================================================================
    // 2. PLATFORM COMPANY REGISTRATIONS (PLATFORM_ADMIN)
    // ========================================================================
    public Task<CompanyRegistrationPageDto> GetCompanyRegistrationsAsync(int page = 1, int pageSize = 25, string? status = null, string? search = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(("page", page.ToString()), ("pageSize", pageSize.ToString()), ("status", status), ("search", search));
        return SendAsync<CompanyRegistrationPageDto>(HttpMethod.Get, $"api/v1/platform/company-registrations{qs}", ct: ct);
    }

    public Task<JsonElement> ApproveCompanyRegistrationAsync(
        Guid companyId, string tenantKey, string adminEmployeeCode, string adminEmail, string adminFullName, string adminPassword, string timeZone = "Asia/Ho_Chi_Minh", CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/platform/company-registrations/{companyId}/approve",
            new
            {
                tenantKey = tenantKey.Trim().ToLowerInvariant(),
                adminEmployeeCode = adminEmployeeCode.Trim(),
                adminEmail = adminEmail.Trim(),
                adminFullName = adminFullName.Trim(),
                adminPassword,
                timeZone
            }, ct: ct);

    public Task<JsonElement> RejectCompanyRegistrationAsync(Guid companyId, string reason, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/platform/company-registrations/{companyId}/reject", new { reason = reason.Trim() }, ct: ct);

    public Task<JsonElement> UpdateCompanyStatusAsync(Guid companyId, string status, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Patch, $"api/v1/platform/company-registrations/{companyId}/status", new { status }, ct: ct);

    // ========================================================================
    // 3. TASKS & TASK LIFECYCLE
    // ========================================================================
    public Task<TaskPageDto> GetTasksAsync(int page = 1, int pageSize = 25, string? search = null, string? status = null, string? priority = null, Guid? requestId = null, Guid? parentTaskId = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(
            ("page", page.ToString()),
            ("pageSize", pageSize.ToString()),
            ("search", search),
            ("status", status),
            ("priority", priority),
            ("requestId", requestId?.ToString()),
            ("parentTaskId", parentTaskId?.ToString()));
        return SendAsync<TaskPageDto>(HttpMethod.Get, $"api/v1/tasks{qs}", ct: ct);
    }

    public Task<TaskDetailDto> GetTaskDetailAsync(Guid taskId, int checklistPage = 1, int reportPage = 1, int resultPage = 1, CancellationToken ct = default)
    {
        var qs = BuildQuery(
            ("checklistPage", checklistPage.ToString()),
            ("reportPage", reportPage.ToString()),
            ("resultPage", resultPage.ToString()),
            ("pageSize", "25"));
        return SendAsync<TaskDetailDto>(HttpMethod.Get, $"api/v1/tasks/{taskId}{qs}", ct: ct);
    }

    public Task<JsonElement> CreateTaskAsync(string title, string? description, string priority, DateTimeOffset? deadline, string[]? checklist, Guid? requestId = null, Guid? parentTaskId = null, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/v1/tasks",
            new
            {
                title = title.Trim(),
                description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                priority,
                deadline = deadline?.ToUniversalTime().ToString("O"),
                checklist = checklist is { Length: > 0 } ? checklist : null,
                requestId,
                parentTaskId
            },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> AssignTaskAsync(Guid taskId, string targetType, Guid targetId, string? note, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/tasks/{taskId}/assign",
            new
            {
                targetType = targetType.ToUpperInvariant(),
                targetId,
                note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> AcceptTaskAsync(Guid taskId, string? note, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/tasks/{taskId}/accept",
            new { note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> StartTaskAsync(Guid taskId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/tasks/{taskId}/start",
            new { },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> SubmitTaskResultAsync(Guid taskId, string content, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/tasks/{taskId}/result",
            new { content = content.Trim() },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> ConfirmTaskResultAsync(Guid taskId, string decision, string? note, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/tasks/{taskId}/confirm",
            new
            {
                decision = decision.ToUpperInvariant(),
                note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    // ========================================================================
    // 4. REQUESTS & REQUEST LIFECYCLE
    // ========================================================================
    public Task<RequestPageDto> GetRequestsAsync(int page = 1, int pageSize = 25, string? search = null, string? status = null, string? priority = null, Guid? serviceId = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(
            ("page", page.ToString()),
            ("pageSize", pageSize.ToString()),
            ("search", search),
            ("status", status),
            ("priority", priority),
            ("serviceId", serviceId?.ToString()));
        return SendAsync<RequestPageDto>(HttpMethod.Get, $"api/v1/requests{qs}", ct: ct);
    }

    public Task<RequestDetailDto> GetRequestDetailAsync(Guid requestId, CancellationToken ct = default) =>
        SendAsync<RequestDetailDto>(HttpMethod.Get, $"api/v1/requests/{requestId}", ct: ct);

    public Task<JsonElement> CreateRequestAsync(Guid serviceId, Guid categoryId, string title, string? description, string priority, bool submitImmediately, Guid? parentRequestId = null, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/v1/requests",
            new
            {
                serviceId,
                categoryId,
                title = title.Trim(),
                description = string.IsNullOrWhiteSpace(description) ? "" : description.Trim(),
                priority,
                parentRequestId,
                submitImmediately
            },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> SubmitRequestAsync(Guid requestId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/submit", new { }, idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> RouteRequestAsync(Guid requestId, Guid? toDepartmentId, Guid? toUserId, string? reason, string source = "MANUAL", CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/route",
            new
            {
                toDepartmentId,
                toUserId,
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                source
            },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> ReceiveRequestAsync(Guid requestId, string? note, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/receive",
            new { note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> StartRequestProcessingAsync(Guid requestId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/start", new { }, idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> ResolveRequestAsync(Guid requestId, string content, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/resolve",
            new { content = content.Trim() },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> ConfirmRequestAsync(Guid requestId, string decision, string? note, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/confirm",
            new
            {
                decision = decision.ToUpperInvariant(),
                note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> RejectRequestAsync(Guid requestId, string reason, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/reject",
            new { reason = reason.Trim() },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> CancelRequestAsync(Guid requestId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/cancel", new { }, idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> ReviseRequestAsync(Guid requestId, string title, string? description, string priority, bool submitImmediately, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/revise",
            new
            {
                title = title.Trim(),
                description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                priority,
                submitImmediately
            },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public Task<JsonElement> CreateTaskForRequestAsync(Guid requestId, string title, string? description, string priority, DateTimeOffset? deadline, string[]? checklist, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/requests/{requestId}/tasks",
            new
            {
                title = title.Trim(),
                description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                priority,
                deadline = deadline?.ToUniversalTime().ToString("O"),
                checklist = checklist is { Length: > 0 } ? checklist : null
            },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    // ========================================================================
    // 5. COMMENTS, ATTACHMENTS & RECORD ARCHIVAL / SEARCH
    // ========================================================================
    public Task<IReadOnlyList<CommentItemDto>> GetCommentsAsync(string objectType, Guid objectId, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<CommentItemDto>>(HttpMethod.Get, $"api/v1/comments?objectType={Uri.EscapeDataString(objectType)}&objectId={objectId}", ct: ct);

    public Task<CommentItemDto> AddCommentAsync(string objectType, Guid objectId, string content, CancellationToken ct = default) =>
        SendAsync<CommentItemDto>(HttpMethod.Post, "api/v1/comments",
            new { objectType = objectType.ToUpperInvariant(), objectId, content = content.Trim() },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

    public async Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default) =>
        await SendNoContentAsync(HttpMethod.Delete, $"api/v1/comments/{commentId}", ct: ct);

    public Task<IReadOnlyList<AttachmentItemDto>> GetAttachmentsAsync(string objectType, Guid objectId, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<AttachmentItemDto>>(HttpMethod.Get, $"api/v1/attachments?objectType={Uri.EscapeDataString(objectType)}&objectId={objectId}", ct: ct);

    public async Task<AttachmentItemDto> UploadAttachmentAsync(string objectType, Guid objectId, string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        var session = await SendAsync<UploadSessionResultDto>(HttpMethod.Post, "api/v1/attachments/upload-session",
            new
            {
                objectType = objectType.ToUpperInvariant(),
                objectId,
                fileName = fileInfo.Name,
                contentType = GetMimeType(filePath),
                sizeBytes = fileInfo.Length
            },
            idempotencyKey: Guid.NewGuid().ToString(), ct: ct);

        await using (var fs = File.OpenRead(filePath))
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, $"api/v1/attachments/upload-session/{session.AttachmentId}/binary")
            {
                Content = new StreamContent(fs)
            };
            AttachAuthHeader(req);
            using var resp = await _http.SendAsync(req, ct);
            await EnsureSuccessOrThrowAsync(resp, ct);
        }

        return await SendAsync<AttachmentItemDto>(HttpMethod.Post, "api/v1/attachments/finalize",
            new { attachmentId = session.AttachmentId, clientHash = (string?)null }, ct: ct);
    }

    public async Task DownloadAttachmentAsync(Guid attachmentId, string destinationFilePath, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"api/v1/attachments/{attachmentId}/download");
        AttachAuthHeader(req);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessOrThrowAsync(resp, ct);
        await using var outStream = File.Create(destinationFilePath);
        await resp.Content.CopyToAsync(outStream, ct);
    }

    public async Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken ct = default) =>
        await SendNoContentAsync(HttpMethod.Delete, $"api/v1/attachments/{attachmentId}", ct: ct);

    public Task<JsonElement> ArchiveRecordAsync(string type, Guid id, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/records/{type.ToLowerInvariant()}/{id}/archive", null, ct: ct);

    public Task<RecordSearchPageDto> SearchRecordsAsync(string? query, string? type, string? status, string? priority, bool includeArchived, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var qs = BuildQuery(
            ("q", query),
            ("type", string.Equals(type, "ALL", StringComparison.OrdinalIgnoreCase) ? null : type),
            ("status", status),
            ("priority", priority),
            ("includeArchived", includeArchived ? "true" : "false"),
            ("page", page.ToString()),
            ("pageSize", pageSize.ToString()));
        return SendAsync<RecordSearchPageDto>(HttpMethod.Get, $"api/v1/records/search{qs}", ct: ct);
    }

    // ========================================================================
    // 6. WORKFLOWS & WORKFLOW DESIGNER
    // ========================================================================
    public Task<WorkflowPageDto> GetWorkflowsAsync(int page = 1, int pageSize = 25, string? search = null, string? businessType = null, string? status = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(("page", page.ToString()), ("pageSize", pageSize.ToString()), ("search", search), ("businessType", businessType), ("status", status));
        return SendAsync<WorkflowPageDto>(HttpMethod.Get, $"api/v1/workflows{qs}", ct: ct);
    }

    public Task<WorkflowRowDto> CreateWorkflowAsync(string name, string businessType, CancellationToken ct = default) =>
        SendAsync<WorkflowRowDto>(HttpMethod.Post, "api/v1/workflows", new { name = name.Trim(), businessType }, ct: ct);

    public Task<WorkflowRowDto> UpdateWorkflowStatusAsync(Guid workflowId, string status, CancellationToken ct = default) =>
        SendAsync<WorkflowRowDto>(HttpMethod.Patch, $"api/v1/workflows/{workflowId}/status", new { status }, ct: ct);

    public async Task DeleteWorkflowAsync(Guid workflowId, CancellationToken ct = default) =>
        await SendNoContentAsync(HttpMethod.Delete, $"api/v1/workflows/{workflowId}", ct: ct);

    public Task<IReadOnlyList<WorkflowVersionSummaryDto>> GetWorkflowVersionsAsync(Guid workflowId, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<WorkflowVersionSummaryDto>>(HttpMethod.Get, $"api/v1/workflows/{workflowId}/versions", ct: ct);

    public Task<WorkflowRowDto> CreateNextWorkflowVersionAsync(Guid workflowId, CancellationToken ct = default) =>
        SendAsync<WorkflowRowDto>(HttpMethod.Post, $"api/v1/workflows/{workflowId}/versions", new { }, ct: ct);

    public Task<WorkflowVersionViewDto> GetWorkflowVersionDetailAsync(Guid versionId, CancellationToken ct = default) =>
        SendAsync<WorkflowVersionViewDto>(HttpMethod.Get, $"api/v1/workflow-versions/{versionId}", ct: ct);

    public Task<WorkflowVersionViewDto> SaveWorkflowDraftAsync(Guid versionId, string definitionJson, object? steps, object? transitions, string? name, CancellationToken ct = default) =>
        SendAsync<WorkflowVersionViewDto>(HttpMethod.Put, $"api/v1/workflow-versions/{versionId}",
            new { definitionJson, steps, transitions, name }, ct: ct);

    public Task<WorkflowValidationResultDto> ValidateWorkflowVersionAsync(Guid versionId, CancellationToken ct = default) =>
        SendAsync<WorkflowValidationResultDto>(HttpMethod.Post, $"api/v1/workflow-versions/{versionId}/validate", new { }, ct: ct);

    public Task<WorkflowVersionViewDto> PublishWorkflowVersionAsync(Guid versionId, Guid? serviceId = null, CancellationToken ct = default) =>
        SendAsync<WorkflowVersionViewDto>(HttpMethod.Post, $"api/v1/workflow-versions/{versionId}/publish", new { serviceId }, ct: ct);

    public Task<WorkflowDraftGraphDto> AnalyzeWorkflowTextAsync(string textContent, string businessType = "REQUEST", CancellationToken ct = default) =>
        SendAsync<WorkflowDraftGraphDto>(HttpMethod.Post, "api/v1/workflows/analyze-text", new { textContent, businessType }, ct: ct);

    public Task<IReadOnlyList<WorkflowLookupItemDto>> GetWorkflowDepartmentsLookupAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<WorkflowLookupItemDto>>(HttpMethod.Get, "api/v1/workflows/lookup/departments", ct: ct);

    public Task<IReadOnlyList<WorkflowLookupItemDto>> GetWorkflowRolesLookupAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<WorkflowLookupItemDto>>(HttpMethod.Get, "api/v1/workflows/lookup/roles", ct: ct);

    public Task<IReadOnlyList<WorkflowUserLookupDto>> GetWorkflowUsersLookupAsync(Guid? departmentId = null, CancellationToken ct = default)
    {
        var qs = departmentId.HasValue ? $"?departmentId={departmentId}" : "";
        return SendAsync<IReadOnlyList<WorkflowUserLookupDto>>(HttpMethod.Get, $"api/v1/workflows/lookup/users{qs}", ct: ct);
    }

    public Task<IReadOnlyList<WorkflowServiceLookupDto>> GetWorkflowServicesLookupAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<WorkflowServiceLookupDto>>(HttpMethod.Get, "api/v1/workflows/lookup/services", ct: ct);

    // ========================================================================
    // 7. ORGANIZATION: DEPARTMENTS, USERS, ROLES, SERVICES, SLA PROFILES
    // ========================================================================
    public Task<DepartmentPageDto> GetDepartmentsAsync(int page = 1, int pageSize = 25, string? status = null, string? search = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(("page", page.ToString()), ("pageSize", pageSize.ToString()), ("status", status), ("search", search));
        return SendAsync<DepartmentPageDto>(HttpMethod.Get, $"api/v1/departments{qs}", ct: ct);
    }

    public Task<JsonElement> CreateDepartmentAsync(string code, string name, Guid? parentDepartmentId = null, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/v1/departments",
            new { code = code.Trim().ToUpperInvariant(), name = name.Trim(), parentDepartmentId }, ct: ct);

    public Task<JsonElement> UpdateDepartmentAsync(Guid departmentId, string code, string name, string status, Guid? parentDepartmentId = null, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Put, $"api/v1/departments/{departmentId}",
            new { code = code.Trim().ToUpperInvariant(), name = name.Trim(), status, parentDepartmentId }, ct: ct);

    public Task<UserPageDto> GetUsersAsync(int page = 1, int pageSize = 25, string? search = null, string? status = null, Guid? departmentId = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(("page", page.ToString()), ("pageSize", pageSize.ToString()), ("search", search), ("status", status), ("departmentId", departmentId?.ToString()));
        return SendAsync<UserPageDto>(HttpMethod.Get, $"api/v1/users{qs}", ct: ct);
    }

    public Task<JsonElement> CreateUserAsync(string employeeCode, string fullName, string email, string password, Guid? departmentId, string? role, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/v1/users",
            new
            {
                employeeCode = employeeCode.Trim(),
                fullName = fullName.Trim(),
                email = email.Trim(),
                password,
                departmentId,
                role = string.IsNullOrWhiteSpace(role) ? "EMPLOYEE" : role
            }, ct: ct);

    public Task<JsonElement> UpdateUserAsync(Guid userId, string fullName, Guid? departmentId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Put, $"api/v1/users/{userId}",
            new { fullName = fullName.Trim(), departmentId }, ct: ct);

    public Task<JsonElement> UpdateUserStatusAsync(Guid userId, string status, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Patch, $"api/v1/users/{userId}/status", new { status }, ct: ct);

    public Task<UserResponsibilitiesDto> GetUserResponsibilitiesAsync(Guid userId, CancellationToken ct = default) =>
        SendAsync<UserResponsibilitiesDto>(HttpMethod.Get, $"api/v1/users/{userId}/responsibilities", ct: ct);

    public Task<JsonElement> PromoteUserToManagerAsync(Guid userId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/users/{userId}/promote-manager", new { }, ct: ct);

    public Task<JsonElement> DemoteUserToEmployeeAsync(Guid userId, Guid? transferToUserId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/users/{userId}/demote-employee", new { transferToUserId }, ct: ct);

    public Task<JsonElement> TransferAdminOwnershipAsync(Guid targetUserId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/v1/users/{targetUserId}/transfer-ownership", new { targetUserId }, ct: ct);

    public Task<BatchImportUsersResultDto> BatchImportUsersAsync(object rows, bool dryRun, CancellationToken ct = default) =>
        SendAsync<BatchImportUsersResultDto>(HttpMethod.Post, "api/v1/users/batch-import", new { users = rows, dryRun }, ct: ct);

    public Task<RolePageDto> GetRolesAsync(int page = 1, int pageSize = 25, string? search = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(("page", page.ToString()), ("pageSize", pageSize.ToString()), ("search", search));
        return SendAsync<RolePageDto>(HttpMethod.Get, $"api/v1/roles{qs}", ct: ct);
    }

    public Task<RoleRowDto> CreateRoleAsync(string name, CancellationToken ct = default) =>
        SendAsync<RoleRowDto>(HttpMethod.Post, "api/v1/roles", new { name = name.Trim() }, ct: ct);

    public Task<RoleRowDto> SetRolePermissionsAsync(Guid roleId, string ifMatchETag, Guid[] permissionIds, CancellationToken ct = default) =>
        SendAsync<RoleRowDto>(HttpMethod.Put, $"api/v1/roles/{roleId}/permissions", new { permissionIds }, ifMatch: ifMatchETag, ct: ct);

    public Task<ServicePageDto> GetServicesAsync(int page = 1, int pageSize = 25, string? search = null, string? status = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(("page", page.ToString()), ("pageSize", pageSize.ToString()), ("search", search), ("status", status));
        return SendAsync<ServicePageDto>(HttpMethod.Get, $"api/v1/services{qs}", ct: ct);
    }

    public Task<ServiceRowDto> CreateServiceAsync(string code, string name, string? description, bool active, object[]? categories = null, CancellationToken ct = default) =>
        SendAsync<ServiceRowDto>(HttpMethod.Post, "api/v1/services",
            new
            {
                code = code.Trim(),
                name = name.Trim(),
                description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                active,
                categories
            }, ct: ct);

    public Task<ServiceRowDto> AddServiceCategoryAsync(Guid serviceId, string ifMatchETag, string code, string name, bool active = true, CancellationToken ct = default) =>
        SendAsync<ServiceRowDto>(HttpMethod.Post, $"api/v1/services/{serviceId}/categories",
            new { code = code.Trim(), name = name.Trim(), active }, ifMatch: ifMatchETag, ct: ct);

    public Task<ServiceRowDto> UpdateServiceMetadataAsync(Guid serviceId, string ifMatchETag, string code, string name, string? description, bool active, CancellationToken ct = default) =>
        SendAsync<ServiceRowDto>(HttpMethod.Put, $"api/v1/services/{serviceId}",
            new
            {
                code = code.Trim(),
                name = name.Trim(),
                description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                active
            }, ifMatch: ifMatchETag, ct: ct);

    public Task<SlaProfilePageDto> GetSlaProfilesAsync(int page = 1, int pageSize = 25, string? search = null, string? status = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(("page", page.ToString()), ("pageSize", pageSize.ToString()), ("search", search), ("status", status));
        return SendAsync<SlaProfilePageDto>(HttpMethod.Get, $"api/v1/sla-profiles{qs}", ct: ct);
    }

    public Task<SlaProfileRowDto> CreateSlaProfileAsync(string name, CancellationToken ct = default) =>
        SendAsync<SlaProfileRowDto>(HttpMethod.Post, "api/v1/sla-profiles", new { name = name.Trim() }, ct: ct);

    public Task<SlaVersionHistoryPageDto> GetSlaVersionHistoryAsync(Guid profileId, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        SendAsync<SlaVersionHistoryPageDto>(HttpMethod.Get, $"api/v1/sla-profiles/{profileId}/versions?page={page}&pageSize={pageSize}", ct: ct);

    // ========================================================================
    // 8. DASHBOARD, REPORTS, NOTIFICATIONS & AUDIT LOGS
    // ========================================================================
    public Task<ManagerDashboardResultDto> GetManagerDashboardAsync(Guid? departmentId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(
            ("departmentId", departmentId?.ToString()),
            ("from", from?.ToUniversalTime().ToString("O")),
            ("to", to?.ToUniversalTime().ToString("O")));
        return SendAsync<ManagerDashboardResultDto>(HttpMethod.Get, $"api/v1/dashboard/manager{qs}", ct: ct);
    }

    public Task<CompanyReportResultDto> GetCompanyReportAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(("from", from?.ToUniversalTime().ToString("O")), ("to", to?.ToUniversalTime().ToString("O")));
        return SendAsync<CompanyReportResultDto>(HttpMethod.Get, $"api/v1/reports/company{qs}", ct: ct);
    }

    public Task<WorkloadReportResultDto> GetWorkloadReportAsync(Guid? departmentId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(
            ("departmentId", departmentId?.ToString()),
            ("from", from?.ToUniversalTime().ToString("O")),
            ("to", to?.ToUniversalTime().ToString("O")));
        return SendAsync<WorkloadReportResultDto>(HttpMethod.Get, $"api/v1/reports/workload{qs}", ct: ct);
    }

    public Task<SlaPerformanceReportResultDto> GetSlaReportAsync(Guid? serviceId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(
            ("serviceId", serviceId?.ToString()),
            ("from", from?.ToUniversalTime().ToString("O")),
            ("to", to?.ToUniversalTime().ToString("O")));
        return SendAsync<SlaPerformanceReportResultDto>(HttpMethod.Get, $"api/v1/reports/sla{qs}", ct: ct);
    }

    public Task<NotificationPageDto> GetNotificationsAsync(int page = 1, int pageSize = 25, bool unreadOnly = false, CancellationToken ct = default) =>
        SendAsync<NotificationPageDto>(HttpMethod.Get, $"api/v1/notifications?page={page}&pageSize={pageSize}&unreadOnly={(unreadOnly ? "true" : "false")}", ct: ct);

    public Task<NotificationRowDto> MarkNotificationReadAsync(Guid notificationId, CancellationToken ct = default) =>
        SendAsync<NotificationRowDto>(HttpMethod.Patch, $"api/v1/notifications/{notificationId}/read", new { }, ct: ct);

    public Task<AuditPageDto> GetAuditLogsAsync(int page = 1, int pageSize = 25, string? action = null, string? actorType = null, Guid? actorId = null, string? objectType = null, Guid? objectId = null, string? from = null, string? until = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(
            ("page", page.ToString()),
            ("pageSize", pageSize.ToString()),
            ("action", action),
            ("actorType", actorType),
            ("actorId", actorId?.ToString()),
            ("objectType", objectType),
            ("objectId", objectId?.ToString()),
            ("from", from),
            ("until", until));
        return SendAsync<AuditPageDto>(HttpMethod.Get, $"api/v1/audit-logs{qs}", ct: ct);
    }

    // ========================================================================
    // 9. AI ASSISTANCE, ROBOT CHAT, DECOMPOSITION, SUMMARIZER, PROACTIVE
    // ========================================================================
    public Task<TaskDraftSuggestionDto> AssistTaskCreationAsync(string prompt, CancellationToken ct = default) =>
        SendAsync<TaskDraftSuggestionDto>(HttpMethod.Post, "api/v1/ai/task-assistance", new { prompt = prompt.Trim() }, ct: ct);

    public Task<TaskBreakdownResultDto> BreakdownTaskAsync(string title, string? description, int targetSubtaskCount = 4, CancellationToken ct = default) =>
        SendAsync<TaskBreakdownResultDto>(HttpMethod.Post, "api/v1/ai/task-breakdown",
            new { title = title.Trim(), description = description ?? "", targetSubtaskCount }, ct: ct);

    public Task<TaskAssignmentRecommendationDto> RecommendTaskAssignmentAsync(string title, string? description, Guid? departmentId = null, CancellationToken ct = default) =>
        SendAsync<TaskAssignmentRecommendationDto>(HttpMethod.Post, "api/v1/ai/task-assignment-recommendation",
            new { title, description = description ?? "", departmentId }, ct: ct);

    public Task<TaskRiskEvaluationDto> EvaluateTaskRiskAsync(Guid taskId, CancellationToken ct = default) =>
        SendAsync<TaskRiskEvaluationDto>(HttpMethod.Post, "api/v1/ai/task-risk", new { taskId }, ct: ct);

    public Task<TaskSummaryDto> SummarizeTaskProgressAsync(Guid taskId, CancellationToken ct = default) =>
        SendAsync<TaskSummaryDto>(HttpMethod.Post, "api/v1/ai/task-summary", new { taskId }, ct: ct);

    public Task<JsonElement> RecommendRequestRoutingAsync(Guid requestId, string title, string? description, Guid serviceId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/v1/ai/request-routing",
            new { requestId, title, description = description ?? "", serviceId }, ct: ct);

    public Task<RobotChatResponseDto> SendRobotChatMessageAsync(string message, string? sessionId = null, CancellationToken ct = default) =>
        SendAsync<RobotChatResponseDto>(HttpMethod.Post, "api/v1/ai/chat",
            new { message = message.Trim(), sessionId }, ct: ct);

    public Task<TaskDecompositionPreviewResultDto> PreviewTaskDecompositionAsync(string prompt, CancellationToken ct = default) =>
        SendAsync<TaskDecompositionPreviewResultDto>(HttpMethod.Post, "api/v1/tasks/ai-decomposition/preview",
            new { prompt = prompt.Trim() }, ct: ct);

    public Task<ConfirmTaskDecompositionResponseDto> ConfirmTaskDecompositionAsync(object items, CancellationToken ct = default) =>
        SendAsync<ConfirmTaskDecompositionResponseDto>(HttpMethod.Post, "api/v1/tasks/ai-decomposition/confirm",
            new { items }, ct: ct);

    public Task<DocumentSummarizeResultDto> SummarizeDocumentAsync(string documentTitle, string content, CancellationToken ct = default) =>
        SendAsync<DocumentSummarizeResultDto>(HttpMethod.Post, "api/v1/ai/documents/summarize",
            new { documentTitle = documentTitle.Trim(), content = content.Trim() }, ct: ct);

    public Task<IReadOnlyList<ProactiveInsightItemDto>> GetProactiveInsightsAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<ProactiveInsightItemDto>>(HttpMethod.Get, "api/v1/ai/proactive/insights", ct: ct);

    public Task<JsonElement> TriggerProactiveScanAsync(CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/v1/ai/proactive/trigger-scan", new { }, ct: ct);

    public Task<ReflectionMetricsResultDto> GetReflectionMetricsAsync(CancellationToken ct = default) =>
        SendAsync<ReflectionMetricsResultDto>(HttpMethod.Get, "api/v1/ai/reflection/metrics", ct: ct);

    // ========================================================================
    // CORE HTTP PIPELINE WITH AUTO-REFRESH & ERROR ENVELOPE PARSING
    // ========================================================================
    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body = null,
        bool requireAuth = true,
        string? idempotencyKey = null,
        string? ifMatch = null,
        CancellationToken ct = default)
    {
        using var response = await ExecuteWithRefreshRetryAsync(method, relativeUrl, body, requireAuth, idempotencyKey, ifMatch, ct);
        await EnsureSuccessOrThrowAsync(response, ct);

        if (typeof(T) == typeof(JsonElement))
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(raw))
                return (T)(object)JsonDocument.Parse("{}").RootElement.Clone();
            using var doc = JsonDocument.Parse(raw);
            return (T)(object)doc.RootElement.Clone();
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result!;
    }

    private async Task SendNoContentAsync(HttpMethod method, string relativeUrl, object? body = null, CancellationToken ct = default)
    {
        using var response = await ExecuteWithRefreshRetryAsync(method, relativeUrl, body, requireAuth: true, idempotencyKey: null, ifMatch: null, ct: ct);
        await EnsureSuccessOrThrowAsync(response, ct);
    }

    private async Task<HttpResponseMessage> ExecuteWithRefreshRetryAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        bool requireAuth,
        string? idempotencyKey,
        string? ifMatch,
        CancellationToken ct)
    {
        using var request = CreateRequestMessage(method, relativeUrl, body, requireAuth, idempotencyKey, ifMatch);
        var response = await _http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized && requireAuth && !string.IsNullOrWhiteSpace(_session.RefreshToken))
        {
            response.Dispose();
            var refreshed = await TryRefreshSessionAsync(ct);
            if (refreshed)
            {
                using var retryRequest = CreateRequestMessage(method, relativeUrl, body, requireAuth, idempotencyKey, ifMatch);
                return await _http.SendAsync(retryRequest, ct);
            }
        }

        return response;
    }

    private HttpRequestMessage CreateRequestMessage(
        HttpMethod method,
        string relativeUrl,
        object? body,
        bool requireAuth,
        string? idempotencyKey,
        string? ifMatch)
    {
        var req = new HttpRequestMessage(method, relativeUrl);
        if (requireAuth)
        {
            AttachAuthHeader(req);
        }
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return req;
    }

    private void AttachAuthHeader(HttpRequestMessage req)
    {
        var token = _session.AccessToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<bool> TryRefreshSessionAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var refreshToken = _session.RefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
                return false;

            using var req = CreateRequestMessage(HttpMethod.Post, "api/v1/auth/refresh", new { refreshToken }, requireAuth: false, idempotencyKey: null, ifMatch: null);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _session.Clear();
                return false;
            }

            var updated = await resp.Content.ReadFromJsonAsync<LoginResponseDto>(JsonOptions, ct);
            if (updated is null)
            {
                _session.Clear();
                return false;
            }

            _session.SetSession(updated);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var code = $"HTTP_{(int)response.StatusCode}";
        var message = $"Yêu cầu không thành công ({(int)response.StatusCode}).";

        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
                        code = c.GetString() ?? code;
                    if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                        message = m.GetString() ?? message;
                    else if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                        message = e.GetString() ?? message;
                    else if (root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                        message = t.GetString() ?? message;
                }
            }
        }
        catch
        {
            // Keep default status code message
        }

        throw new ApiClientException(response.StatusCode, code, message);
    }

    private static string BuildQuery(params (string Key, string? Value)[] parameters)
    {
        var pairs = new List<string>();
        foreach (var (key, val) in parameters)
        {
            if (!string.IsNullOrWhiteSpace(val))
            {
                pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(val.Trim())}");
            }
        }
        return pairs.Count == 0 ? "" : "?" + string.Join("&", pairs);
    }

    private static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
