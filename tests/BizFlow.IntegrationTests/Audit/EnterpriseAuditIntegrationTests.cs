using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BizFlow.Application.AI;
using BizFlow.Application.Authentication;
using BizFlow.Application.Requests;
using BizFlow.Application.Tasks;
using BizFlow.Domain.AI;
using BizFlow.Domain.Organization;
using BizFlow.Domain.Security;
using BizFlow.Domain.Tasks;
using BizFlow.Domain.Services;
using BizFlow.Infrastructure.AI;
using BizFlow.Infrastructure.Persistence;
using BizFlow.IntegrationTests.Authentication;
using BizFlow.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BizFlow.IntegrationTests.Audit;

[AllowAnonymous]
[ApiController, Route("/_audit-probe")]
public sealed class AuditProbeController : ControllerBase
{
    [HttpGet("concurrency")]
    public IActionResult ThrowConcurrency() =>
        throw new DbUpdateConcurrencyException("Simulated concurrent modification.");
}

[Collection("Postgres")]
public sealed class EnterpriseAuditIntegrationTests(PostgresFixture database)
{
    private const string Password = "Audit-Password!2026";

    private sealed record ActorUser(Guid UserId, Guid TenantId, string TenantKey, string EmployeeCode, string Email, HttpClient Client);
    private sealed record AuditEnv(
        ActorUser Admin,
        ActorUser Manager,
        ActorUser OpsEmployee,
        ActorUser Requester,
        ActorUser OutsiderEmployee,
        Guid ItDeptId,
        Guid OpsDeptId,
        Guid ServiceId,
        Guid CategoryId);

