using BizFlow.Desktop.Core;
using BizFlow.Desktop.Services;

namespace BizFlow.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly SessionManager _session;
    private readonly BackendHostSupervisor _supervisor;
    private readonly ToastNotificationService _toast;

    // Views: "Workspace", "Tasks", "Requests", "Approvals", "Workflows", "Reports", "SlaMonitor", "RecordSearch", "Organization", "Notifications", "Audit", "PlatformCompanies", "PlatformAudit"
    private string _currentView = "Workspace";

    public MainWindowViewModel(
        SessionManager session,
        BackendHostSupervisor supervisor,
        ToastNotificationService toast,
        BizFlowApiClient api)
    {
        _session = session;
        _supervisor = supervisor;
        _toast = toast;

        Auth = new AuthViewModel(api, session, toast);
        Dashboard = new WorkspaceDashboardViewModel(api, session, NavigateTo);
        Tasks = new TasksViewModel(api, session, toast);
        Requests = new RequestsViewModel(api, session, toast);
        Approvals = new ApprovalsViewModel(api, session, toast, OpenTaskDetailFromExternalAsync, OpenRequestDetailFromExternalAsync);
        Workflows = new WorkflowsViewModel(api, session, toast);
        Organization = new OrganizationViewModel(api, session, toast);
        Reports = new ReportsViewModel(api, session);
        Operations = new OperationsAndPlatformViewModel(api, session, toast, OpenTaskDetailFromExternalAsync, OpenRequestDetailFromExternalAsync);
        AiAssistant = new AiRobotAssistantViewModel(api, session, toast);

        NavigateCommand = new RelayCommand(p => NavigateTo(p?.ToString() ?? "Workspace"));
        ToggleAiAssistantCommand = new RelayCommand(() => AiAssistant.IsOpen = !AiAssistant.IsOpen);
        SignOutCommand = new RelayCommand(ExecuteSignOut);
        DismissToastCommand = new RelayCommand(() => _toast.Dismiss());

        _session.SessionChanged += OnSessionChanged;
    }

    public SessionManager Session => _session;
    public BackendHostSupervisor Supervisor => _supervisor;
    public ToastNotificationService Toast => _toast;

    public AuthViewModel Auth { get; }
    public WorkspaceDashboardViewModel Dashboard { get; }
    public TasksViewModel Tasks { get; }
    public RequestsViewModel Requests { get; }
    public ApprovalsViewModel Approvals { get; }
    public WorkflowsViewModel Workflows { get; }
    public OrganizationViewModel Organization { get; }
    public ReportsViewModel Reports { get; }
    public OperationsAndPlatformViewModel Operations { get; }
    public AiRobotAssistantViewModel AiAssistant { get; }

    public string CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    // RBAC Navigation Sidebar Visibility
    public bool ShowTenantMenu => _session.IsSignedIn && !_session.IsPlatformAdmin;
    public bool ShowPlatformMenu => _session.IsSignedIn && _session.IsPlatformAdmin;

    public bool CanNavTasks => ShowTenantMenu && _session.HasAnyPermission("tasks.read.tenant", "tasks.read.managed", "tasks.read.own", "tasks.read.assigned");
    public bool CanNavRequests => ShowTenantMenu && _session.HasAnyPermission("requests.read.tenant", "requests.read.managed", "requests.read.own");
    public bool CanNavApprovals => ShowTenantMenu && _session.HasAnyPermission("tasks.confirm", "tasks.read.managed", "roles.configure");
    public bool CanNavWorkflows => ShowTenantMenu && _session.HasPermission("workflows.configure");
    public bool CanNavReports => ShowTenantMenu && _session.HasAnyPermission("reports.manager", "reports.company", "reports.workload", "reports.sla");
    public bool CanNavSlaMonitor => ShowTenantMenu && _session.HasAnyPermission("sla.read", "reports.sla", "tasks.read.managed", "roles.configure");
    public bool CanNavSearch => ShowTenantMenu && _session.HasPermission("records.search");
    public bool CanNavOrganization => ShowTenantMenu && _session.HasAnyPermission("roles.configure", "users.read", "departments.read", "service.create", "sla.read");
    public bool CanNavAudit => ShowTenantMenu && _session.HasPermission("audit.read");

    public RelayCommand NavigateCommand { get; }
    public RelayCommand ToggleAiAssistantCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand DismissToastCommand { get; }

    public async Task InitializeAsync()
    {
        await _supervisor.EnsureBackendRunningAsync();
    }

    public void NavigateTo(string viewName)
    {
        if (!_session.IsSignedIn) return;

        // Handle quick action shortcuts from Dashboard Hero Banner
        if (viewName == "NewTask")
        {
            CurrentView = "Tasks";
            Tasks.OpenCreateTask(null, null, null);
            return;
        }
        if (viewName == "NewRequest")
        {
            CurrentView = "Requests";
            _ = RunSafelyAsync(() => Requests.OpenCreateRequestAsync());
            return;
        }

        CurrentView = viewName;

        if (viewName == "Reports") Reports.ActiveReportTab = "Manager";
        else if (viewName == "SlaMonitor") Reports.ActiveReportTab = "Sla";
        else if (viewName == "RecordSearch") Operations.ActiveSection = "RecordSearch";
        else if (viewName == "Notifications") Operations.ActiveSection = "Notifications";
        else if (viewName is "Audit" or "PlatformAudit") Operations.ActiveSection = "Audit";
        else if (viewName == "PlatformCompanies") Operations.ActiveSection = "PlatformCompanies";

        _ = RunSafelyAsync(() => viewName switch
        {
            "Workspace" => Dashboard.LoadAsync(),
            "Tasks" => Tasks.LoadPageAsync(1),
            "Requests" => Requests.LoadPageAsync(1),
            "Approvals" => Approvals.LoadAsync(),
            "Workflows" => Workflows.LoadLibraryAsync(),
            "Reports" => Reports.LoadAllReportsAsync(),
            "SlaMonitor" => Reports.LoadAllReportsAsync(),
            "RecordSearch" => Operations.LoadSearchAsync(),
            "Organization" => Organization.LoadAllAsync(),
            "Notifications" => Operations.LoadNotificationsAsync(),
            "Audit" => Operations.LoadAuditLogsAsync(),
            "PlatformCompanies" => Operations.LoadCompanyRegistrationsAsync(),
            "PlatformAudit" => Operations.LoadAuditLogsAsync(),
            _ => Task.CompletedTask
        });
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tải dữ liệu");
        }
    }

    private async Task OpenTaskDetailFromExternalAsync(Guid taskId)
    {
        CurrentView = "Tasks";
        await Tasks.OpenDetailAsync(taskId);
    }

    private async Task OpenRequestDetailFromExternalAsync(Guid requestId)
    {
        CurrentView = "Requests";
        await Requests.OpenDetailAsync(requestId);
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ShowTenantMenu));
        OnPropertyChanged(nameof(ShowPlatformMenu));
        OnPropertyChanged(nameof(CanNavTasks));
        OnPropertyChanged(nameof(CanNavRequests));
        OnPropertyChanged(nameof(CanNavApprovals));
        OnPropertyChanged(nameof(CanNavWorkflows));
        OnPropertyChanged(nameof(CanNavReports));
        OnPropertyChanged(nameof(CanNavSlaMonitor));
        OnPropertyChanged(nameof(CanNavSearch));
        OnPropertyChanged(nameof(CanNavOrganization));
        OnPropertyChanged(nameof(CanNavAudit));

        if (_session.IsSignedIn)
        {
            AiAssistant.ResetWelcomeMessage();
            if (_session.IsPlatformAdmin)
            {
                NavigateTo("PlatformCompanies");
            }
            else
            {
                NavigateTo("Workspace");
                _ = RunSafelyAsync(() => Operations.LoadNotificationsAsync());
            }
        }
        else
        {
            AiAssistant.IsOpen = false;
            CurrentView = "Workspace";
        }
    }

    private void ExecuteSignOut()
    {
        _session.Clear();
        _toast.Info("Đã đăng xuất khỏi phiên làm việc BizFlow.");
    }
}
