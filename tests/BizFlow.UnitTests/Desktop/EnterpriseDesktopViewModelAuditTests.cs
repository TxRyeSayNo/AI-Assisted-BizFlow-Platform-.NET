using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BizFlow.Desktop.Models;
using BizFlow.Desktop.Services;
using BizFlow.Desktop.ViewModels;

namespace BizFlow.UnitTests.Desktop;

public sealed class EnterpriseDesktopViewModelAuditTests
{
    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            Requests.Add((request.Method, request.RequestUri?.PathAndQuery ?? "", body));
            return responder(request);
        }
    }

    private static HttpResponseMessage JsonResponse(object payload, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private static LoginResponseDto CreateSession(Guid userId, string fullName, Guid tenantId, params string[] permissions) =>
        new(
            AccessToken: "test-access-token",
            RefreshToken: "test-refresh-token",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            Session: new SessionIdentityDto(userId, fullName, tenantId, "Acme Corp", permissions));

    [Fact]
    public async Task RequestsViewModel_LifecycleFlags_MatchBackendStateMachine_Strictly()
    {
        var requesterId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var currentStatus = "DRAFT";
        Guid? routedUserId = null;

        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/v1/services"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 100, total = 0 });
            if (path.StartsWith("/api/v1/departments"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 100, total = 0 });
            if (path.StartsWith("/api/v1/users"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 100, total = 0 });
            if (path.StartsWith("/api/v1/comments") || path.StartsWith("/api/v1/attachments"))
                return JsonResponse(Array.Empty<object>());
            if (path == $"/api/v1/requests/{requestId}")
            {
                return JsonResponse(new RequestDetailDto(
                    RequestId: requestId,
                    Title: "VPN Access",
                    Description: "Need VPN",
                    Status: currentStatus,
                    Priority: "HIGH",
                    ServiceId: serviceId,
                    ServiceName: "IT",
                    CategoryId: categoryId,
                    CategoryName: "Network",
                    RequesterId: requesterId,
                    RequesterName: "Requester One",
                    RequesterEmail: "req@acme.local",
                    ParentRequestId: null,
                    RevisedFromRequestId: null,
                    CreatedAt: DateTimeOffset.UtcNow.AddHours(-2),
                    UpdatedAt: DateTimeOffset.UtcNow,
                    ResolvedAt: null,
                    ClosedAt: null,
                    CurrentRoutingDepartmentId: currentStatus == "DRAFT" ? null : Guid.NewGuid(),
                    CurrentRoutingDepartmentName: "IT Dept",
                    CurrentRoutingUserId: routedUserId,
                    CurrentRoutingUserName: null,
                    RoutedAt: null,
                    ReceivedAt: null,
                    RejectionReason: null,
                    Resolutions: [],
                    Confirmations: [],
                    Tasks: []));
            }
            return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 25, total = 0 });
        });

        var session = new SessionManager();
        var supervisor = new BackendHostSupervisor();
        var toast = new ToastNotificationService();
        using var api = new BizFlowApiClient(session, supervisor, new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        var vm = new RequestsViewModel(api, session, toast);

        // Manager/Admin without requester ownership
        session.SetSession(CreateSession(otherManagerId, "Manager", tenantId,
            "requests.read.tenant", "requests.create", "requests.submit", "requests.route",
            "requests.receive", "requests.process", "requests.resolve", "requests.confirm",
            "requests.reject", "requests.cancel", "records.archive", "roles.configure"));

        // SUBMITTED: only Route & Reject & Cancel allowed, NEVER Receive
        currentStatus = "SUBMITTED";
        await vm.OpenDetailAsync(requestId);
        Assert.False(vm.CanSubmitDraft);
        Assert.True(vm.CanRouteCurrent);
        Assert.False(vm.CanReceiveCurrent);
        Assert.True(vm.CanRejectCurrent);
        Assert.False(vm.CanConfirmCurrent);

        // ROUTED: Receive allowed, Route no longer allowed!
        currentStatus = "ROUTED";
        await vm.OpenDetailAsync(requestId);
        Assert.False(vm.CanRouteCurrent);
        Assert.True(vm.CanReceiveCurrent);
        Assert.True(vm.CanRejectCurrent);

        // ROUTED specifically to requesterId: otherManagerId must NOT see CanReceiveCurrent
        routedUserId = requesterId;
        await vm.OpenDetailAsync(requestId);
        Assert.False(vm.CanReceiveCurrent);
        routedUserId = null;

        // IN_PROGRESS: Reject is NOT allowed by RequestLifecyclePolicy
        currentStatus = "IN_PROGRESS";
        await vm.OpenDetailAsync(requestId);
        Assert.False(vm.CanRejectCurrent);
        Assert.True(vm.CanResolveCurrent);

        // RESOLVED: Non-requester admin/manager must NOT see CanConfirmCurrent
        currentStatus = "RESOLVED";
        await vm.OpenDetailAsync(requestId);
        Assert.False(vm.CanConfirmCurrent);

        // CLOSED: CanArchiveCurrent visible
        currentStatus = "CLOSED";
        await vm.OpenDetailAsync(requestId);
        Assert.True(vm.CanArchiveCurrent);

        // Switch session to actual Requester -> sees CanConfirmCurrent on RESOLVED & CanReviseCurrent on REJECTED
        session.SetSession(CreateSession(requesterId, "Requester", tenantId,
            "requests.read.own", "requests.create", "requests.submit", "requests.confirm"));
        currentStatus = "RESOLVED";
        await vm.OpenDetailAsync(requestId);
        Assert.True(vm.CanConfirmCurrent);

        currentStatus = "REJECTED";
        await vm.OpenDetailAsync(requestId);
        Assert.True(vm.CanReviseCurrent);

        // ClearRouteUserCommand clears selected RouteUser
        vm.RouteUser = new UserRowDto(requesterId, "EMP01", "Requester", "req@acme.local", null, "IT", "ACTIVE");
        vm.ClearRouteUserCommand.Execute(null);
        Assert.Null(vm.RouteUser);
    }

    [Fact]
    public async Task ApprovalsViewModel_LoadsRoutedAndOwnResolvedRequests_AndValidatesReworkNote()
    {
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var routedReqId = Guid.NewGuid();
        var ownResolvedReqId = Guid.NewGuid();
        var otherResolvedReqId = Guid.NewGuid();
        var submittedTaskId = Guid.NewGuid();

        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("/api/v1/tasks") && path.Contains("status=SUBMITTED"))
            {
                return JsonResponse(new TaskPageDto(
                [
                    new TaskRowDto(submittedTaskId, "Review Task", "SUBMITTED", "HIGH", null, otherUserId, "Dev", null, "IT", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                ], 1, 50, 1));
            }
            if (path.Contains("/api/v1/requests") && path.Contains("status=ROUTED"))
            {
                return JsonResponse(new RequestPageDto(
                [
                    new RequestRowDto(routedReqId, "Routed Request", "ROUTED", "MEDIUM", Guid.NewGuid(), "IT", Guid.NewGuid(), "HW", otherUserId, "Alice", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                ], 1, 50, 1));
            }
            if (path.Contains("/api/v1/requests") && path.Contains("status=RESOLVED"))
            {
                return JsonResponse(new RequestPageDto(
                [
                    new RequestRowDto(ownResolvedReqId, "Own Resolved", "RESOLVED", "MEDIUM", Guid.NewGuid(), "IT", Guid.NewGuid(), "HW", currentUserId, "Me", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                    new RequestRowDto(otherResolvedReqId, "Other Resolved", "RESOLVED", "MEDIUM", Guid.NewGuid(), "IT", Guid.NewGuid(), "HW", otherUserId, "Alice", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                ], 1, 50, 2));
            }
            if (req.Method == HttpMethod.Post && path.Contains($"/api/v1/requests/{routedReqId}/receive"))
                return JsonResponse(new { requestId = routedReqId, status = "RECEIVED" });

            return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 50, total = 0 });
        });

        var session = new SessionManager();
        session.SetSession(CreateSession(currentUserId, "Manager", tenantId,
            "tasks.confirm", "requests.receive", "requests.confirm", "requests.reject"));
        var supervisor = new BackendHostSupervisor();
        var toast = new ToastNotificationService();
        using var api = new BizFlowApiClient(session, supervisor, new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        var vm = new ApprovalsViewModel(api, session, toast, _ => Task.CompletedTask, _ => Task.CompletedTask);

        await vm.LoadAsync();

        // Should never query SUBMITTED requests, and should exclude otherResolvedReqId!
        Assert.DoesNotContain(handler.Requests, r => r.PathAndQuery.Contains("status=SUBMITTED") && r.PathAndQuery.Contains("/requests"));
        Assert.Equal(3, vm.TotalPendingCount);
        Assert.DoesNotContain(vm.FilteredApprovals, x => x.SourceId == otherResolvedReqId);
        Assert.Contains(vm.FilteredApprovals, x => x.SourceId == ownResolvedReqId);

        // QuickApprove on ROUTED request hits /receive cleanly
        var routedItem = vm.FilteredApprovals.Single(x => x.SourceId == routedReqId);
        await vm.QuickApproveCommand.ExecuteAsync(routedItem);
        Assert.Contains(handler.Requests, r => r.PathAndQuery.Contains($"/api/v1/requests/{routedReqId}/receive"));

        // QuickReject with blank note on ROUTED request warns & refuses to submit invalid empty reason
        var callCountBefore = handler.Requests.Count;
        vm.ApprovalNote = "   ";
        await vm.QuickRejectCommand.ExecuteAsync(routedItem);
        Assert.Equal(callCountBefore, handler.Requests.Count);
        Assert.Equal("WARNING", toast.ToastKind);
    }

    [Fact]
    public async Task WorkspaceDashboardViewModel_EmployeeKpis_CalculateAccurateCompletionRate()
    {
        var empId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/v1/notifications"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 5, total = 4, unreadCount = 2 });
            if (path.StartsWith("/api/v1/tasks") && path.Contains("status=COMPLETED"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 1, total = 3 });
            if (path.StartsWith("/api/v1/tasks") && path.Contains("status=CANCELLED"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 1, total = 1 });
            if (path.StartsWith("/api/v1/tasks"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 1, total = 10 });
            if (path.StartsWith("/api/v1/requests") && path.Contains("status=CLOSED"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 1, total = 2 });
            if (path.StartsWith("/api/v1/requests") && path.Contains("status=CANCELLED"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 1, total = 1 });
            if (path.StartsWith("/api/v1/requests") && path.Contains("status=REJECTED"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 1, total = 0 });
            if (path.StartsWith("/api/v1/requests"))
                return JsonResponse(new { items = Array.Empty<object>(), page = 1, pageSize = 1, total = 5 });

            return JsonResponse(new { });
        });

        var session = new SessionManager();
        session.SetSession(CreateSession(empId, "Emp", tenantId, "tasks.read.assigned", "requests.read.own"));
        var supervisor = new BackendHostSupervisor();
        using var api = new BizFlowApiClient(session, supervisor, new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        var vm = new WorkspaceDashboardViewModel(api, session, _ => { });

        await vm.LoadAsync();

        var myTasksCard = Assert.Single(vm.KpiCards, c => c.Title == "NHIỆM VỤ CỦA TÔI");
        Assert.Equal("6", myTasksCard.Metric); // 10 - 3 - 1 = 6 active

        var myReqsCard = Assert.Single(vm.KpiCards, c => c.Title == "YÊU CẦU CỦA TÔI");
        Assert.Equal("2", myReqsCard.Metric); // 5 - 2 - 1 - 0 = 2 pending

        var rateCard = Assert.Single(vm.KpiCards, c => c.Title == "TIẾN ĐỘ HOÀN THÀNH");
        Assert.Equal("40%", rateCard.Badge); // 4/10 non-active
    }

    [Fact]
    public async Task SessionChanged_ClearsAllViewModelCaches_AndMainWindowCatchesNavigationErrors()
    {
        var user1 = Guid.NewGuid();
        var tenant1 = Guid.NewGuid();

        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"code\":\"BOOM\",\"message\":\"Backend offline\"}", Encoding.UTF8, "application/json")
        });

        var session = new SessionManager();
        var supervisor = new BackendHostSupervisor();
        var toast = new ToastNotificationService();
        using var api = new BizFlowApiClient(session, supervisor, new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        var mainVm = new MainWindowViewModel(session, supervisor, toast, api);

        session.SetSession(CreateSession(user1, "Admin User", tenant1,
            "roles.configure", "tasks.read.tenant", "requests.read.tenant"));

        // Navigating to NewRequest without requests.create permission triggers a clean RBAC warning Toast
        mainVm.NavigateTo("NewRequest");
        await Task.Delay(50);
        Assert.Equal("WARNING", toast.ToastKind);
        Assert.True(toast.IsVisible);

        // Seed state into collections directly to verify SessionChanged/SignOut cleanup
        mainVm.Tasks.Tasks.Add(new TaskRowDto(Guid.NewGuid(), "T1", "DRAFT", "LOW", null, null, null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        mainVm.Requests.Requests.Add(new RequestRowDto(Guid.NewGuid(), "R1", "DRAFT", "LOW", Guid.NewGuid(), "IT", Guid.NewGuid(), "Cat", user1, "Admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        mainVm.Workflows.Workflows.Add(new WorkflowRowDto(Guid.NewGuid(), "WF1", "REQUEST", "ACTIVE", null, 1, "DRAFT"));
        mainVm.Organization.Users.Add(new UserRowDto(user1, "U1", "Admin", "a@b.c", null, null, "ACTIVE", true));
        mainVm.Operations.Notifications.Add(new NotificationRowDto(Guid.NewGuid(), "TASK", null, null, "N", "C", null, DateTimeOffset.UtcNow));

        // Sign out via SignOutCommand
        mainVm.SignOutCommand.Execute(null);

        Assert.False(session.IsSignedIn);
        Assert.Empty(mainVm.Tasks.Tasks);
        Assert.Empty(mainVm.Requests.Requests);
        Assert.Empty(mainVm.Workflows.Workflows);
        Assert.Empty(mainVm.Organization.Users);
        Assert.Empty(mainVm.Operations.Notifications);
        Assert.Empty(mainVm.Dashboard.KpiCards);
        Assert.Equal("Workspace", mainVm.CurrentView);
        Assert.False(mainVm.AiAssistant.IsOpen);

        // UserRowDto AssigneeOptionLabel shows role + department for unambiguous task assignment
        var mgrRow = new UserRowDto(Guid.NewGuid(), "MGR01", "Trần Quản Lý", "mgr@co.vn", Guid.NewGuid(), "Kỹ thuật", "ACTIVE", IsManager: true, RoleName: "MANAGER");
        Assert.Contains("Quản lý", mgrRow.AssigneeOptionLabel);
        Assert.Contains("Kỹ thuật", mgrRow.AssigneeOptionLabel);
    }
}