    [Fact]
    public async Task Full_EndToEnd_Request_ParentAndSubtask_Rework_Confirm_Notification_And_Archive()
    {
        await using var factory = new AuthenticationFactory(database);
        var env = await SeedEnvironmentAsync(factory);

        // 1. Requester creates a DRAFT request and then submits it
        var createReqRes = await env.Requester.Client.PostAsJsonAsync("/api/v1/requests", new
        {
            serviceId = env.ServiceId,
            categoryId = env.CategoryId,
            title = "Cấp máy trạm cho dự án mới",
            description = "Yêu cầu trang bị máy trạm cấu hình cao",
            priority = "HIGH",
            submitImmediately = false
        });
        Assert.Equal(HttpStatusCode.Created, createReqRes.StatusCode);
        var draftReq = (await createReqRes.Content.ReadFromJsonAsync<RequestCreatedView>())!;
        Assert.Equal("DRAFT", draftReq.Status);

        var submitReqRes = await env.Requester.Client.PostAsync($"/api/v1/requests/{draftReq.RequestId}/submit", JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.OK, submitReqRes.StatusCode);

        // Negative: cannot receive before routing
        var prematureReceive = await env.OpsEmployee.Client.PostAsJsonAsync($"/api/v1/requests/{draftReq.RequestId}/receive", new { note = "Early" });
        Assert.Equal(HttpStatusCode.Conflict, prematureReceive.StatusCode);

        // 2. Manager routes SUBMITTED request to Ops department (no ToUserId) -> ROUTED
        var routeRes = await env.Manager.Client.PostAsJsonAsync($"/api/v1/requests/{draftReq.RequestId}/route", new
        {
            toDepartmentId = env.OpsDeptId,
            toUserId = (Guid?)null,
            reason = "Chuyển phòng Vận hành xử lý",
            source = "MANUAL"
        });
        Assert.True(routeRes.StatusCode == HttpStatusCode.OK, $"Route failed: {routeRes.StatusCode} {await routeRes.Content.ReadAsStringAsync()} | {factory.LastServerError}");

        // 3. Ops employee receives and starts processing -> RECEIVED -> IN_PROGRESS
        var receiveRes = await env.OpsEmployee.Client.PostAsJsonAsync($"/api/v1/requests/{draftReq.RequestId}/receive", new { note = "Đã tiếp nhận" });
        Assert.Equal(HttpStatusCode.OK, receiveRes.StatusCode);

        var startReqRes = await env.OpsEmployee.Client.PostAsync($"/api/v1/requests/{draftReq.RequestId}/start", null);
        Assert.Equal(HttpStatusCode.OK, startReqRes.StatusCode);

        // 4. Create linked parent task for the request & enforce hierarchical manager assignment rule
        var parentTaskRes = await env.Admin.Client.PostAsJsonAsync($"/api/v1/requests/{draftReq.RequestId}/tasks", new
        {
            title = "Điều phối cấp phát thiết bị",
            description = "Nhiệm vụ tổng hợp cấp phòng",
            priority = "HIGH",
            deadline = DateTimeOffset.UtcNow.AddDays(3).ToString("O"),
            checklist = new[] { "Kiểm tra tồn kho", "Bàn giao thiết bị" }
        });
        Assert.Equal(HttpStatusCode.Created, parentTaskRes.StatusCode);
        var parentTask = (await parentTaskRes.Content.ReadFromJsonAsync<TaskCreatedView>())!;

        // Negative: Company Admin cannot assign a company-level main task directly to an individual employee
        var badAssignRes = await env.Admin.Client.PostAsJsonAsync($"/api/v1/tasks/{parentTask.TaskId}/assign", new
        {
            targetType = "USER",
            targetId = env.OpsEmployee.UserId,
            note = "Gán sai cấp"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badAssignRes.StatusCode);
        var badAssignBody = await badAssignRes.Content.ReadAsStringAsync();
        Assert.Contains("TASK.MANAGER_ASSIGNMENT_REQUIRED", badAssignBody);

        // Positive: Assign parent task to accountable Manager -> ASSIGNED -> ACCEPTED -> IN_PROGRESS
        var goodAssignRes = await env.Admin.Client.PostAsJsonAsync($"/api/v1/tasks/{parentTask.TaskId}/assign", new
        {
            targetType = "USER",
            targetId = env.Manager.UserId,
            note = "Giao Trưởng phòng Ops điều phối"
        });
        Assert.Equal(HttpStatusCode.OK, goodAssignRes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Manager.Client.PostAsJsonAsync($"/api/v1/tasks/{parentTask.TaskId}/accept", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Manager.Client.PostAsJsonAsync($"/api/v1/tasks/{parentTask.TaskId}/start", new { })).StatusCode);

        // 5. Manager creates child subtask and assigns to Ops employee -> Rework -> Resubmit -> Confirm
        var subTaskRes = await env.Manager.Client.PostAsJsonAsync("/api/v1/tasks", new
        {
            title = "Cài đặt hệ điều hành và bàn giao",
            priority = "MEDIUM",
            parentTaskId = parentTask.TaskId,
            requestId = draftReq.RequestId
        });
        Assert.Equal(HttpStatusCode.Created, subTaskRes.StatusCode);
        var subTask = (await subTaskRes.Content.ReadFromJsonAsync<TaskCreatedView>())!;

        Assert.Equal(HttpStatusCode.OK, (await env.Manager.Client.PostAsJsonAsync($"/api/v1/tasks/{subTask.TaskId}/assign", new { targetType = "USER", targetId = env.OpsEmployee.UserId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.OpsEmployee.Client.PostAsJsonAsync($"/api/v1/tasks/{subTask.TaskId}/accept", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.OpsEmployee.Client.PostAsJsonAsync($"/api/v1/tasks/{subTask.TaskId}/start", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.OpsEmployee.Client.PostAsJsonAsync($"/api/v1/tasks/{subTask.TaskId}/result", new { content = "Đã cài sơ bộ v1" })).StatusCode);

        // Negative: Parent task CANNOT be confirmed while child subtask is incomplete
        Assert.Equal(HttpStatusCode.OK, (await env.Manager.Client.PostAsJsonAsync($"/api/v1/tasks/{parentTask.TaskId}/result", new { content = "Báo cáo tổng" })).StatusCode);
        var blockedParentConfirm = await env.Manager.Client.PostAsJsonAsync($"/api/v1/tasks/{parentTask.TaskId}/confirm", new { decision = "CONFIRMED", note = "Thử đóng trước" });
        Assert.Equal(HttpStatusCode.Conflict, blockedParentConfirm.StatusCode);
        Assert.Contains("TASK.CHILD_TASKS_INCOMPLETE", await blockedParentConfirm.Content.ReadAsStringAsync());

        // Rework on subtask then resubmit and confirm both subtask and parent task
        Assert.Equal(HttpStatusCode.OK, (await env.Manager.Client.PostAsJsonAsync($"/api/v1/tasks/{subTask.TaskId}/confirm", new { decision = "REWORK", note = "Cần bổ sung biên bản bàn giao" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.OpsEmployee.Client.PostAsJsonAsync($"/api/v1/tasks/{subTask.TaskId}/result", new { content = "Đã bổ sung biên bản v2" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Manager.Client.PostAsJsonAsync($"/api/v1/tasks/{subTask.TaskId}/confirm", new { decision = "CONFIRMED", note = "Đạt yêu cầu" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Manager.Client.PostAsJsonAsync($"/api/v1/tasks/{parentTask.TaskId}/confirm", new { decision = "CONFIRMED", note = "Hoàn tất toàn bộ" })).StatusCode);

        // 6. Ops employee resolves the request (since routed to Ops department with ToUserId == null)
        var resolveRes = await env.OpsEmployee.Client.PostAsJsonAsync($"/api/v1/requests/{draftReq.RequestId}/resolve", new { content = "Đã bàn giao máy trạm hoàn chỉnh" });
        Assert.Equal(HttpStatusCode.OK, resolveRes.StatusCode);

        // Negative: Non-requester Admin/Manager cannot confirm resolution -> 403
        var badConfirmRes = await env.Manager.Client.PostAsJsonAsync($"/api/v1/requests/{draftReq.RequestId}/confirm", new { decision = "CONFIRMED", note = "Self confirm" });
        Assert.Equal(HttpStatusCode.Forbidden, badConfirmRes.StatusCode);

        // 7. Original Requester confirms -> CLOSED & verifies Notification reached actual resolver (OpsEmployee), NOT Requester!
        var confirmReqRes = await env.Requester.Client.PostAsJsonAsync($"/api/v1/requests/{draftReq.RequestId}/confirm", new { decision = "CONFIRMED", note = "Đã nhận máy hoạt động tốt" });
        Assert.Equal(HttpStatusCode.OK, confirmReqRes.StatusCode);
        var confirmedView = (await confirmReqRes.Content.ReadFromJsonAsync<RequestConfirmedView>())!;
        Assert.Equal("CLOSED", confirmedView.Status);

        await using (var resolverDb = database.Create(env.OpsEmployee.UserId, env.OpsEmployee.TenantId))
        {
            var resolverNotifs = await resolverDb.Notifications
                .Where(n => n.ObjectId == draftReq.RequestId && n.Title == "Request resolution confirmed")
                .ToListAsync();
            Assert.Single(resolverNotifs);
        }

        // 8. Archive closed request -> hidden from default search, visible when includeArchived=true
        var archiveRes = await env.Admin.Client.PostAsync($"/api/v1/records/request/{draftReq.RequestId}/archive", null);
        Assert.Equal(HttpStatusCode.OK, archiveRes.StatusCode);

        var activeSearch = await env.Admin.Client.GetFromJsonAsync<JsonElement>($"/api/v1/records/search?q={Uri.EscapeDataString("Cấp máy trạm")}&includeArchived=false");
        Assert.Equal(0, activeSearch.GetProperty("totalCount").GetInt64());

        var archivedSearch = await env.Admin.Client.GetFromJsonAsync<JsonElement>($"/api/v1/records/search?q={Uri.EscapeDataString("Cấp máy trạm")}&includeArchived=true");
        Assert.True(archivedSearch.GetProperty("totalCount").GetInt64() >= 1);
    }

    [Fact]
    public async Task Reject_Revise_Cancel_InactiveUser_And_ConcurrencyConflict_BehaveCorrectly()
    {
        await using var factory = new AuthenticationFactory(database);
        var env = await SeedEnvironmentAsync(factory);

        // 1. Concurrency exception maps cleanly to 409 Conflict CONCURRENCY.CONFLICT (not 500)
        var concRes = await env.Admin.Client.GetAsync("/_audit-probe/concurrency");
        Assert.Equal(HttpStatusCode.Conflict, concRes.StatusCode);
        var concBody = await concRes.Content.ReadAsStringAsync();
        var concJson = JsonDocument.Parse(concBody).RootElement;
        Assert.Equal("CONCURRENCY.CONFLICT", concJson.GetProperty("code").GetString());
        Assert.DoesNotContain("Simulated concurrent modification", concBody);

        // 2. Reject -> Revise workflow
        var createRes = await env.Requester.Client.PostAsJsonAsync("/api/v1/requests", new
        {
            serviceId = env.ServiceId,
            categoryId = env.CategoryId,
            title = "Xin cấp quyền DB Production",
            description = "Chưa ghi rõ lý do",
            priority = "MEDIUM",
            submitImmediately = true
        });
        var req1 = (await createRes.Content.ReadFromJsonAsync<RequestCreatedView>())!;

        var rejectRes = await env.Manager.Client.PostAsJsonAsync($"/api/v1/requests/{req1.RequestId}/reject", new { reason = "Thiếu phê duyệt bảo mật" });
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        var reviseRes = await env.Requester.Client.PostAsJsonAsync($"/api/v1/requests/{req1.RequestId}/revise", new
        {
            title = "Xin cấp quyền DB Production (Đã bổ sung ticket bảo mật #992)",
            description = "Kèm xác nhận trưởng bộ phận",
            priority = "HIGH",
            submitImmediately = true
        });
        Assert.Equal(HttpStatusCode.Created, reviseRes.StatusCode);
        var revised = (await reviseRes.Content.ReadFromJsonAsync<RequestRevisedView>())!;
        Assert.Equal("SUBMITTED", revised.Status);
        Assert.Equal(req1.RequestId, revised.SourceRequestId);

        // Requester can cancel own SUBMITTED request
        var cancelRes = await env.Requester.Client.PostAsync($"/api/v1/requests/{revised.RequestId}/cancel", JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        // 3. Inactive user cannot log in even with valid password
        await using (var db = database.Create(env.Admin.UserId, env.Admin.TenantId))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "User" SET "Status"='INACTIVE'::user_status WHERE "UserId"={env.OutsiderEmployee.UserId};
                """);
        }

        using var freshClient = factory.CreateClient();
        var inactiveLogin = await freshClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            identifier = env.OutsiderEmployee.EmployeeCode,
            password = Password,
            tenantKey = env.OutsiderEmployee.TenantKey
        });
        Assert.True(inactiveLogin.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AiSecurity_ReadPolicy_ActionDispatcher_And_ProactiveAgentDaemon_WorkEndToEnd()
    {
        await using var baseFactory = new AuthenticationFactory(database);
        using var factory = baseFactory.WithWebHostBuilder(b => b.UseSetting("AI:ProactiveDaemonEnabled", "false"));
        var env = await SeedEnvironmentAsync(factory);

        // Create a confidential task belonging to Ops department assigned to OpsEmployee
        var taskRes = await env.Admin.Client.PostAsJsonAsync("/api/v1/tasks", new
        {
            title = "Kiểm toán bảo mật hệ thống Q4",
            description = "Báo cáo nội bộ phòng Ops",
            priority = "CRITICAL",
            deadline = DateTimeOffset.UtcNow.AddHours(6).ToString("O")
        });
        Assert.Equal(HttpStatusCode.Created, taskRes.StatusCode);
        var task = (await taskRes.Content.ReadFromJsonAsync<TaskCreatedView>())!;

        // Manager creates a DRAFT request for routing test
        var reqRes = await env.Requester.Client.PostAsJsonAsync("/api/v1/requests", new
        {
            serviceId = env.ServiceId,
            categoryId = env.CategoryId,
            title = "Hỗ trợ nâng cấp firewall",
            description = "Yêu cầu khẩn",
            priority = "HIGH",
            submitImmediately = true
        });
        var req = (await reqRes.Content.ReadFromJsonAsync<RequestCreatedView>())!;

        // 1. Negative: OutsiderEmployee (not in Ops, not creator, not assigned, no tenant read permission) CANNOT call AI task-risk or task-summary!
        var outsiderRisk = await env.OutsiderEmployee.Client.PostAsJsonAsync("/api/v1/ai/task-risk", new { taskId = task.TaskId });
        Assert.Equal(HttpStatusCode.Forbidden, outsiderRisk.StatusCode);

        var outsiderSummary = await env.OutsiderEmployee.Client.PostAsJsonAsync("/api/v1/ai/task-summary", new { taskId = task.TaskId });
        Assert.Equal(HttpStatusCode.Forbidden, outsiderSummary.StatusCode);

        // Positive: Admin / authorized reader CAN call AI task-risk & task-summary & request-routing
        var adminRisk = await env.Admin.Client.PostAsJsonAsync("/api/v1/ai/task-risk", new { taskId = task.TaskId });
        Assert.Equal(HttpStatusCode.OK, adminRisk.StatusCode);

        // 2. Human-in-the-loop Confirmation Token & State validation via /api/v1/ai/actions/execute
        var assignArgs = JsonSerializer.Serialize(new { taskId = task.TaskId, userId = env.Manager.UserId });
        var proposeAssign = await env.Admin.Client.PostAsJsonAsync("/api/v1/ai/actions/execute", new
        {
            toolName = "assign_task",
            argsJson = assignArgs,
            confirmed = false
        });
        Assert.True(proposeAssign.StatusCode == HttpStatusCode.OK, $"ProposeAssign failed: {proposeAssign.StatusCode} {await proposeAssign.Content.ReadAsStringAsync()} | {baseFactory.LastServerError}");
        var proposed = (await proposeAssign.Content.ReadFromJsonAsync<ExecuteAiActionResult>())!;
        Assert.Equal("NOT_EXECUTED", proposed.ExecutionStatus);
        Assert.False(string.IsNullOrWhiteSpace(proposed.ConfirmationToken));

        // Confirmed execution uses TaskAssignmentCommand -> transitions DRAFT -> ASSIGNED + AuditLog!
        var execAssign = await env.Admin.Client.PostAsJsonAsync("/api/v1/ai/actions/execute", new
        {
            toolName = "assign_task",
            argsJson = assignArgs,
            confirmed = true,
            confirmationToken = proposed.ConfirmationToken
        });
        Assert.Equal(HttpStatusCode.OK, execAssign.StatusCode);

        // Negative: route_request on already-ROUTED request must return 409 Conflict instead of 500
        Assert.Equal(HttpStatusCode.OK, (await env.Manager.Client.PostAsJsonAsync($"/api/v1/requests/{req.RequestId}/route", new
        {
            toDepartmentId = env.OpsDeptId,
            reason = "Manual route",
            source = "MANUAL"
        })).StatusCode);

        var routeArgs = JsonSerializer.Serialize(new { requestId = req.RequestId, toDepartmentId = env.ItDeptId, reason = "AI reroute" });
        var proposeRoute = await env.Admin.Client.PostAsJsonAsync("/api/v1/ai/actions/execute", new
        {
            toolName = "route_request",
            argsJson = routeArgs,
            confirmed = false
        });
        var proposedRoute = (await proposeRoute.Content.ReadFromJsonAsync<ExecuteAiActionResult>())!;
        var invalidRouteRes = await env.Admin.Client.PostAsJsonAsync("/api/v1/ai/actions/execute", new
        {
            toolName = "route_request",
            argsJson = routeArgs,
            confirmed = true,
            confirmationToken = proposedRoute.ConfirmationToken
        });
        Assert.Equal(HttpStatusCode.Conflict, invalidRouteRes.StatusCode);

        // 3. ProactiveAgentDaemon autonomous background scan cycle works end-to-end with system scope!
        using var scope = factory.Services.CreateScope();
        var daemon = new ProactiveAgentDaemon(
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<ProactiveAgentDaemon>.Instance);
        await daemon.RunAutonomousScanCycleAsync(CancellationToken.None);

        await using var checkDb = database.Create(env.Admin.UserId, env.Admin.TenantId);
        var proactiveAlerts = await checkDb.AIRecommendations
            .Where(r => r.ObjectId == task.TaskId && r.RecommendationType == "PROACTIVE_RISK_ALERT")
            .ToListAsync();
        Assert.Single(proactiveAlerts);

        await using var managerNotifDb = database.Create(env.Manager.UserId, env.Manager.TenantId);
        var proactiveNotif = await managerNotifDb.Notifications
            .Where(n => n.ObjectId == task.TaskId && n.Title.Contains("AI Chủ động"))
            .ToListAsync();
        Assert.Single(proactiveNotif);
    }

    private async Task<AuditEnv> SeedEnvironmentAsync(WebApplicationFactory<Program> factory)
    {
        var seed = await database.SeedTenantAsync();
        await using var db = database.Create(seed.UserId, seed.TenantId);
        var adminEntity = await db.Users.SingleAsync();
        var tenant = await db.Tenants.SingleAsync();
        var now = DateTimeOffset.UtcNow;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Company" SET "Status"='ACTIVE' WHERE "CompanyId"={tenant.CompanyId};
            """);

        var itDept = Department.Create(tenant.Id, "IT", "Information Technology", null, now);
        var opsDept = Department.Create(tenant.Id, "OPS", "Operations", null, now);
        db.Departments.AddRange(itDept, opsDept);
        await db.SaveChangesAsync();

        var hasher = new PasswordHasher<UserAccount>();

        UserAccount MakeUser(string code, string email, string name, Guid deptId)
        {
            var u = UserAccount.CreateTenantUser(tenant.Id, code, email, name, "placeholder", now);
            var h = hasher.HashPassword(u, Password);
            typeof(UserAccount).GetProperty(nameof(UserAccount.PasswordHash))!.SetValue(u, h);
            typeof(UserAccount).GetProperty(nameof(UserAccount.DepartmentId))!.SetValue(u, deptId);
            db.Users.Add(u);
            return u;
        }

        // Update seeded admin password so we can log in cleanly as COMPANY_ADMIN
        var adminHash = hasher.HashPassword(adminEntity, Password);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "User" SET "PasswordHash"={adminHash} WHERE "UserId"={adminEntity.Id};
            """);

        var managerEntity = MakeUser("MGR_AUD", "mgr.aud@example.test", "Audit Manager", opsDept.Id);
        var opsEmpEntity = MakeUser("OPS_AUD", "ops.aud@example.test", "Audit Ops Emp", opsDept.Id);
        var reqEntity = MakeUser("REQ_AUD", "req.aud@example.test", "Audit Requester", itDept.Id);
        var outsiderEntity = MakeUser("OUT_AUD", "out.aud@example.test", "Audit Outsider", itDept.Id);
        await db.SaveChangesAsync();

        var adminRole = await db.Roles.SingleAsync(r => r.IsSystem && r.Name == "COMPANY_ADMIN");
        var mgrRole = await db.Roles.SingleAsync(r => r.IsSystem && r.Name == "MANAGER");
        var empRole = await db.Roles.SingleAsync(r => r.IsSystem && r.Name == "EMPLOYEE");

        db.UserRoles.AddRange(
            new UserRole(adminEntity.Id, adminRole.Id),
            new UserRole(adminEntity.Id, mgrRole.Id),
            new UserRole(managerEntity.Id, mgrRole.Id),
            new UserRole(opsEmpEntity.Id, empRole.Id),
            new UserRole(reqEntity.Id, empRole.Id),
            new UserRole(outsiderEntity.Id, empRole.Id));

        db.ManagementScopes.AddRange(
            ManagementScope.Create(tenant.Id, adminEntity.Id, opsDept.Id, true, adminEntity.Id, now),
            ManagementScope.Create(tenant.Id, managerEntity.Id, opsDept.Id, true, adminEntity.Id, now));

        var service = InternalService.Create(tenant.Id, "AUD-IT", "Audit IT Support", "Hardware and Access", true, now);
        var category = ServiceCategory.Create(service.Id, "HW", "Hardware & Setup");
        db.Services.Add(service);
        db.ServiceCategories.Add(category);
        await db.SaveChangesAsync();

        async Task<ActorUser> LoginClient(Guid userId, string code, string email)
        {
            var client = factory.CreateClient();
            var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier = code, password = Password, tenantKey = tenant.TenantKey });
            Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
            var auth = (await loginRes.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            return new ActorUser(userId, tenant.Id, tenant.TenantKey, code, email, client);
        }

        return new AuditEnv(
            Admin: await LoginClient(adminEntity.Id, adminEntity.EmployeeCode, adminEntity.Email),
            Manager: await LoginClient(managerEntity.Id, managerEntity.EmployeeCode, managerEntity.Email),
            OpsEmployee: await LoginClient(opsEmpEntity.Id, opsEmpEntity.EmployeeCode, opsEmpEntity.Email),
            Requester: await LoginClient(reqEntity.Id, reqEntity.EmployeeCode, reqEntity.Email),
            OutsiderEmployee: await LoginClient(outsiderEntity.Id, outsiderEntity.EmployeeCode, outsiderEntity.Email),
            ItDeptId: itDept.Id,
            OpsDeptId: opsDept.Id,
            ServiceId: service.Id,
            CategoryId: category.Id);
    }
}
