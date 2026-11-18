using System.Globalization;
using System.Windows;
using BizFlow.Desktop.Core;
using BizFlow.Desktop.Models;
using BizFlow.Desktop.Services;
using BizFlow.Desktop.ViewModels;

namespace BizFlow.UnitTests.Desktop;

public sealed class BizFlowDesktopMigrationTests
{
    private static LoginResponseDto CreateLoginResponse(
        string employeeCode,
        string fullName,
        Guid? tenantId,
        string? tenantName,
        string[] permissions)
    {
        return new LoginResponseDto(
            AccessToken: "jwt-access-token",
            RefreshToken: "jwt-refresh-token",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15),
            Session: new SessionIdentityDto(
                UserId: Guid.NewGuid(),
                FullName: fullName,
                TenantId: tenantId,
                TenantName: tenantName,
                Permissions: permissions,
                AvatarUrl: null,
                EmployeeCode: employeeCode,
                Email: $"{employeeCode.ToLowerInvariant()}@bizflow.local"));
    }

    [Fact]
    public void SessionManager_Applies_CompanyAdmin_Roles_And_Granular_Permissions()
    {
        var session = new SessionManager();
        Assert.False(session.IsSignedIn);

        var auth = CreateLoginResponse(
            employeeCode: "ADMIN001",
            fullName: "Nguyen Quan Tri",
            tenantId: Guid.NewGuid(),
            tenantName: "BizFlow Corporation",
            permissions: ["tasks.read.tenant", "workflows.configure", "roles.configure", "audit.read"]);

        session.SetSession(auth);

        Assert.True(session.IsSignedIn);
        Assert.True(session.IsCompanyAdmin);
        Assert.False(session.IsPlatformAdmin);
        Assert.True(session.HasPermission("workflows.configure"));
        Assert.True(session.HasAnyPermission("unknown.perm", "roles.configure"));
        Assert.Equal("Quản trị viên Doanh nghiệp (Company Admin)", session.RoleBadgeLabel);
        Assert.Equal("BizFlow Corporation", session.TenantDisplayLabel);
        Assert.Equal("ADMIN001", session.EmployeeCode);

        session.Clear();
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public void MainWindowViewModel_Enforces_Rbac_Navigation_Across_All_Roles()
    {
        var session = new SessionManager();
        var toast = new ToastNotificationService();
        var supervisor = new BackendHostSupervisor();
        var api = new BizFlowApiClient(session, supervisor);
        var vm = new MainWindowViewModel(session, supervisor, toast, api);

        // 1. Employee Role: can see Tasks & Requests, cannot see Workflows or Organization Admin
        session.SetSession(CreateLoginResponse(
            "EMP001", "Le Nhan Vien", Guid.NewGuid(), "BizFlow Corp",
            ["tasks.read.own", "tasks.read.assigned", "requests.read.own", "records.search"]));

        Assert.True(vm.ShowTenantMenu);
        Assert.False(vm.ShowPlatformMenu);
        Assert.True(vm.CanNavTasks);
        Assert.True(vm.CanNavRequests);
        Assert.True(vm.CanNavSearch);
        Assert.False(vm.CanNavWorkflows);
        Assert.False(vm.CanNavApprovals);
        Assert.False(vm.CanNavOrganization);

        // 2. Company Admin Role: can access Workflow Designer, Approvals, Organization, Reports, Audit
        session.SetSession(CreateLoginResponse(
            "ADMIN001", "Admin", Guid.NewGuid(), "BizFlow Corp",
            ["tasks.read.tenant", "requests.read.tenant", "tasks.confirm", "workflows.configure", "reports.company", "sla.read", "records.search", "roles.configure", "audit.read"]));

        Assert.True(vm.ShowTenantMenu);
        Assert.True(vm.CanNavWorkflows);
        Assert.True(vm.CanNavApprovals);
        Assert.True(vm.CanNavOrganization);
        Assert.True(vm.CanNavReports);
        Assert.True(vm.CanNavAudit);

        // 3. Platform Admin Role: switches to Platform Menu and navigates to PlatformCompanies
        session.SetSession(CreateLoginResponse(
            "SUPERADMIN", "Platform Admin", null, null,
            ["platform.companies.manage", "audit.read"]));

        Assert.False(vm.ShowTenantMenu);
        Assert.True(vm.ShowPlatformMenu);
        Assert.Equal("PlatformCompanies", vm.CurrentView);
        Assert.Equal("PlatformCompanies", vm.Operations.ActiveSection);
    }

    [Fact]
    public void AuthViewModel_ScreenSwitching_And_SuperAdminDetection_Work_Correctly()
    {
        var session = new SessionManager();
        var toast = new ToastNotificationService();
        var supervisor = new BackendHostSupervisor();
        var api = new BizFlowApiClient(session, supervisor);
        var authVm = new AuthViewModel(api, session, toast);

        Assert.Equal("Login", authVm.ActiveScreen);
        Assert.Equal("ADMIN001", authVm.Identifier);
        Assert.False(authVm.IsSuperAdminIdentifier);

        authVm.Identifier = "SUPERADMIN";
        Assert.True(authVm.IsSuperAdminIdentifier);

        authVm.SwitchScreenCommand.Execute("RegisterCompany");
        Assert.Equal("RegisterCompany", authVm.ActiveScreen);

        authVm.SwitchScreenCommand.Execute("EmailLink");
        Assert.Equal("EmailLink", authVm.ActiveScreen);
    }

    [Fact]
    public void WorkflowsViewModel_Designer_Supports_Adding_Connecting_And_Layout_Of_Nodes()
    {
        var session = new SessionManager();
        var toast = new ToastNotificationService();
        var supervisor = new BackendHostSupervisor();
        var api = new BizFlowApiClient(session, supervisor);
        var wfVm = new WorkflowsViewModel(api, session, toast);

        wfVm.AddNodeCommand.Execute("START");
        wfVm.AddNodeCommand.Execute("APPROVAL");
        wfVm.AddNodeCommand.Execute("END");

        Assert.Equal(3, wfVm.DesignerNodes.Count);

        wfVm.ConnectFromNode = wfVm.DesignerNodes[0];
        wfVm.ConnectToNode = wfVm.DesignerNodes[1];
        wfVm.NewEdgeLabel = "Trình duyệt";
        wfVm.ConnectNodesCommand.Execute(null);

        Assert.Single(wfVm.DesignerEdges);
        Assert.Equal(wfVm.DesignerNodes[0].Id, wfVm.DesignerEdges[0].Source);
        Assert.Equal(wfVm.DesignerNodes[1].Id, wfVm.DesignerEdges[0].Target);

        wfVm.AutoLayoutCommand.Execute(null);
        Assert.Equal(40, wfVm.DesignerNodes[0].X);

        wfVm.SelectedNode = wfVm.DesignerNodes[1];
        wfVm.DeleteSelectedNodeCommand.Execute(null);
        Assert.Equal(2, wfVm.DesignerNodes.Count);
        Assert.Empty(wfVm.DesignerEdges);
    }

    [Fact]
    public void WpfConverters_Transform_Visibility_And_Status_Brushes_Accurately()
    {
        var boolToVis = new BoolToVisibilityConverter();
        var invBoolToVis = new InverseBoolToVisibilityConverter();
        var equalToVis = new EqualToVisibilityConverter();
        var statusBg = new StatusToBackgroundBrushConverter();
        var priorityBrush = new PriorityToBrushConverter();

        Assert.Equal(Visibility.Visible, boolToVis.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, boolToVis.Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, invBoolToVis.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, equalToVis.Convert("Tasks", typeof(Visibility), "Tasks", CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, equalToVis.Convert("Tasks", typeof(Visibility), "Requests", CultureInfo.InvariantCulture));

        Assert.NotNull(statusBg.Convert("IN_PROGRESS", typeof(object), null!, CultureInfo.InvariantCulture));
        Assert.NotNull(priorityBrush.Convert("CRITICAL", typeof(object), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void All_Wpf_Views_And_MainWindow_Instantiate_Without_XamlParseException()
    {
        Exception? capturedException = null;

        var staThread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    var app = new System.Windows.Application
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/BizFlow.Desktop;component/Resources/BrandTheme.xaml", UriKind.Absolute)
                    });
                }

                var session = new SessionManager();
                var toast = new ToastNotificationService();
                var supervisor = new BackendHostSupervisor();
                var api = new BizFlowApiClient(session, supervisor);
                var mainVm = new MainWindowViewModel(session, supervisor, toast, api);

                var authView = new BizFlow.Desktop.Views.AuthView { DataContext = mainVm.Auth };
                var dashboardView = new BizFlow.Desktop.Views.DashboardView { DataContext = mainVm.Dashboard };
                var tasksView = new BizFlow.Desktop.Views.TasksView { DataContext = mainVm.Tasks };
                var requestsView = new BizFlow.Desktop.Views.RequestsView { DataContext = mainVm.Requests };
                var approvalsView = new BizFlow.Desktop.Views.ApprovalsView { DataContext = mainVm.Approvals };
                var workflowsView = new BizFlow.Desktop.Views.WorkflowsView { DataContext = mainVm.Workflows };
                var orgView = new BizFlow.Desktop.Views.OrganizationView { DataContext = mainVm.Organization };
                var reportsView = new BizFlow.Desktop.Views.ReportsView { DataContext = mainVm.Reports };
                var operationsView = new BizFlow.Desktop.Views.OperationsView { DataContext = mainVm.Operations };
                var mainWindow = new BizFlow.Desktop.MainWindow(mainVm);

                Assert.NotNull(authView);
                Assert.NotNull(dashboardView);
                Assert.NotNull(tasksView);
                Assert.NotNull(requestsView);
                Assert.NotNull(approvalsView);
                Assert.NotNull(workflowsView);
                Assert.NotNull(orgView);
                Assert.NotNull(reportsView);
                Assert.NotNull(operationsView);
                Assert.NotNull(mainWindow);
                mainWindow.Close();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        Assert.Null(capturedException);
    }

    [Fact]
    public async Task EndToEnd_WpfViewModels_Communicate_With_Live_Backend_And_PostgreSql_Across_All_Modules()
    {
        var session = new SessionManager();
        var toast = new ToastNotificationService();
        using var supervisor = new BackendHostSupervisor();
        var api = new BizFlowApiClient(session, supervisor);
        var mainVm = new MainWindowViewModel(session, supervisor, toast, api);

        var backendReady = await supervisor.EnsureBackendRunningAsync();
        Assert.True(backendReady, $"Backend failed to become ready: {supervisor.StatusMessage}");
        Assert.True(supervisor.IsReady);

        // 1. Login as COMPANY_ADMIN (ADMIN001)
        mainVm.Auth.TenantKey = "bizflow-corp";
        mainVm.Auth.Identifier = "ADMIN001";
        mainVm.Auth.Password = "Password123!";
        await mainVm.Auth.LoginCommand.ExecuteAsync(null);

        Assert.True(session.IsSignedIn, $"Company Admin login failed: {mainVm.Auth.ErrorMessage}");
        Assert.True(session.IsCompanyAdmin);

        // 2. Workspace Dashboard
        await mainVm.Dashboard.LoadAsync();
        Assert.NotEmpty(mainVm.Dashboard.KpiCards);
        Assert.NotEmpty(mainVm.Dashboard.ModuleCards);

        // 3. Task Management & Task Detail
        await mainVm.Tasks.LoadPageAsync(1);
        Assert.NotEmpty(mainVm.Tasks.Tasks);
        await mainVm.Tasks.OpenDetailAsync(mainVm.Tasks.Tasks[0].TaskId);
        Assert.NotNull(mainVm.Tasks.TaskDetail);

        // 4. Request Management & Request Detail
        await mainVm.Requests.LoadPageAsync(1);
        Assert.NotEmpty(mainVm.Requests.Requests);
        await mainVm.Requests.OpenDetailAsync(mainVm.Requests.Requests[0].RequestId);
        Assert.NotNull(mainVm.Requests.RequestDetail);

        // 5. Approvals Center
        await mainVm.Approvals.LoadAsync();

        // 6. Workflow Management & Designer
        await mainVm.Workflows.LoadLibraryAsync();
        Assert.NotEmpty(mainVm.Workflows.Workflows);
        await mainVm.Workflows.OpenDesignerCommand.ExecuteAsync(mainVm.Workflows.Workflows[0]);
        Assert.NotNull(mainVm.Workflows.CurrentVersion);
        Assert.NotEmpty(mainVm.Workflows.DesignerNodes);

        // 7. Organization, Users, Departments, Roles, Services, SLA Profiles
        await mainVm.Organization.LoadAllAsync();
        Assert.NotEmpty(mainVm.Organization.Users);
        Assert.NotEmpty(mainVm.Organization.Departments);
        Assert.NotEmpty(mainVm.Organization.Roles);
        Assert.NotEmpty(mainVm.Organization.PermissionItems);

        // 8. Reports & SLA Analytics
        await mainVm.Reports.LoadAllReportsAsync();
        Assert.NotNull(mainVm.Reports.CompanyReport);
        Assert.NotNull(mainVm.Reports.WorkloadReport);
        Assert.NotNull(mainVm.Reports.SlaReport);

        // 9. Unified Record Search, Notifications & Audit Logs
        await mainVm.Operations.LoadSearchAsync();
        Assert.NotEmpty(mainVm.Operations.SearchResults);
        await mainVm.Operations.LoadNotificationsAsync();
        await mainVm.Operations.LoadAuditLogsAsync();
        Assert.NotEmpty(mainVm.Operations.AuditLogs);

        // 10. AI Robot Assistant
        mainVm.AiAssistant.ChatInput = "/notifications";
        await mainVm.AiAssistant.SendChatCommand.ExecuteAsync(null);
        Assert.True(mainVm.AiAssistant.Messages.Count >= 2);

        // 11. Login as MANAGER (MGR001)
        mainVm.SignOutCommand.Execute(null);
        mainVm.Auth.TenantKey = "bizflow-corp";
        mainVm.Auth.Identifier = "MGR001";
        mainVm.Auth.Password = "Password123!";
        await mainVm.Auth.LoginCommand.ExecuteAsync(null);
        Assert.True(session.IsSignedIn);
        Assert.True(session.IsManager);
        await mainVm.Tasks.LoadPageAsync(1);
        Assert.NotEmpty(mainVm.Tasks.Tasks);

        // 12. Login as EMPLOYEE (EMP001)
        mainVm.SignOutCommand.Execute(null);
        mainVm.Auth.TenantKey = "bizflow-corp";
        mainVm.Auth.Identifier = "EMP001";
        mainVm.Auth.Password = "Password123!";
        await mainVm.Auth.LoginCommand.ExecuteAsync(null);
        Assert.True(session.IsSignedIn);
        Assert.True(session.IsEmployee);
        Assert.False(mainVm.CanNavWorkflows);
        await mainVm.Tasks.LoadPageAsync(1);
        Assert.NotEmpty(mainVm.Tasks.Tasks);

        // 13. Login as PLATFORM_ADMIN (SUPERADMIN)
        mainVm.SignOutCommand.Execute(null);
        mainVm.Auth.Identifier = "SUPERADMIN";
        mainVm.Auth.Password = "Password123!";
        await mainVm.Auth.LoginCommand.ExecuteAsync(null);
        Assert.True(session.IsSignedIn);
        Assert.True(session.IsPlatformAdmin);
        Assert.Equal("PlatformCompanies", mainVm.CurrentView);
        await mainVm.Operations.LoadCompanyRegistrationsAsync();
        Assert.NotEmpty(mainVm.Operations.CompanyRegistrations);
        await mainVm.Operations.LoadAuditLogsAsync();
        Assert.NotEmpty(mainVm.Operations.AuditLogs);
    }
}
