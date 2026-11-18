using System.Collections.ObjectModel;
using BizFlow.Desktop.Core;
using BizFlow.Desktop.Models;
using BizFlow.Desktop.Services;

namespace BizFlow.Desktop.ViewModels;

// ============================================================================
// 1. ORGANIZATION, USERS, ROLES, SERVICES & SLA PROFILES VIEWMODEL
// ============================================================================
public sealed class OrganizationViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;
    private readonly ToastNotificationService _toast;

    // Tabs: "Users", "Departments", "Roles", "Services", "SlaProfiles"
    private string _activeTab = "Users";
    private bool _isBusy;
    private int _configuredModulesCount;

    // Users Tab State
    private string _userSearch = "";
    private string _userStatusFilter = "";
    private DepartmentRowDto? _userDepartmentFilter;
    private string _newUserCode = "";
    private string _newUserFullName = "";
    private string _newUserEmail = "";
    private string _newUserPassword = "Password123!";
    private DepartmentRowDto? _newUserDepartment;
    private string _newUserRole = "EMPLOYEE";
    private UserRowDto? _selectedUser;
    private UserResponsibilitiesDto? _demoteResponsibilities;
    private ReplacementManagerDto? _selectedReplacementManager;
    private string _batchCsvText = "EMP020, Nguyễn Văn Minh, minh.nv@bizflow.local, TECH, EMPLOYEE\nEMP021, Lê Thị Hương, huong.lt@bizflow.local, TECH, EMPLOYEE";

    // Departments Tab State
    private string _deptSearch = "";
    private string _newDeptCode = "";
    private string _newDeptName = "";

    // Roles Tab State
    private string _newRoleName = "";
    private RoleRowDto? _selectedRole;

    // Services Tab State
    private string _newServiceCode = "";
    private string _newServiceName = "";
    private string _newServiceDescription = "";
    private string _newCategoryCode = "";
    private string _newCategoryName = "";
    private ServiceRowDto? _selectedService;

    // SLA Profiles Tab State
    private string _newSlaProfileName = "";
    private SlaProfileRowDto? _selectedSlaProfile;

    public OrganizationViewModel(BizFlowApiClient api, SessionManager session, ToastNotificationService toast)
    {
        _api = api;
        _session = session;
        _toast = toast;

        Users = [];
        Departments = [];
        Roles = [];
        PermissionItems = [];
        Services = [];
        SlaProfiles = [];
        SlaVersions = [];
        BatchValidationItems = [];

        UserStatusOptions = ["", "ACTIVE", "INACTIVE", "LOCKED"];
        RoleCodeOptions = ["EMPLOYEE", "MANAGER", "COMPANY_ADMIN"];

        LoadAllCommand = new AsyncRelayCommand(LoadAllAsync);
        SwitchTabCommand = new RelayCommand(p => ActiveTab = p?.ToString() ?? "Users");

        // User Commands
        FilterUsersCommand = new AsyncRelayCommand(LoadUsersAsync);
        CreateUserCommand = new AsyncRelayCommand(ExecuteCreateUserAsync, () => !IsBusy);
        ToggleUserStatusCommand = new AsyncRelayCommand(p => p is UserRowDto u ? ExecuteToggleUserStatusAsync(u) : Task.CompletedTask);
        PromoteToManagerCommand = new AsyncRelayCommand(p => p is UserRowDto u ? ExecutePromoteManagerAsync(u) : Task.CompletedTask);
        InspectDemoteCommand = new AsyncRelayCommand(p => p is UserRowDto u ? ExecuteInspectDemoteAsync(u) : Task.CompletedTask);
        ConfirmDemoteCommand = new AsyncRelayCommand(ExecuteConfirmDemoteAsync, () => !IsBusy);
        TransferAdminCommand = new AsyncRelayCommand(p => p is UserRowDto u ? ExecuteTransferAdminAsync(u) : Task.CompletedTask);
        DryRunBatchImportCommand = new AsyncRelayCommand(() => ExecuteBatchImportAsync(dryRun: true), () => !IsBusy);
        CommitBatchImportCommand = new AsyncRelayCommand(() => ExecuteBatchImportAsync(dryRun: false), () => !IsBusy);

        // Department Commands
        FilterDepartmentsCommand = new AsyncRelayCommand(LoadDepartmentsAsync);
        CreateDepartmentCommand = new AsyncRelayCommand(ExecuteCreateDepartmentAsync, () => !IsBusy);

        // Role Commands
        CreateRoleCommand = new AsyncRelayCommand(ExecuteCreateRoleAsync, () => !IsBusy);
        SaveRolePermissionsCommand = new AsyncRelayCommand(ExecuteSaveRolePermissionsAsync, () => !IsBusy);

        // Service Commands
        CreateServiceCommand = new AsyncRelayCommand(ExecuteCreateServiceAsync, () => !IsBusy);
        AddServiceCategoryCommand = new AsyncRelayCommand(ExecuteAddServiceCategoryAsync, () => !IsBusy);

        // SLA Profile Commands
        CreateSlaProfileCommand = new AsyncRelayCommand(ExecuteCreateSlaProfileAsync, () => !IsBusy);

        _session.SessionChanged += (_, _) => ResetForSession();
    }

    private void ResetForSession()
    {
        UserSearch = "";
        DeptSearch = "";
        SelectedUser = null;
        SelectedRole = null;
        SelectedService = null;
        SelectedSlaProfile = null;
        Users.Clear();
        Departments.Clear();
        Roles.Clear();
        PermissionItems.Clear();
        Services.Clear();
        SlaProfiles.Clear();
        SlaVersions.Clear();
        BatchValidationItems.Clear();
        ConfiguredModulesCount = 0;
        OnPropertyChanged(nameof(CanManageOrganization));
    }

    public SessionManager Session => _session;
    public ObservableCollection<UserRowDto> Users { get; }
    public ObservableCollection<DepartmentRowDto> Departments { get; }
    public ObservableCollection<RoleRowDto> Roles { get; }
    public ObservableCollection<SelectablePermissionItem> PermissionItems { get; }
    public ObservableCollection<ServiceRowDto> Services { get; }
    public ObservableCollection<SlaProfileRowDto> SlaProfiles { get; }
    public ObservableCollection<SlaVersionRowDto> SlaVersions { get; }
    public ObservableCollection<BatchImportValidationItemDto> BatchValidationItems { get; }

    public IReadOnlyList<string> UserStatusOptions { get; }
    public IReadOnlyList<string> RoleCodeOptions { get; }

    public string ActiveTab { get => _activeTab; set => SetProperty(ref _activeTab, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public int ConfiguredModulesCount { get => _configuredModulesCount; private set => SetProperty(ref _configuredModulesCount, value); }
    public int ReadinessPercent => ConfiguredModulesCount * 20;
    public string ReadinessSummary => $"{ConfiguredModulesCount}/5 mô-đun vận hành cốt lõi đã được cấu hình ({ReadinessPercent}% Sẵn sàng)";

    public bool CanManageOrganization => _session.HasAnyPermission("roles.configure", "users.create", "users.update");

    // User bindings
    public string UserSearch { get => _userSearch; set => SetProperty(ref _userSearch, value); }
    public string UserStatusFilter { get => _userStatusFilter; set => SetProperty(ref _userStatusFilter, value); }
    public DepartmentRowDto? UserDepartmentFilter { get => _userDepartmentFilter; set => SetProperty(ref _userDepartmentFilter, value); }
    public string NewUserCode { get => _newUserCode; set => SetProperty(ref _newUserCode, value); }
    public string NewUserFullName { get => _newUserFullName; set => SetProperty(ref _newUserFullName, value); }
    public string NewUserEmail { get => _newUserEmail; set => SetProperty(ref _newUserEmail, value); }
    public string NewUserPassword { get => _newUserPassword; set => SetProperty(ref _newUserPassword, value); }
    public DepartmentRowDto? NewUserDepartment { get => _newUserDepartment; set => SetProperty(ref _newUserDepartment, value); }
    public string NewUserRole { get => _newUserRole; set => SetProperty(ref _newUserRole, value); }
    public UserRowDto? SelectedUser { get => _selectedUser; set => SetProperty(ref _selectedUser, value); }
    public UserResponsibilitiesDto? DemoteResponsibilities { get => _demoteResponsibilities; private set => SetProperty(ref _demoteResponsibilities, value); }
    public ReplacementManagerDto? SelectedReplacementManager { get => _selectedReplacementManager; set => SetProperty(ref _selectedReplacementManager, value); }
    public string BatchCsvText { get => _batchCsvText; set => SetProperty(ref _batchCsvText, value); }

    // Department bindings
    public string DeptSearch { get => _deptSearch; set => SetProperty(ref _deptSearch, value); }
    public string NewDeptCode { get => _newDeptCode; set => SetProperty(ref _newDeptCode, value); }
    public string NewDeptName { get => _newDeptName; set => SetProperty(ref _newDeptName, value); }

    // Role bindings
    public string NewRoleName { get => _newRoleName; set => SetProperty(ref _newRoleName, value); }
    public RoleRowDto? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (SetProperty(ref _selectedRole, value))
                SyncRolePermissionSelection();
        }
    }

    // Service bindings
    public string NewServiceCode { get => _newServiceCode; set => SetProperty(ref _newServiceCode, value); }
    public string NewServiceName { get => _newServiceName; set => SetProperty(ref _newServiceName, value); }
    public string NewServiceDescription { get => _newServiceDescription; set => SetProperty(ref _newServiceDescription, value); }
    public string NewCategoryCode { get => _newCategoryCode; set => SetProperty(ref _newCategoryCode, value); }
    public string NewCategoryName { get => _newCategoryName; set => SetProperty(ref _newCategoryName, value); }
    public ServiceRowDto? SelectedService { get => _selectedService; set => SetProperty(ref _selectedService, value); }

    // SLA Profile bindings
    public string NewSlaProfileName { get => _newSlaProfileName; set => SetProperty(ref _newSlaProfileName, value); }
    public SlaProfileRowDto? SelectedSlaProfile
    {
        get => _selectedSlaProfile;
        set
        {
            if (SetProperty(ref _selectedSlaProfile, value) && value is not null)
                _ = LoadSlaVersionsAsync(value.SlaProfileId);
        }
    }

    // Commands
    public AsyncRelayCommand LoadAllCommand { get; }
    public RelayCommand SwitchTabCommand { get; }
    public AsyncRelayCommand FilterUsersCommand { get; }
    public AsyncRelayCommand CreateUserCommand { get; }
    public AsyncRelayCommand ToggleUserStatusCommand { get; }
    public AsyncRelayCommand PromoteToManagerCommand { get; }
    public AsyncRelayCommand InspectDemoteCommand { get; }
    public AsyncRelayCommand ConfirmDemoteCommand { get; }
    public AsyncRelayCommand TransferAdminCommand { get; }
    public AsyncRelayCommand DryRunBatchImportCommand { get; }
    public AsyncRelayCommand CommitBatchImportCommand { get; }
    public AsyncRelayCommand FilterDepartmentsCommand { get; }
    public AsyncRelayCommand CreateDepartmentCommand { get; }
    public AsyncRelayCommand CreateRoleCommand { get; }
    public AsyncRelayCommand SaveRolePermissionsCommand { get; }
    public AsyncRelayCommand CreateServiceCommand { get; }
    public AsyncRelayCommand AddServiceCategoryCommand { get; }
    public AsyncRelayCommand CreateSlaProfileCommand { get; }

    public async Task LoadAllAsync()
    {
        if (!_session.IsSignedIn) return;
        OnPropertyChanged(nameof(CanManageOrganization));

        await LoadDepartmentsAsync();
        await LoadUsersAsync();
        await LoadRolesAsync();
        await LoadServicesAsync();
        await LoadSlaProfilesAsync();

        var count = 0;
        if (Departments.Count > 0) count++;
        if (Users.Count > 0) count++;
        if (Roles.Count > 0) count++;
        if (Services.Count > 0) count++;
        if (SlaProfiles.Count > 0) count++;
        ConfiguredModulesCount = count;
        OnPropertyChanged(nameof(ReadinessPercent));
        OnPropertyChanged(nameof(ReadinessSummary));
    }

    public async Task LoadDepartmentsAsync()
    {
        try
        {
            var page = await _api.GetDepartmentsAsync(1, 100, null, DeptSearch);
            Departments.Clear();
            foreach (var d in page.Items) Departments.Add(d);
            NewUserDepartment ??= Departments.FirstOrDefault();
        }
        catch { }
    }

    public async Task LoadUsersAsync()
    {
        try
        {
            var page = await _api.GetUsersAsync(1, 100, UserSearch, UserStatusFilter, UserDepartmentFilter?.DepartmentId);
            Users.Clear();
            foreach (var u in page.Items) Users.Add(u);
        }
        catch { }
    }

    public async Task LoadRolesAsync()
    {
        try
        {
            var page = await _api.GetRolesAsync(1, 50);
            Roles.Clear();
            foreach (var r in page.Items) Roles.Add(r);

            PermissionItems.Clear();
            foreach (var p in page.AvailablePermissions)
            {
                PermissionItems.Add(new SelectablePermissionItem { Permission = p, IsChecked = false });
            }

            SelectedRole = Roles.FirstOrDefault(r => !r.IsSystem) ?? Roles.FirstOrDefault();
        }
        catch { }
    }

    public async Task LoadServicesAsync()
    {
        try
        {
            var page = await _api.GetServicesAsync(1, 100);
            Services.Clear();
            foreach (var s in page.Items) Services.Add(s);
            SelectedService ??= Services.FirstOrDefault();
        }
        catch { }
    }

    public async Task LoadSlaProfilesAsync()
    {
        try
        {
            var page = await _api.GetSlaProfilesAsync(1, 50);
            SlaProfiles.Clear();
            foreach (var p in page.Items) SlaProfiles.Add(p);
            SelectedSlaProfile ??= SlaProfiles.FirstOrDefault();
        }
        catch { }
    }

    private async Task LoadSlaVersionsAsync(Guid profileId)
    {
        try
        {
            var history = await _api.GetSlaVersionHistoryAsync(profileId, 1, 25);
            SlaVersions.Clear();
            foreach (var v in history.Items) SlaVersions.Add(v);
        }
        catch { }
    }

    private void SyncRolePermissionSelection()
    {
        var ids = SelectedRole?.PermissionIds ?? [];
        foreach (var item in PermissionItems)
        {
            item.IsChecked = ids.Contains(item.Permission.PermissionId);
        }
    }

    private async Task ExecuteCreateUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUserCode) || string.IsNullOrWhiteSpace(NewUserFullName) || string.IsNullOrWhiteSpace(NewUserEmail))
        {
            _toast.Warning("Vui lòng nhập đầy đủ Mã nhân viên, Họ và tên, và Email.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.CreateUserAsync(NewUserCode, NewUserFullName, NewUserEmail, NewUserPassword, NewUserDepartment?.DepartmentId, NewUserRole);
            _toast.Success($"Đã tạo tài khoản nhân sự '{NewUserFullName}' ({NewUserCode}) thành công!");
            NewUserCode = "";
            NewUserFullName = "";
            NewUserEmail = "";
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tạo tài khoản nhân sự");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteToggleUserStatusAsync(UserRowDto user)
    {
        var nextStatus = user.Status == "ACTIVE" ? "INACTIVE" : "ACTIVE";
        IsBusy = true;
        try
        {
            await _api.UpdateUserStatusAsync(user.UserId, nextStatus);
            _toast.Success($"Đã cập nhật trạng thái của '{user.FullName}' sang {nextStatus}.");
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecutePromoteManagerAsync(UserRowDto user)
    {
        IsBusy = true;
        try
        {
            await _api.PromoteUserToManagerAsync(user.UserId);
            _toast.Success($"Đã thăng cấp '{user.FullName}' lên vai trò Quản lý / Trưởng phòng!");
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi thăng cấp Quản lý");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteInspectDemoteAsync(UserRowDto user)
    {
        SelectedUser = user;
        IsBusy = true;
        try
        {
            var resp = await _api.GetUserResponsibilitiesAsync(user.UserId);
            DemoteResponsibilities = resp;
            SelectedReplacementManager = resp.EligibleReplacementManagers.FirstOrDefault();
            _toast.Info($"Đã kiểm tra trách nhiệm của {user.FullName}: {resp.ActiveMainTasksCount} việc chính, {resp.PendingApprovalsCount} phê duyệt chờ xử lý.");
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteConfirmDemoteAsync()
    {
        if (SelectedUser is null) return;
        IsBusy = true;
        try
        {
            await _api.DemoteUserToEmployeeAsync(SelectedUser.UserId, SelectedReplacementManager?.UserId);
            _toast.Success($"Đã hạ cấp '{SelectedUser.FullName}' xuống vai trò Nhân viên!");
            DemoteResponsibilities = null;
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi hạ cấp Quản lý");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteTransferAdminAsync(UserRowDto user)
    {
        IsBusy = true;
        try
        {
            await _api.TransferAdminOwnershipAsync(user.UserId);
            _toast.Success($"Đã bàn giao quyền Quản trị viên Doanh nghiệp cho '{user.FullName}'!");
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi bàn giao quyền Admin");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteBatchImportAsync(bool dryRun)
    {
        var lines = (BatchCsvText ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsedRows = new List<object>();

        foreach (var line in lines)
        {
            if (line.StartsWith("Mã NV", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 3)
            {
                parsedRows.Add(new
                {
                    employeeCode = parts[0],
                    fullName = parts[1],
                    email = parts[2],
                    departmentCode = parts.Length > 3 ? parts[3] : "TECH",
                    role = parts.Length > 4 ? parts[4] : "EMPLOYEE"
                });
            }
        }

        if (parsedRows.Count == 0)
        {
            _toast.Warning("Vui lòng nhập ít nhất 1 dòng dữ liệu CSV: Mã NV, Họ Tên, Email, Mã Phòng Ban, Vai Trò.");
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _api.BatchImportUsersAsync(parsedRows, dryRun);
            BatchValidationItems.Clear();
            var items = res.ValidationItems ?? res.Items ?? [];
            foreach (var it in items) BatchValidationItems.Add(it);

            if (dryRun)
            {
                _toast.Info($"Kiểm tra Dry-Run hoàn tất: {res.ValidCount}/{res.TotalProcessed} dòng hợp lệ.");
            }
            else
            {
                _toast.Success($"Đã nhập thành công {res.ImportedCount} nhân sự mới vào hệ thống!");
                await LoadUsersAsync();
            }
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi nhập nhân sự hàng loạt");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteCreateDepartmentAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDeptCode) || string.IsNullOrWhiteSpace(NewDeptName))
        {
            _toast.Warning("Vui lòng nhập Mã phòng ban và Tên phòng ban.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.CreateDepartmentAsync(NewDeptCode, NewDeptName);
            _toast.Success($"Đã tạo phòng ban '{NewDeptName}' ({NewDeptCode})!");
            NewDeptCode = "";
            NewDeptName = "";
            await LoadDepartmentsAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tạo phòng ban");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteCreateRoleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRoleName))
        {
            _toast.Warning("Vui lòng nhập tên vai trò tùy chỉnh.");
            return;
        }

        IsBusy = true;
        try
        {
            var created = await _api.CreateRoleAsync(NewRoleName);
            NewRoleName = "";
            _toast.Success($"Đã tạo vai trò tùy chỉnh '{created.Name}'!");
            await LoadRolesAsync();
            SelectedRole = Roles.FirstOrDefault(r => r.RoleId == created.RoleId);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tạo vai trò");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteSaveRolePermissionsAsync()
    {
        if (SelectedRole is null || SelectedRole.IsSystem)
        {
            _toast.Warning("Vai trò hệ thống được bảo vệ. Hãy chọn hoặc tạo một vai trò tùy chỉnh để cấu hình quyền.");
            return;
        }

        var checkedIds = PermissionItems.Where(p => p.IsChecked).Select(p => p.Permission.PermissionId).ToArray();
        IsBusy = true;
        try
        {
            var updated = await _api.SetRolePermissionsAsync(SelectedRole.RoleId, SelectedRole.ETag, checkedIds);
            _toast.Success($"Đã lưu {updated.PermissionIds.Count} quyền hạn cho vai trò '{updated.Name}'!");
            await LoadRolesAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi lưu cấu hình quyền");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteCreateServiceAsync()
    {
        if (string.IsNullOrWhiteSpace(NewServiceCode) || string.IsNullOrWhiteSpace(NewServiceName))
        {
            _toast.Warning("Vui lòng nhập Mã dịch vụ và Tên dịch vụ nội bộ.");
            return;
        }

        var initialCategories = !string.IsNullOrWhiteSpace(NewCategoryCode) && !string.IsNullOrWhiteSpace(NewCategoryName)
            ? new object[] { new { code = NewCategoryCode.Trim(), name = NewCategoryName.Trim(), active = true } }
            : new object[] { new { code = "GENERAL", name = "Hỗ trợ chung", active = true } };

        IsBusy = true;
        try
        {
            var created = await _api.CreateServiceAsync(NewServiceCode, NewServiceName, NewServiceDescription, active: true, initialCategories);
            _toast.Success($"Đã khởi tạo dịch vụ '{created.Name}' ({created.Code})!");
            NewServiceCode = "";
            NewServiceName = "";
            NewServiceDescription = "";
            NewCategoryCode = "";
            NewCategoryName = "";
            await LoadServicesAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tạo dịch vụ nội bộ");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteAddServiceCategoryAsync()
    {
        if (SelectedService is null) return;
        if (string.IsNullOrWhiteSpace(NewCategoryCode) || string.IsNullOrWhiteSpace(NewCategoryName))
        {
            _toast.Warning("Vui lòng nhập Mã phân loại và Tên phân loại dịch vụ.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.AddServiceCategoryAsync(SelectedService.ServiceId, SelectedService.ETag, NewCategoryCode, NewCategoryName);
            _toast.Success($"Đã thêm phân loại '{NewCategoryName}' vào dịch vụ '{SelectedService.Name}'!");
            NewCategoryCode = "";
            NewCategoryName = "";
            await LoadServicesAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteCreateSlaProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSlaProfileName))
        {
            _toast.Warning("Vui lòng nhập tên hồ sơ SLA.");
            return;
        }

        IsBusy = true;
        try
        {
            var created = await _api.CreateSlaProfileAsync(NewSlaProfileName);
            NewSlaProfileName = "";
            _toast.Success($"Đã tạo hồ sơ SLA bản nháp '{created.Name}'!");
            await LoadSlaProfilesAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tạo hồ sơ SLA");
        }
        finally
        {
            IsBusy = false;
        }
    }
}

// ============================================================================
// 2. REPORTS, EXECUTIVE ANALYTICS & SLA MONITOR VIEWMODEL
// ============================================================================
public sealed class ReportsViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;

    private string _activeReportTab = "Manager"; // Manager, Company, Workload, Sla
    private bool _isBusy;
    private ManagerDashboardResultDto? _managerDashboard;
    private CompanyReportResultDto? _companyReport;
    private WorkloadReportResultDto? _workloadReport;
    private SlaPerformanceReportResultDto? _slaReport;

    public ReportsViewModel(BizFlowApiClient api, SessionManager session)
    {
        _api = api;
        _session = session;

        DepartmentSummaries = [];
        CompanyDepartmentMetrics = [];
        UserWorkloads = [];
        ServiceSlaBreakdown = [];

        LoadCommand = new AsyncRelayCommand(LoadAllReportsAsync);
        SelectTabCommand = new RelayCommand(p => ActiveReportTab = p?.ToString() ?? "Manager");

        _session.SessionChanged += (_, _) => ResetForSession();
    }

    private void ResetForSession()
    {
        ActiveReportTab = "Manager";
        ManagerDashboard = null;
        CompanyReport = null;
        WorkloadReport = null;
        SlaReport = null;
        DepartmentSummaries.Clear();
        CompanyDepartmentMetrics.Clear();
        UserWorkloads.Clear();
        ServiceSlaBreakdown.Clear();
    }

    public ObservableCollection<DepartmentWorkloadSummaryDto> DepartmentSummaries { get; }
    public ObservableCollection<CompanyDepartmentMetricDto> CompanyDepartmentMetrics { get; }
    public ObservableCollection<UserWorkloadItemDto> UserWorkloads { get; }
    public ObservableCollection<ServiceSlaMetricDto> ServiceSlaBreakdown { get; }

    public string ActiveReportTab { get => _activeReportTab; set => SetProperty(ref _activeReportTab, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public ManagerDashboardResultDto? ManagerDashboard { get => _managerDashboard; private set => SetProperty(ref _managerDashboard, value); }
    public CompanyReportResultDto? CompanyReport { get => _companyReport; private set => SetProperty(ref _companyReport, value); }
    public WorkloadReportResultDto? WorkloadReport { get => _workloadReport; private set => SetProperty(ref _workloadReport, value); }
    public SlaPerformanceReportResultDto? SlaReport { get => _slaReport; private set => SetProperty(ref _slaReport, value); }

    public AsyncRelayCommand LoadCommand { get; }
    public RelayCommand SelectTabCommand { get; }

    public async Task LoadAllReportsAsync()
    {
        if (!_session.IsSignedIn) return;
        IsBusy = true;
        try
        {
            try
            {
                ManagerDashboard = await _api.GetManagerDashboardAsync();
                DepartmentSummaries.Clear();
                foreach (var d in ManagerDashboard.DepartmentSummaries) DepartmentSummaries.Add(d);
            }
            catch { }

            try
            {
                CompanyReport = await _api.GetCompanyReportAsync();
                CompanyDepartmentMetrics.Clear();
                foreach (var d in CompanyReport.DepartmentMetrics) CompanyDepartmentMetrics.Add(d);
            }
            catch { }

            try
            {
                WorkloadReport = await _api.GetWorkloadReportAsync();
                UserWorkloads.Clear();
                foreach (var u in WorkloadReport.Items) UserWorkloads.Add(u);
            }
            catch { }

            try
            {
                SlaReport = await _api.GetSlaReportAsync();
                ServiceSlaBreakdown.Clear();
                foreach (var s in SlaReport.ServiceBreakdown) ServiceSlaBreakdown.Add(s);
            }
            catch { }
        }
        finally
        {
            IsBusy = false;
        }
    }
}

// ============================================================================
// 3. SEARCH, NOTIFICATIONS, AUDIT & PLATFORM COMPANIES VIEWMODEL
// ============================================================================
public sealed class OperationsAndPlatformViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;
    private readonly ToastNotificationService _toast;
    private readonly Func<Guid, Task> _openTaskDetail;
    private readonly Func<Guid, Task> _openRequestDetail;

    private bool _isBusy;
    private string _activeSection = "RecordSearch"; // RecordSearch, Notifications, Audit, PlatformCompanies

    // Record Search
    private string _searchQuery = "";
    private string _searchType = "ALL";
    private string _searchStatus = "";
    private string _searchPriority = "";
    private bool _includeArchived;

    // Notifications
    private bool _unreadOnly;
    private int _unreadCount;

    // Audit Logs
    private string _auditActionFilter = "";
    private string _auditActorTypeFilter = "";
    private AuditRowDto? _selectedAuditEvent;

    // Platform Company Registrations
    private string _companySearch = "";
    private string _companyStatusFilter = "";
    private CompanyRegistrationRowDto? _selectedCompany;
    private string _approveTenantKey = "";
    private string _approveAdminCode = "ADMIN01";
    private string _approveAdminEmail = "";
    private string _approveAdminName = "";
    private string _approveAdminPassword = "Password123!";
    private string _rejectCompanyReason = "";

    public OperationsAndPlatformViewModel(
        BizFlowApiClient api,
        SessionManager session,
        ToastNotificationService toast,
        Func<Guid, Task> openTaskDetail,
        Func<Guid, Task> openRequestDetail)
    {
        _api = api;
        _session = session;
        _toast = toast;
        _openTaskDetail = openTaskDetail;
        _openRequestDetail = openRequestDetail;

        SearchResults = [];
        Notifications = [];
        AuditLogs = [];
        CompanyRegistrations = [];

        SearchTypeOptions = ["ALL", "TASK", "REQUEST"];
        SearchStatusOptions = ["", "DRAFT", "SUBMITTED", "ASSIGNED", "ACCEPTED", "IN_PROGRESS", "RESOLVED", "CONFIRMED", "COMPLETED", "CLOSED", "REJECTED", "CANCELLED", "OVERDUE"];
        SearchPriorityOptions = ["", "LOW", "MEDIUM", "HIGH", "CRITICAL"];
        ActorTypeOptions = ["", "USER", "SYSTEM", "AI_AGENT"];
        CompanyStatusOptions = ["", "PENDING", "ACTIVE", "SUSPENDED", "INACTIVE"];

        ExecuteSearchCommand = new AsyncRelayCommand(LoadSearchAsync);
        OpenSearchResultCommand = new AsyncRelayCommand(async p =>
        {
            if (p is not RecordSearchItemDto item) return;
            if (string.Equals(item.RecordType, "TASK", StringComparison.OrdinalIgnoreCase))
                await _openTaskDetail(item.Id);
            else
                await _openRequestDetail(item.Id);
        });

        LoadNotificationsCommand = new AsyncRelayCommand(LoadNotificationsAsync);
        MarkNotificationReadCommand = new AsyncRelayCommand(p => p is NotificationRowDto n ? ExecuteMarkNotificationReadAsync(n) : Task.CompletedTask);

        LoadAuditLogsCommand = new AsyncRelayCommand(LoadAuditLogsAsync);

        LoadCompaniesCommand = new AsyncRelayCommand(LoadCompanyRegistrationsAsync);
        ApproveCompanyCommand = new AsyncRelayCommand(ExecuteApproveCompanyAsync, () => !IsBusy);
        RejectCompanyCommand = new AsyncRelayCommand(ExecuteRejectCompanyAsync, () => !IsBusy);
        UpdateCompanyStatusCommand = new AsyncRelayCommand(p => p is string st ? ExecuteUpdateCompanyStatusAsync(st) : Task.CompletedTask);
        SelectSectionCommand = new RelayCommand(p => ActiveSection = p?.ToString() ?? "RecordSearch");

        _session.SessionChanged += (_, _) => ResetForSession();
    }

    private void ResetForSession()
    {
        SearchQuery = "";
        SearchType = "ALL";
        SearchResults.Clear();
        Notifications.Clear();
        AuditLogs.Clear();
        CompanyRegistrations.Clear();
        SelectedCompany = null;
    }

    public string ActiveSection { get => _activeSection; set => SetProperty(ref _activeSection, value); }
    public RelayCommand SelectSectionCommand { get; }

    public ObservableCollection<RecordSearchItemDto> SearchResults { get; }
    public ObservableCollection<NotificationRowDto> Notifications { get; }
    public ObservableCollection<AuditRowDto> AuditLogs { get; }
    public ObservableCollection<CompanyRegistrationRowDto> CompanyRegistrations { get; }

    public IReadOnlyList<string> SearchTypeOptions { get; }
    public IReadOnlyList<string> SearchStatusOptions { get; }
    public IReadOnlyList<string> SearchPriorityOptions { get; }
    public IReadOnlyList<string> ActorTypeOptions { get; }
    public IReadOnlyList<string> CompanyStatusOptions { get; }

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public string SearchQuery { get => _searchQuery; set => SetProperty(ref _searchQuery, value); }
    public string SearchType { get => _searchType; set => SetProperty(ref _searchType, value); }
    public string SearchStatus { get => _searchStatus; set => SetProperty(ref _searchStatus, value); }
    public string SearchPriority { get => _searchPriority; set => SetProperty(ref _searchPriority, value); }
    public bool IncludeArchived { get => _includeArchived; set => SetProperty(ref _includeArchived, value); }

    public bool UnreadOnly { get => _unreadOnly; set { if (SetProperty(ref _unreadOnly, value)) _ = LoadNotificationsAsync(); } }
    public int UnreadCount { get => _unreadCount; private set => SetProperty(ref _unreadCount, value); }

    public string AuditActionFilter { get => _auditActionFilter; set => SetProperty(ref _auditActionFilter, value); }
    public string AuditActorTypeFilter { get => _auditActorTypeFilter; set => SetProperty(ref _auditActorTypeFilter, value); }
    public AuditRowDto? SelectedAuditEvent { get => _selectedAuditEvent; set => SetProperty(ref _selectedAuditEvent, value); }

    public string CompanySearch { get => _companySearch; set => SetProperty(ref _companySearch, value); }
    public string CompanyStatusFilter { get => _companyStatusFilter; set => SetProperty(ref _companyStatusFilter, value); }
    public CompanyRegistrationRowDto? SelectedCompany
    {
        get => _selectedCompany;
        set
        {
            if (SetProperty(ref _selectedCompany, value) && value is not null)
            {
                ApproveTenantKey = value.TenantKey ?? value.Code.ToLowerInvariant();
                ApproveAdminCode = "ADMIN01";
                ApproveAdminEmail = value.ContactEmail;
                ApproveAdminName = string.IsNullOrWhiteSpace(value.ContactPersonName) ? $"{value.Name} Admin" : value.ContactPersonName!;
                ApproveAdminPassword = "Password123!";
            }
        }
    }
    public string ApproveTenantKey { get => _approveTenantKey; set => SetProperty(ref _approveTenantKey, value); }
    public string ApproveAdminCode { get => _approveAdminCode; set => SetProperty(ref _approveAdminCode, value); }
    public string ApproveAdminEmail { get => _approveAdminEmail; set => SetProperty(ref _approveAdminEmail, value); }
    public string ApproveAdminName { get => _approveAdminName; set => SetProperty(ref _approveAdminName, value); }
    public string ApproveAdminPassword { get => _approveAdminPassword; set => SetProperty(ref _approveAdminPassword, value); }
    public string RejectCompanyReason { get => _rejectCompanyReason; set => SetProperty(ref _rejectCompanyReason, value); }

    public AsyncRelayCommand ExecuteSearchCommand { get; }
    public AsyncRelayCommand OpenSearchResultCommand { get; }
    public AsyncRelayCommand LoadNotificationsCommand { get; }
    public AsyncRelayCommand MarkNotificationReadCommand { get; }
    public AsyncRelayCommand LoadAuditLogsCommand { get; }
    public AsyncRelayCommand LoadCompaniesCommand { get; }
    public AsyncRelayCommand ApproveCompanyCommand { get; }
    public AsyncRelayCommand RejectCompanyCommand { get; }
    public AsyncRelayCommand UpdateCompanyStatusCommand { get; }

    public async Task LoadSearchAsync()
    {
        if (!_session.IsSignedIn) return;
        IsBusy = true;
        try
        {
            var res = await _api.SearchRecordsAsync(SearchQuery, SearchType, SearchStatus, SearchPriority, IncludeArchived, 1, 50);
            SearchResults.Clear();
            foreach (var r in res.Items) SearchResults.Add(r);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadNotificationsAsync()
    {
        if (!_session.IsSignedIn) return;
        IsBusy = true;
        try
        {
            var res = await _api.GetNotificationsAsync(1, 50, UnreadOnly);
            Notifications.Clear();
            foreach (var n in res.Items) Notifications.Add(n);
            UnreadCount = res.UnreadCount;
        }
        catch { }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteMarkNotificationReadAsync(NotificationRowDto notif)
    {
        if (!notif.IsUnread) return;
        IsBusy = true;
        try
        {
            await _api.MarkNotificationReadAsync(notif.NotificationId);
            await LoadNotificationsAsync();
            _toast.Success("Đã đánh dấu đã đọc thông báo.");
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadAuditLogsAsync()
    {
        if (!_session.IsSignedIn) return;
        IsBusy = true;
        try
        {
            var res = await _api.GetAuditLogsAsync(1, 50, AuditActionFilter, AuditActorTypeFilter);
            AuditLogs.Clear();
            foreach (var a in res.Items) AuditLogs.Add(a);
            SelectedAuditEvent = AuditLogs.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadCompanyRegistrationsAsync()
    {
        if (!_session.IsSignedIn) return;
        IsBusy = true;
        try
        {
            var res = await _api.GetCompanyRegistrationsAsync(1, 50, CompanyStatusFilter, CompanySearch);
            CompanyRegistrations.Clear();
            foreach (var c in res.Items) CompanyRegistrations.Add(c);
            SelectedCompany ??= CompanyRegistrations.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteApproveCompanyAsync()
    {
        if (SelectedCompany is null) return;
        if (string.IsNullOrWhiteSpace(ApproveTenantKey) || string.IsNullOrWhiteSpace(ApproveAdminEmail) || string.IsNullOrWhiteSpace(ApproveAdminPassword))
        {
            _toast.Warning("Vui lòng nhập đầy đủ Tenant Key, Email Admin và Mật khẩu khởi tạo.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.ApproveCompanyRegistrationAsync(
                SelectedCompany.CompanyId,
                ApproveTenantKey,
                ApproveAdminCode,
                ApproveAdminEmail,
                ApproveAdminName,
                ApproveAdminPassword);
            _toast.Success($"Đã phê duyệt và cấp phát Workspace '{ApproveTenantKey}' cho doanh nghiệp '{SelectedCompany.Name}'!");
            await LoadCompanyRegistrationsAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi phê duyệt doanh nghiệp");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteRejectCompanyAsync()
    {
        if (SelectedCompany is null) return;
        if (string.IsNullOrWhiteSpace(RejectCompanyReason) || RejectCompanyReason.Trim().Length < 5)
        {
            _toast.Warning("Vui lòng nhập lý do từ chối hồ sơ doanh nghiệp (tối thiểu 5 ký tự).");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.RejectCompanyRegistrationAsync(SelectedCompany.CompanyId, RejectCompanyReason);
            RejectCompanyReason = "";
            _toast.Info($"Đã từ chối hồ sơ đăng ký của '{SelectedCompany.Name}'.");
            await LoadCompanyRegistrationsAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteUpdateCompanyStatusAsync(string nextStatus)
    {
        if (SelectedCompany is null) return;
        IsBusy = true;
        try
        {
            await _api.UpdateCompanyStatusAsync(SelectedCompany.CompanyId, nextStatus);
            _toast.Success($"Đã chuyển trạng thái doanh nghiệp '{SelectedCompany.Name}' sang {nextStatus}.");
            await LoadCompanyRegistrationsAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

// ============================================================================
// 4. AI ROBOT ASSISTANT & AUTONOMOUS INSIGHTS VIEWMODEL
// ============================================================================
public sealed class AiRobotAssistantViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;
    private readonly ToastNotificationService _toast;

    private bool _isOpen;
    private bool _isBusy;
    private string _chatInput = "";
    private string _sessionId = Guid.NewGuid().ToString("N");
    private string _documentTitle = "Biên bản họp giao ban vận hành";
    private string _documentContent = "1. Phòng CNTT hoàn tất nâng cấp tường lửa trước thứ Sáu.\n2. Phòng Hành chính rà soát hợp đồng nhà cung cấp thiết bị in ấn.\n3. Báo cáo tiến độ SLA toàn công ty gửi Ban Giám đốc.";
    private string _documentSummaryResult = "";

    public AiRobotAssistantViewModel(BizFlowApiClient api, SessionManager session, ToastNotificationService toast)
    {
        _api = api;
        _session = session;
        _toast = toast;

        Messages = [];
        ProactiveInsights = [];
        ResetWelcomeMessage();

        SendChatCommand = new AsyncRelayCommand(ExecuteSendChatAsync, () => !IsBusy);
        QuickPromptCommand = new AsyncRelayCommand(p =>
        {
            ChatInput = p?.ToString() ?? "/notifications";
            return ExecuteSendChatAsync();
        }, _ => !IsBusy);
        ClearChatCommand = new RelayCommand(() =>
        {
            _sessionId = Guid.NewGuid().ToString("N");
            ResetWelcomeMessage();
        });
        SummarizeDocumentCommand = new AsyncRelayCommand(ExecuteSummarizeDocumentAsync, () => !IsBusy);
        TriggerProactiveScanCommand = new AsyncRelayCommand(ExecuteTriggerProactiveScanAsync, () => !IsBusy);

        _session.SessionChanged += (_, _) => ResetForSession();
    }

    private void ResetForSession()
    {
        ChatInput = "";
        DocumentTitle = "";
        DocumentContent = "";
        DocumentSummaryResult = "";
        ProactiveInsights.Clear();
        ResetWelcomeMessage();
    }

    public ObservableCollection<ChatMessageItem> Messages { get; }
    public ObservableCollection<ProactiveInsightItemDto> ProactiveInsights { get; }

    public bool IsOpen { get => _isOpen; set => SetProperty(ref _isOpen, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string ChatInput { get => _chatInput; set => SetProperty(ref _chatInput, value); }
    public string DocumentTitle { get => _documentTitle; set => SetProperty(ref _documentTitle, value); }
    public string DocumentContent { get => _documentContent; set => SetProperty(ref _documentContent, value); }
    public string DocumentSummaryResult { get => _documentSummaryResult; set => SetProperty(ref _documentSummaryResult, value); }

    public AsyncRelayCommand SendChatCommand { get; }
    public AsyncRelayCommand QuickPromptCommand { get; }
    public RelayCommand ClearChatCommand { get; }
    public AsyncRelayCommand SummarizeDocumentCommand { get; }
    public AsyncRelayCommand TriggerProactiveScanCommand { get; }

    public void ResetWelcomeMessage()
    {
        Messages.Clear();
        Messages.Add(new ChatMessageItem
        {
            Sender = "robot",
            SenderLabel = "🤖 Trợ lý AI BizFlow",
            Text = $"Xin chào {_session.WelcomeName}! Tôi là Trợ lý AI Agent BizFlow.\nBạn có thể nhập câu hỏi tự nhiên hoặc dùng lệnh nhanh:\n• /notifications — Kiểm tra thông báo & deadline\n• /task <nội dung> — Phân rã kế hoạch công việc tự động",
            Time = DateTime.Now.ToString("HH:mm")
        });
    }

    private async Task ExecuteSendChatAsync()
    {
        var prompt = (ChatInput ?? "").Trim();
        if (string.IsNullOrEmpty(prompt)) return;

        ChatInput = "";
        Messages.Add(new ChatMessageItem
        {
            Sender = "user",
            SenderLabel = $"👤 {_session.WelcomeName}",
            Text = prompt,
            Time = DateTime.Now.ToString("HH:mm")
        });

        IsBusy = true;
        try
        {
            var res = await _api.SendRobotChatMessageAsync(prompt, _sessionId);
            var replyText = res.Reply;
            if (res.TaskProposals is { Count: > 0 })
            {
                replyText += "\n\n📋 Đề xuất nhánh công việc từ AI:\n" +
                    string.Join("\n", res.TaskProposals.Select((p, idx) => $"  {idx + 1}. {p.Title} [{p.DepartmentName ?? "Phòng chuyên môn"} - {p.Priority ?? "MEDIUM"}]"));
            }

            Messages.Add(new ChatMessageItem
            {
                Sender = "robot",
                SenderLabel = "🤖 Trợ lý AI BizFlow",
                Text = replyText,
                Time = DateTime.Now.ToString("HH:mm"),
                AgentThought = res.AgentThought
            });
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageItem
            {
                Sender = "robot",
                SenderLabel = "🤖 Trợ lý AI BizFlow",
                Text = $"Phản hồi từ hệ thống: {ex.Message}",
                Time = DateTime.Now.ToString("HH:mm")
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteSummarizeDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(DocumentContent))
        {
            _toast.Warning("Vui lòng nhập nội dung văn bản cần tóm tắt.");
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _api.SummarizeDocumentAsync(DocumentTitle, DocumentContent);
            var points = res.KeyPoints is { Count: > 0 } ? string.Join("\n• ", res.KeyPoints) : "";
            var actions = res.ActionItems is { Count: > 0 } ? string.Join("\n• ", res.ActionItems.Select(a => $"{a.Title} ({a.Priority})")) : "";
            DocumentSummaryResult = $"📄 TÓM TẮT ĐIỀU HÀNH:\n{res.ExecutiveSummary}\n\n📌 Ý CHÍNH:\n• {points}\n\n⚡ HÀNH ĐỘNG ĐỀ XUẤT:\n• {actions}";
            _toast.Success("AI đã tóm tắt tài liệu và trích xuất hạng mục công việc!");
        }
        catch (Exception ex)
        {
            DocumentSummaryResult = $"Lỗi tóm tắt tài liệu: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteTriggerProactiveScanAsync()
    {
        IsBusy = true;
        try
        {
            await _api.TriggerProactiveScanAsync();
            var insights = await _api.GetProactiveInsightsAsync();
            ProactiveInsights.Clear();
            foreach (var item in insights) ProactiveInsights.Add(item);
            _toast.Success($"Đã quét chủ động toàn doanh nghiệp ({ProactiveInsights.Count} khuyến nghị AI).");
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
