using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using BizFlow.Desktop.Core;

namespace BizFlow.Desktop.Models;

// ============================================================================
// 1. AUTHENTICATION & SESSION MODELS
// ============================================================================
public sealed record SessionIdentityDto(
    Guid UserId,
    string FullName,
    Guid? TenantId,
    string? TenantName,
    string[] Permissions,
    string? AvatarUrl = null,
    string? EmployeeCode = null,
    string? Email = null);

public sealed record LoginResponseDto(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    SessionIdentityDto Session);

public sealed record WorkspaceCandidateModel(
    Guid? TenantId,
    string TenantKey,
    string TenantName);

public sealed record EmailLinkRequestResponseDto(
    string Message,
    int ExpiresInMinutes);

public sealed record EmailLinkVerificationResponseDto(
    string Status,
    string? Message,
    string? RegisterCompanyUrl,
    string? Ticket,
    IReadOnlyList<WorkspaceCandidateModel>? Workspaces,
    LoginResponseDto? Authentication);

public sealed record CheckCodeResponseDto(
    bool Available,
    string Code,
    string? Message = null);

public sealed record GenerateCodeResponseDto(string Code);

public sealed record UploadCompanyDocumentResultDto(
    string DocumentKey,
    string FileName,
    long SizeBytes);

public sealed record SubmitCompanyRegistrationResponseDto(
    Guid CompanyId,
    string Code,
    string Name,
    string TaxId,
    string ContactPersonName,
    string ContactEmail,
    string ContactPhone,
    string Status,
    DateTimeOffset CreatedAt,
    string? EvidenceDocumentKey = null,
    string? EvidenceDocumentFileName = null);

// ============================================================================
// 2. DASHBOARD & WORKSPACE KPI MODELS
// ============================================================================
public sealed class KpiCardModel : ObservableObject
{
    private string _title = "";
    private string _badge = "";
    private string _metric = "0";
    private string _sublabel = "";
    private string _targetView = "Workspace";
    private string _actionLabel = "";
    private string _accentColor = "#17436F";

    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Badge { get => _badge; set => SetProperty(ref _badge, value); }
    public string Metric { get => _metric; set => SetProperty(ref _metric, value); }
    public string Sublabel { get => _sublabel; set => SetProperty(ref _sublabel, value); }
    public string TargetView { get => _targetView; set => SetProperty(ref _targetView, value); }
    public string ActionLabel { get => _actionLabel; set => SetProperty(ref _actionLabel, value); }
    public string AccentColor { get => _accentColor; set => SetProperty(ref _accentColor, value); }
}

public sealed record ModuleCardModel(
    string Icon,
    string Title,
    string Description,
    string ActionLabel,
    string TargetView);

// ============================================================================
// 3. TASK MANAGEMENT MODELS
// ============================================================================
public sealed record TaskRowDto(
    Guid TaskId,
    string Title,
    string Status,
    string Priority,
    DateTimeOffset? Deadline,
    Guid? AssignedUserId,
    string? AssignedUserName,
    Guid? AssignedDepartmentId,
    string? AssignedDepartmentName,
    Guid? RequestId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ParentTaskId = null,
    int SubtaskCount = 0,
    int CompletedSubtaskCount = 0)
{
    public string AssigneeDisplay =>
        !string.IsNullOrWhiteSpace(AssignedUserName) ? $"👤 {AssignedUserName}"
        : !string.IsNullOrWhiteSpace(AssignedDepartmentName) ? $"🏢 {AssignedDepartmentName}"
        : "Chưa phân công";

    public string DeadlineDisplay => Deadline?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Không thời hạn";

    public string SubtaskSummary => SubtaskCount > 0 ? $"{CompletedSubtaskCount}/{SubtaskCount} nhiệm vụ con" : "";
}

public sealed record TaskPageDto(
    IReadOnlyList<TaskRowDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record TaskChecklistDto(
    Guid Id,
    string Title,
    int SortOrder,
    bool IsCompleted);

public sealed record TaskReportDto(
    Guid Id,
    Guid AuthorId,
    short Percent,
    string? Content,
    DateTimeOffset SubmittedAt);

public sealed record TaskResultDto(
    Guid Id,
    Guid AuthorId,
    int RevisionNo,
    string? Content,
    DateTimeOffset SubmittedAt);

public sealed record TaskEvidencePageDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record TaskDetailDto(
    TaskRowDto Task,
    string? Description,
    string? CreatorName,
    Guid? WorkflowVersionId,
    Guid? SlaVersionId,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt,
    TaskEvidencePageDto<TaskChecklistDto> Checklist,
    TaskEvidencePageDto<TaskReportDto> Reports,
    TaskEvidencePageDto<TaskResultDto> Results,
    IReadOnlyList<TaskRowDto>? Subtasks = null,
    string? ParentTaskTitle = null);

public sealed record TaskDraftSuggestionDto(
    string Title,
    string Description,
    string Priority,
    IReadOnlyList<string> ChecklistItems,
    double Confidence,
    string Reasoning);

public sealed record TaskBreakdownSubtaskDto(
    string Title,
    string? Description,
    string? Priority,
    int? EstimatedHours);

public sealed record TaskBreakdownResultDto(
    IReadOnlyList<TaskBreakdownSubtaskDto> Subtasks,
    string? Summary);

public sealed record TaskAssignmentRecommendationDto(
    Guid? RecommendedUserId,
    string? RecommendedUserName,
    Guid? RecommendedDepartmentId,
    string? RecommendedDepartmentName,
    double Confidence,
    string Rationale);

public sealed record TaskRiskEvaluationDto(
    string RiskLevel,
    double RiskScore,
    IReadOnlyList<string> RiskFactors,
    IReadOnlyList<string> Mitigations,
    string Summary);

public sealed record TaskSummaryDto(
    string ExecutiveSummary,
    int CompletionEstimatePercent,
    IReadOnlyList<string> KeyHighlights,
    IReadOnlyList<string> NextSteps);

public sealed class DecompositionCandidateItem : ObservableObject
{
    private bool _isSelected = true;
    private string _title = "";
    private string _description = "";
    private string _type = "Assignment";
    private Guid? _departmentId;
    private string _departmentName = "Phòng Kỹ thuật & CNTT";
    private string _priority = "MEDIUM";
    private int _suggestedDurationDays = 3;
    private string _checklistText = "";
    private string _reasoning = "";

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public Guid? DepartmentId { get => _departmentId; set => SetProperty(ref _departmentId, value); }
    public string DepartmentName { get => _departmentName; set => SetProperty(ref _departmentName, value); }
    public string Priority { get => _priority; set => SetProperty(ref _priority, value); }
    public int SuggestedDurationDays { get => _suggestedDurationDays; set => SetProperty(ref _suggestedDurationDays, value); }
    public string ChecklistText { get => _checklistText; set => SetProperty(ref _checklistText, value); }
    public string Reasoning { get => _reasoning; set => SetProperty(ref _reasoning, value); }
}

// ============================================================================
// 4. REQUESTS & COLLABORATION MODELS
// ============================================================================
public sealed record RequestRowDto(
    Guid RequestId,
    string Title,
    string Status,
    string Priority,
    Guid ServiceId,
    string ServiceName,
    Guid CategoryId,
    string CategoryName,
    Guid RequesterId,
    string RequesterName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string CreatedDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

public sealed record RequestPageDto(
    IReadOnlyList<RequestRowDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record RequestResolutionItemDto(
    Guid ResolutionId,
    int RevisionNo,
    string Content,
    DateTimeOffset ResolvedAt,
    Guid ResolverUserId,
    string ResolverName);

public sealed record RequestConfirmationItemDto(
    Guid ConfirmationId,
    string MilestoneType,
    string Decision,
    string? Note,
    DateTimeOffset DecidedAt,
    Guid ActorUserId,
    string ActorName);

public sealed record RequestDetailDto(
    Guid RequestId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    Guid ServiceId,
    string ServiceName,
    Guid CategoryId,
    string CategoryName,
    Guid RequesterId,
    string RequesterName,
    string? RequesterEmail,
    Guid? ParentRequestId,
    Guid? RevisedFromRequestId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    Guid? CurrentRoutingDepartmentId,
    string? CurrentRoutingDepartmentName,
    Guid? CurrentRoutingUserId,
    string? CurrentRoutingUserName,
    DateTimeOffset? RoutedAt,
    DateTimeOffset? ReceivedAt,
    string? RejectionReason,
    IReadOnlyList<RequestResolutionItemDto>? Resolutions,
    IReadOnlyList<RequestConfirmationItemDto>? Confirmations,
    IReadOnlyList<TaskRowDto>? Tasks);

public sealed record CommentItemDto(
    Guid CommentId,
    string ObjectType,
    Guid ObjectId,
    Guid AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AttachmentItemDto(
    Guid AttachmentId,
    string ObjectType,
    Guid ObjectId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    Guid UploadedBy,
    string? UploadedByName,
    DateTimeOffset CreatedAt)
{
    public string SizeDisplay => SizeBytes >= 1024 * 1024
        ? $"{(SizeBytes / (1024.0 * 1024.0)):F1} MB"
        : $"{Math.Max(1, SizeBytes / 1024)} KB";
}

public sealed record UploadSessionResultDto(
    Guid AttachmentId,
    string ObjectKey,
    string UploadUrl,
    DateTimeOffset ExpiresAt);

// ============================================================================
// 5. APPROVALS CENTER MODELS
// ============================================================================
public sealed record PendingApprovalItemModel(
    Guid Id,
    string ApprovalType,
    string TypeBadgeLabel,
    string Title,
    Guid SourceId,
    string RequesterOrPerformer,
    string? DepartmentName,
    string Priority,
    DateTimeOffset SubmittedAt,
    string Summary,
    string Status)
{
    public string SubmittedDisplay => SubmittedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

// ============================================================================
// 6. WORKFLOWS & VISUAL DESIGNER MODELS
// ============================================================================
public sealed record WorkflowRowDto(
    Guid WorkflowId,
    string Name,
    string BusinessType,
    string Status,
    Guid? LatestVersionId,
    int? LatestVersionNo,
    string? LatestVersionStatus,
    int VersionCount = 1);

public sealed record WorkflowPageDto(
    IReadOnlyList<WorkflowRowDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record WorkflowVersionSummaryDto(
    Guid VersionId,
    int VersionNo,
    string Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);

public sealed record WorkflowStepDto(
    Guid StepId,
    string StepCode,
    string Name,
    string Type,
    int OrderNo,
    JsonElement Config);

public sealed record WorkflowTransitionDto(
    Guid TransitionId,
    string FromState,
    string ToState,
    JsonElement? Guard);

public sealed record WorkflowVersionViewDto(
    Guid WorkflowId,
    string Name,
    string BusinessType,
    Guid VersionId,
    int VersionNo,
    string Status,
    DateTimeOffset? PublishedAt,
    JsonElement Definition,
    IReadOnlyList<WorkflowStepDto> Steps,
    IReadOnlyList<WorkflowTransitionDto> Transitions);

public sealed record WorkflowLookupItemDto(
    Guid Id,
    string Name,
    string? Code = null);

public sealed record WorkflowUserLookupDto(
    Guid Id,
    string FullName,
    string EmployeeCode,
    string Email,
    Guid? DepartmentId = null)
{
    public string DisplayLabel => $"{FullName} ({EmployeeCode})";
}

public sealed record WorkflowServiceLookupDto(
    Guid Id,
    string Name,
    string Code,
    Guid? ActiveWorkflowVersionId);

public sealed class DesignerNodeModel : ObservableObject
{
    private string _id = "";
    private string _type = "DEPARTMENT";
    private string _name = "";
    private double _x = 100;
    private double _y = 120;
    private string? _departmentId;
    private string? _departmentName;
    private string? _roleId;
    private string? _roleName;
    private string _assignmentMethod = "DEPARTMENT_QUEUE";
    private string? _specificUserId;
    private string? _specificUserName;
    private int _slaHours = 24;
    private string? _description;
    private string _approverType = "ROLE";
    private string _approvalCountPolicy = "SINGLE";
    private bool _rejectionReasonRequired = true;
    private string _milestone = "RESULT";
    private string _trigger = "REQUEST_CREATED";
    private string _completionState = "COMPLETED";
    private bool _isSelected;

    public string Id { get => _id; set => SetProperty(ref _id, value); }
    public string Type { get => _type; set { if (SetProperty(ref _type, value)) OnPropertyChanged(nameof(TypeColor)); } }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public double X { get => _x; set => SetProperty(ref _x, value); }
    public double Y { get => _y; set => SetProperty(ref _y, value); }
    public string? DepartmentId { get => _departmentId; set => SetProperty(ref _departmentId, value); }
    public string? DepartmentName { get => _departmentName; set => SetProperty(ref _departmentName, value); }
    public string? RoleId { get => _roleId; set => SetProperty(ref _roleId, value); }
    public string? RoleName { get => _roleName; set => SetProperty(ref _roleName, value); }
    public string AssignmentMethod { get => _assignmentMethod; set => SetProperty(ref _assignmentMethod, value); }
    public string? SpecificUserId { get => _specificUserId; set => SetProperty(ref _specificUserId, value); }
    public string? SpecificUserName { get => _specificUserName; set => SetProperty(ref _specificUserName, value); }
    public int SlaHours { get => _slaHours; set => SetProperty(ref _slaHours, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public string ApproverType { get => _approverType; set => SetProperty(ref _approverType, value); }
    public string ApprovalCountPolicy { get => _approvalCountPolicy; set => SetProperty(ref _approvalCountPolicy, value); }
    public bool RejectionReasonRequired { get => _rejectionReasonRequired; set => SetProperty(ref _rejectionReasonRequired, value); }
    public string Milestone { get => _milestone; set => SetProperty(ref _milestone, value); }
    public string Trigger { get => _trigger; set => SetProperty(ref _trigger, value); }
    public string CompletionState { get => _completionState; set => SetProperty(ref _completionState, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public string TypeColor => Type switch
    {
        "START" => "#059669",
        "END" => "#0F172A",
        "APPROVAL" => "#D97706",
        "DECISION" => "#7C3AED",
        "CONFIRMATION" => "#0284C7",
        _ => "#17436F"
    };
}

public sealed class DesignerEdgeModel : ObservableObject
{
    private string _id = "";
    private string _source = "";
    private string _target = "";
    private string _label = "";
    private string _outcomeCode = "DEFAULT";
    private bool _isDefault = true;

    public string Id { get => _id; set => SetProperty(ref _id, value); }
    public string Source { get => _source; set => SetProperty(ref _source, value); }
    public string Target { get => _target; set => SetProperty(ref _target, value); }
    public string Label { get => _label; set => SetProperty(ref _label, value); }
    public string OutcomeCode { get => _outcomeCode; set => SetProperty(ref _outcomeCode, value); }
    public bool IsDefault { get => _isDefault; set => SetProperty(ref _isDefault, value); }
}

public sealed record WorkflowValidationResultDto(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record WorkflowDraftGraphDto(
    string WorkflowName,
    string BusinessType,
    string SummaryExplanation,
    IReadOnlyList<DesignerNodeModel> Nodes,
    IReadOnlyList<DesignerEdgeModel> Edges);

// ============================================================================
// 7. ORGANIZATION, USERS, ROLES, SERVICES, SLA MODELS
// ============================================================================
public sealed record DepartmentRowDto(
    Guid DepartmentId,
    string Code,
    string Name,
    Guid? ParentDepartmentId,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record DepartmentPageDto(
    IReadOnlyList<DepartmentRowDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record UserRowDto(
    Guid UserId,
    string EmployeeCode,
    string FullName,
    string Email,
    Guid? DepartmentId,
    string? DepartmentName,
    string Status,
    bool IsManager = false,
    string? RoleName = null)
{
    public string RoleDisplay => IsManager ? "👔 Quản lý / Trưởng phòng" : (!string.IsNullOrWhiteSpace(RoleName) ? RoleName : "👤 Nhân viên");
    public string DepartmentDisplay => string.IsNullOrWhiteSpace(DepartmentName) ? "Chưa phân bổ" : DepartmentName;
    public string AssigneeOptionLabel => $"{FullName} ({(IsManager ? "Quản lý" : (!string.IsNullOrWhiteSpace(RoleName) ? RoleName : "Nhân viên"))}{(string.IsNullOrWhiteSpace(DepartmentName) ? "" : $" · {DepartmentName}")})";
}

public sealed record UserPageDto(
    IReadOnlyList<UserRowDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record ReplacementManagerDto(
    Guid UserId,
    string FullName,
    string EmployeeCode)
{
    public string DisplayName => $"{FullName} ({EmployeeCode})";
}

public sealed record UserResponsibilitiesDto(
    Guid UserId,
    string FullName,
    string EmployeeCode,
    Guid? DepartmentId,
    string? DepartmentName,
    bool IsManager,
    bool IsSoleDepartmentManager,
    int ActiveMainTasksCount,
    int ActiveChildTasksCount,
    int PendingApprovalsCount,
    IReadOnlyList<ReplacementManagerDto> EligibleReplacementManagers);

public sealed record BatchImportValidationItemDto(
    int RowIndex,
    string EmployeeCode,
    string Email,
    string? FullName,
    string? DepartmentCode,
    string? Role,
    bool IsValid,
    IReadOnlyList<string>? Errors,
    string? ErrorMessage)
{
    public string StatusDisplay => IsValid ? "HỢP LỆ" : "LỖI";
    public string ErrorSummary => !string.IsNullOrWhiteSpace(ErrorMessage)
        ? ErrorMessage
        : (Errors is { Count: > 0 } ? string.Join(" | ", Errors) : "Sẵn sàng nhập");
}

public sealed record BatchImportUsersResultDto(
    int TotalProcessed,
    int ValidCount,
    int ImportedCount,
    bool DryRun,
    IReadOnlyList<BatchImportValidationItemDto>? Items,
    int TotalRows = 0,
    int ValidRows = 0,
    int ErrorRows = 0,
    int CreatedCount = 0,
    IReadOnlyList<BatchImportValidationItemDto>? ValidationItems = null);

public sealed record PermissionOptionDto(
    Guid PermissionId,
    string Code,
    string Module,
    string Action,
    string Scope);

public sealed class SelectablePermissionItem : ObservableObject
{
    private bool _isChecked;
    public required PermissionOptionDto Permission { get; init; }
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
}

public sealed record RoleRowDto(
    Guid RoleId,
    string Name,
    bool IsSystem,
    string Status,
    IReadOnlyList<Guid> PermissionIds,
    string ETag)
{
    public string TypeBadge => IsSystem ? "Vai trò Hệ thống (Bảo vệ)" : "Vai trò Tùy chỉnh";
}

public sealed record RolePageDto(
    IReadOnlyList<RoleRowDto> Items,
    int Page,
    int PageSize,
    long Total,
    IReadOnlyList<PermissionOptionDto> AvailablePermissions);

public sealed record ServiceCategoryRowDto(
    Guid ServiceCategoryId,
    string Code,
    string Name,
    string Status);

public sealed record ServiceRowDto(
    Guid ServiceId,
    string Code,
    string Name,
    string? Description,
    string Status,
    IReadOnlyList<ServiceCategoryRowDto> Categories,
    Guid? ActiveWorkflowVersionId,
    Guid? ActiveSlaVersionId,
    string ETag)
{
    public string CategoriesSummary => Categories is { Count: > 0 }
        ? string.Join(", ", Categories.Select(c => $"{c.Name} ({c.Code})"))
        : "Chưa có phân loại";
}

public sealed record ServicePageDto(
    IReadOnlyList<ServiceRowDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record SlaProfileRowDto(
    Guid SlaProfileId,
    string Name,
    string Status);

public sealed record SlaProfilePageDto(
    IReadOnlyList<SlaProfileRowDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record SlaVersionRowDto(
    Guid SlaVersionId,
    int VersionNo,
    int TargetMinutes,
    int WarningMinutes,
    Guid CalendarId,
    string? CalendarName,
    DateTimeOffset CreatedAt)
{
    public string TargetDisplay => $"{TargetMinutes} phút ({TargetMinutes / 60.0:F1} giờ)";
    public string WarningDisplay => $"{WarningMinutes} phút ({WarningMinutes / 60.0:F1} giờ)";
}

public sealed record SlaVersionHistoryPageDto(
    Guid SlaProfileId,
    string ProfileName,
    string Status,
    IReadOnlyList<SlaVersionRowDto> Items,
    int Page,
    int PageSize,
    long Total);

// ============================================================================
// 8. REPORTS & EXECUTIVE ANALYTICS MODELS
// ============================================================================
public sealed record TaskMetricsDto(
    int TotalTasks,
    int ActiveTasks,
    int CompletedTasks,
    int OverdueTasks,
    int CancelledTasks,
    Dictionary<string, int>? ByStatus = null,
    Dictionary<string, int>? ByPriority = null);

public sealed record RequestMetricsDto(
    int TotalRequests,
    int PendingRequests,
    int ClosedRequests,
    int RejectedRequests,
    int CancelledRequests,
    Dictionary<string, int>? ByStatus = null,
    Dictionary<string, int>? ByPriority = null);

public sealed record DepartmentWorkloadSummaryDto(
    Guid DepartmentId,
    string DepartmentName,
    int ActiveTasks,
    int CompletedTasks,
    int OverdueTasks,
    int TotalRequests);

public sealed record ManagerDashboardResultDto(
    TaskMetricsDto TaskMetrics,
    RequestMetricsDto RequestMetrics,
    IReadOnlyList<DepartmentWorkloadSummaryDto> DepartmentSummaries,
    DateTimeOffset GeneratedAt);

public sealed record CompanyDepartmentMetricDto(
    Guid DepartmentId,
    string DepartmentName,
    int TotalTasks,
    int CompletedTasks,
    int TotalRequests,
    int ClosedRequests);

public sealed record CompanyReportResultDto(
    int TotalTasksCreated,
    int TotalTasksCompleted,
    double TaskCompletionRatePercent,
    double? AverageTaskCompletionTimeHours,
    int TotalRequestsSubmitted,
    int TotalRequestsClosed,
    double RequestResolutionRatePercent,
    double? AverageRequestResolutionTimeHours,
    IReadOnlyList<CompanyDepartmentMetricDto> DepartmentMetrics,
    int RecentAuditActivityCount,
    DateTimeOffset GeneratedAt);

public sealed record UserWorkloadItemDto(
    Guid UserId,
    string FullName,
    string EmployeeCode,
    Guid? DepartmentId,
    string? DepartmentName,
    int ActiveTasks,
    int CompletedTasks,
    int OverdueTasks,
    int TotalAssigned);

public sealed record DepartmentWorkloadItemDto(
    Guid DepartmentId,
    string DepartmentName,
    int ActiveTasks,
    int TotalMembers,
    double AvgTasksPerMember);

public sealed record WorkloadReportResultDto(
    IReadOnlyList<UserWorkloadItemDto> Items,
    IReadOnlyList<DepartmentWorkloadItemDto> DepartmentBreakdown,
    DateTimeOffset GeneratedAt);

public sealed record ServiceSlaMetricDto(
    Guid ServiceId,
    string ServiceName,
    int TotalRequests,
    int CompliantRequests,
    int BreachedRequests,
    double ComplianceRatePercent);

public sealed record SlaPerformanceReportResultDto(
    int TotalTracked,
    int CompliantCount,
    int BreachedCount,
    double ComplianceRatePercent,
    double? AvgElapsedHours,
    IReadOnlyList<ServiceSlaMetricDto> ServiceBreakdown,
    DateTimeOffset GeneratedAt);

// ============================================================================
// 9. NOTIFICATIONS, AUDIT, SEARCH & PLATFORM REGISTRATION MODELS
// ============================================================================
public sealed record NotificationRowDto(
    Guid NotificationId,
    string Type,
    string? ObjectType,
    Guid? ObjectId,
    string Title,
    string Content,
    DateTimeOffset? ReadAt,
    DateTimeOffset? SentAt)
{
    public bool IsUnread => ReadAt is null;
    public string StatusLabel => ReadAt is null ? "MỚI (CHƯA ĐỌC)" : "ĐÃ ĐỌC";
    public string SentDisplay => SentAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "";
}

public sealed record NotificationPageDto(
    IReadOnlyList<NotificationRowDto> Items,
    int Page,
    int PageSize,
    long Total,
    int UnreadCount);

public sealed record AuditRowDto(
    Guid AuditId,
    Guid? TenantId,
    string ActorType,
    Guid? ActorId,
    string Action,
    string ObjectType,
    Guid? ObjectId,
    JsonElement Metadata,
    DateTimeOffset CreatedAt)
{
    public string CreatedDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public string MetadataPretty => Metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
        ? "{}"
        : JsonSerializer.Serialize(Metadata, new JsonSerializerOptions { WriteIndented = true });
}

public sealed record AuditPageDto(
    IReadOnlyList<AuditRowDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record RecordSearchItemDto(
    Guid Id,
    string RecordType,
    string Title,
    string? Description,
    string Status,
    string Priority,
    Guid? CreatorOrRequesterId,
    string? CreatorOrRequesterName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    bool IsArchived)
{
    public string UpdatedDisplay => UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

public sealed record RecordSearchPageDto(
    IReadOnlyList<RecordSearchItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

public sealed record CompanyRegistrationRowDto(
    Guid CompanyId,
    string Code,
    string Name,
    string? TaxId,
    string? TenantKey,
    string? ContactPersonName,
    string ContactEmail,
    string? ContactPhone,
    string Status,
    DateTimeOffset CreatedAt,
    string? EvidenceDocumentKey,
    string? EvidenceDocumentFileName,
    string? EvidenceDocumentDownloadUrl)
{
    public string CreatedDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public bool IsPending => string.Equals(Status, "PENDING", StringComparison.OrdinalIgnoreCase);
    public bool IsActive => string.Equals(Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
    public bool IsSuspended => string.Equals(Status, "SUSPENDED", StringComparison.OrdinalIgnoreCase);
}

public sealed record CompanyRegistrationPageDto(
    IReadOnlyList<CompanyRegistrationRowDto> Items,
    int Page,
    int PageSize,
    long Total);

// ============================================================================
// 10. AI ROBOT ASSISTANT & PROACTIVE AGENT MODELS
// ============================================================================
public sealed record RobotChatResponseDto(
    string Reply,
    string? Intent,
    IReadOnlyList<DecompositionProposalDto>? TaskProposals = null,
    string? AgentThought = null);

public sealed record DecompositionProposalDto(
    string Title,
    string? Description,
    string? Type,
    Guid? DepartmentId,
    string? DepartmentName,
    string? Priority,
    int? SuggestedDurationDays,
    IReadOnlyList<string>? ChecklistItems,
    string? Reasoning);

public sealed record TaskDecompositionPreviewResultDto(
    string SourcePrompt,
    IReadOnlyList<DecompositionProposalDto> Proposals,
    string? Summary);

public sealed record ConfirmTaskDecompositionResponseDto(
    int CreatedTasksCount,
    int CreatedRequestsCount,
    IReadOnlyList<Guid> CreatedTaskIds,
    IReadOnlyList<Guid> CreatedRequestIds,
    string Message);

public sealed record DocumentSummarizeResultDto(
    string ExecutiveSummary,
    IReadOnlyList<string> KeyPoints,
    IReadOnlyList<DecompositionProposalDto> ActionItems);

public sealed record ProactiveInsightItemDto(
    Guid RecommendationId,
    string RecommendationType,
    string TargetType,
    Guid? TargetId,
    string Summary,
    double Confidence,
    string HumanDecision,
    DateTimeOffset CreatedAt);

public sealed record ReflectionMetricsResultDto(
    int TotalRecommendations,
    int AcceptedCount,
    int OverriddenCount,
    int DismissedCount,
    double AcceptanceRate,
    double ConfidenceAdjustmentFactor,
    IReadOnlyList<string>? ActiveDirectives = null);

public sealed class ChatMessageItem : ObservableObject
{
    public required string Sender { get; init; }
    public required string SenderLabel { get; init; }
    public required string Text { get; init; }
    public required string Time { get; init; }
    public string? AgentThought { get; init; }
    public bool IsUser => Sender == "user";
    public bool IsRobot => Sender != "user";
}
