using System.Collections.ObjectModel;
using System.Text.Json;
using BizFlow.Desktop.Core;
using BizFlow.Desktop.Models;
using BizFlow.Desktop.Services;

namespace BizFlow.Desktop.ViewModels;

// ============================================================================
// 1. APPROVALS CENTER VIEWMODEL
// ============================================================================
public sealed class ApprovalsViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;
    private readonly ToastNotificationService _toast;
    private readonly Func<Guid, Task> _openTaskDetail;
    private readonly Func<Guid, Task> _openRequestDetail;

    private bool _isBusy;
    private string _filterType = "ALL"; // ALL, TASK, REQUEST
    private string _searchQuery = "";
    private string _approvalNote = "";
    private readonly List<PendingApprovalItemModel> _allItems = [];

    public ApprovalsViewModel(
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

        FilteredApprovals = [];

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SetFilterCommand = new RelayCommand(p =>
        {
            FilterType = p?.ToString() ?? "ALL";
            ApplyLocalFilter();
        });
        InspectItemCommand = new AsyncRelayCommand(async p =>
        {
            if (p is not PendingApprovalItemModel item) return;
            if (item.ApprovalType == "TASK_CONFIRMATION")
                await _openTaskDetail(item.SourceId);
            else
                await _openRequestDetail(item.SourceId);
        });
        QuickApproveCommand = new AsyncRelayCommand(p => p is PendingApprovalItemModel item ? ExecuteQuickDecisionAsync(item, approve: true) : Task.CompletedTask);
        QuickRejectCommand = new AsyncRelayCommand(p => p is PendingApprovalItemModel item ? ExecuteQuickDecisionAsync(item, approve: false) : Task.CompletedTask);

        _session.SessionChanged += (_, _) => ResetForSession();
    }

    private void ResetForSession()
    {
        _allItems.Clear();
        FilteredApprovals.Clear();
        ApprovalNote = "";
        SearchQuery = "";
        FilterType = "ALL";
        OnPropertyChanged(nameof(TotalPendingCount));
        OnPropertyChanged(nameof(TaskPendingCount));
        OnPropertyChanged(nameof(RequestPendingCount));
    }

    public ObservableCollection<PendingApprovalItemModel> FilteredApprovals { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public string FilterType { get => _filterType; set => SetProperty(ref _filterType, value); }
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                ApplyLocalFilter();
        }
    }
    public string ApprovalNote { get => _approvalNote; set => SetProperty(ref _approvalNote, value); }

    public int TotalPendingCount => _allItems.Count;
    public int TaskPendingCount => _allItems.Count(x => x.ApprovalType == "TASK_CONFIRMATION");
    public int RequestPendingCount => _allItems.Count(x => x.ApprovalType == "REQUEST_RESOLUTION");

    public AsyncRelayCommand LoadCommand { get; }
    public RelayCommand SetFilterCommand { get; }
    public AsyncRelayCommand InspectItemCommand { get; }
    public AsyncRelayCommand QuickApproveCommand { get; }
    public AsyncRelayCommand QuickRejectCommand { get; }

    public async Task LoadAsync()
    {
        if (!_session.IsSignedIn) return;
        IsBusy = true;
        try
        {
            _allItems.Clear();

            // 1. Submitted tasks awaiting confirmation
            if (_session.HasPermission("tasks.confirm"))
            {
                try
                {
                    var tasks = await _api.GetTasksAsync(1, 50, status: "SUBMITTED");
                    foreach (var t in tasks.Items)
                    {
                        _allItems.Add(new PendingApprovalItemModel(
                            t.TaskId,
                            "TASK_CONFIRMATION",
                            "NGHIỆM THU NHIỆM VỤ",
                            t.Title,
                            t.TaskId,
                            t.AssignedUserName ?? "Nhân sự thực hiện",
                            t.AssignedDepartmentName,
                            t.Priority,
                            t.CreatedAt,
                            "Đã nộp kết quả bàn giao công việc. Đang chờ cấp quản lý xem xét và nghiệm thu hoàn thành.",
                            t.Status));
                    }
                }
                catch { }
            }

            // 2. Requests awaiting receipt (ROUTED) or requester confirmation (RESOLVED)
            try
            {
                if (_session.HasPermission("requests.receive"))
                {
                    var routedReqs = await _api.GetRequestsAsync(1, 50, status: "ROUTED");
                    foreach (var r in routedReqs.Items)
                    {
                        _allItems.Add(new PendingApprovalItemModel(
                            r.RequestId,
                            "REQUEST_RESOLUTION",
                            "TIẾP NHẬN YÊU CẦU ĐÃ ĐIỀU PHỐI",
                            r.Title,
                            r.RequestId,
                            r.RequesterName,
                            r.ServiceName,
                            r.Priority,
                            r.CreatedAt,
                            $"Yêu cầu dịch vụ [{r.ServiceName} / {r.CategoryName}] đã được điều phối và đang chờ tiếp nhận xử lý.",
                            r.Status));
                    }
                }

                if (_session.HasPermission("requests.confirm"))
                {
                    var resolvedReqs = await _api.GetRequestsAsync(1, 50, status: "RESOLVED");
                    var currentUserId = _session.Identity?.UserId;
                    foreach (var r in resolvedReqs.Items.Where(x => currentUserId.HasValue && x.RequesterId == currentUserId.Value))
                    {
                        _allItems.Add(new PendingApprovalItemModel(
                            r.RequestId,
                            "REQUEST_RESOLUTION",
                            "XÁC NHẬN KẾT QUẢ YÊU CẦU",
                            r.Title,
                            r.RequestId,
                            r.RequesterName,
                            r.ServiceName,
                            r.Priority,
                            r.UpdatedAt,
                            "Yêu cầu đã được bộ phận chuyên môn giải quyết. Đang chờ bạn (người gửi) xác nhận đóng hồ sơ.",
                            r.Status));
                    }
                }
            }
            catch { }

            OnPropertyChanged(nameof(TotalPendingCount));
            OnPropertyChanged(nameof(TaskPendingCount));
            OnPropertyChanged(nameof(RequestPendingCount));
            ApplyLocalFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyLocalFilter()
    {
        FilteredApprovals.Clear();
        var q = (SearchQuery ?? "").Trim().ToLowerInvariant();
        foreach (var item in _allItems)
        {
            if (FilterType == "TASK" && item.ApprovalType != "TASK_CONFIRMATION") continue;
            if (FilterType == "REQUEST" && item.ApprovalType != "REQUEST_RESOLUTION") continue;
            if (!string.IsNullOrEmpty(q) &&
                !item.Title.ToLowerInvariant().Contains(q) &&
                !item.RequesterOrPerformer.ToLowerInvariant().Contains(q))
                continue;
            FilteredApprovals.Add(item);
        }
    }

    private async Task ExecuteQuickDecisionAsync(PendingApprovalItemModel item, bool approve)
    {
        if (!approve && string.IsNullOrWhiteSpace(ApprovalNote))
        {
            _toast.Warning("Vui lòng nhập ghi chú lý do từ chối hoặc yêu cầu làm lại trước khi xác nhận.", "Thiếu lý do");
            return;
        }

        var note = string.IsNullOrWhiteSpace(ApprovalNote)
            ? "Phê duyệt từ Trung tâm Phê duyệt BizFlow Desktop"
            : ApprovalNote.Trim();

        IsBusy = true;
        try
        {
            if (item.ApprovalType == "TASK_CONFIRMATION")
            {
                await _api.ConfirmTaskResultAsync(item.SourceId, approve ? "CONFIRMED" : "REWORK", note);
                _toast.Success(approve
                    ? $"Đã nghiệm thu hoàn thành nhiệm vụ '{item.Title}'!"
                    : $"Đã gửi yêu cầu làm lại cho nhiệm vụ '{item.Title}'.");
            }
            else
            {
                if (item.Status == "RESOLVED")
                {
                    await _api.ConfirmRequestAsync(item.SourceId, approve ? "CONFIRMED" : "REWORK", note);
                    _toast.Success(approve
                        ? $"Đã xác nhận đóng yêu cầu '{item.Title}'!"
                        : $"Đã yêu cầu xử lý lại '{item.Title}'.");
                }
                else if (item.Status == "ROUTED")
                {
                    if (approve)
                    {
                        await _api.ReceiveRequestAsync(item.SourceId, note);
                        _toast.Success($"Đã tiếp nhận xử lý yêu cầu '{item.Title}'!");
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(note))
                        {
                            _toast.Warning("Vui lòng nhập ghi chú lý do từ chối yêu cầu.");
                            return;
                        }
                        await _api.RejectRequestAsync(item.SourceId, note);
                        _toast.Info($"Đã từ chối yêu cầu '{item.Title}'.");
                    }
                }
                else
                {
                    if (!approve)
                    {
                        if (string.IsNullOrWhiteSpace(note))
                        {
                            _toast.Warning("Vui lòng nhập ghi chú lý do từ chối yêu cầu.");
                            return;
                        }
                        await _api.RejectRequestAsync(item.SourceId, note);
                        _toast.Info($"Đã từ chối yêu cầu '{item.Title}'.");
                    }
                    else
                    {
                        _toast.Warning("Yêu cầu ở trạng thái SUBMITTED cần được điều phối (Route) tại màn hình Yêu cầu trước khi tiếp nhận xử lý.");
                        return;
                    }
                }
            }

            ApprovalNote = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Thao tác phê duyệt thất bại");
        }
        finally
        {
            IsBusy = false;
        }
    }
}

// ============================================================================
// 2. WORKFLOW MANAGEMENT & VISUAL WORKFLOW DESIGNER VIEWMODEL
// ============================================================================
public sealed class WorkflowsViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;
    private readonly ToastNotificationService _toast;

    // Modes: "Library", "Designer"
    private string _activeMode = "Library";
    private bool _isBusy;
    private string _errorMessage = "";

    // Library Filters
    private string _searchQuery = "";
    private string _selectedBusinessType = "";
    private string _selectedStatus = "";
    private string _newWorkflowName = "";
    private string _newWorkflowBusinessType = "REQUEST";

    // Selected Workflow & Designer State
    private WorkflowRowDto? _selectedWorkflow;
    private WorkflowVersionViewDto? _currentVersion;
    private string _editableWorkflowName = "";
    private DesignerNodeModel? _selectedNode;
    private DesignerEdgeModel? _selectedEdge;
    private DesignerNodeModel? _connectFromNode;
    private DesignerNodeModel? _connectToNode;
    private string _newEdgeLabel = "";
    private WorkflowServiceLookupDto? _publishAssociateService;
    private string _aiWorkflowDocumentText = "Quy trình xử lý yêu cầu mua sắm thiết bị CNTT:\nBước 1: Nhân viên gửi yêu cầu dịch vụ CNTT.\nBước 2: Phòng Công nghệ & Chuyển đổi số kiểm tra kỹ thuật (SLA 24h).\nBước 3: Trưởng phòng phê duyệt ngân sách (SLA 12h).\nBước 4: Người yêu cầu xác nhận bàn giao thiết bị và hoàn tất.";

    public WorkflowsViewModel(BizFlowApiClient api, SessionManager session, ToastNotificationService toast)
    {
        _api = api;
        _session = session;
        _toast = toast;

        Workflows = [];
        VersionHistory = [];
        DesignerNodes = [];
        DesignerEdges = [];
        ValidationMessages = [];
        LookupDepartments = [];
        LookupRoles = [];
        LookupUsers = [];
        LookupServices = [];

        BusinessTypeOptions = ["", "REQUEST", "TASK"];
        CreateBusinessTypeOptions = ["REQUEST", "TASK"];
        StatusOptions = ["", "DRAFT", "ACTIVE", "INACTIVE"];
        NodeTypeOptions = ["DEPARTMENT", "APPROVAL", "DECISION", "CONFIRMATION", "END"];
        AssignmentMethodOptions = ["DEPARTMENT_QUEUE", "BUSINESS_ROLE", "DEPARTMENT_MANAGER", "DIRECT_MANAGER", "SPECIFIC_USER", "REQUESTER"];
        ApprovalPolicyOptions = ["SINGLE", "ALL", "ANY"];
        MilestoneOptions = ["RECEIVE", "RESULT", "RESOLUTION"];

        LoadCommand = new AsyncRelayCommand(LoadLibraryAsync);
        CreateWorkflowCommand = new AsyncRelayCommand(ExecuteCreateWorkflowAsync, () => !IsBusy);
        ToggleStatusCommand = new AsyncRelayCommand(p => p is WorkflowRowDto row ? ExecuteToggleStatusAsync(row) : Task.CompletedTask);
        DeleteWorkflowCommand = new AsyncRelayCommand(p => p is WorkflowRowDto row ? ExecuteDeleteWorkflowAsync(row) : Task.CompletedTask);
        OpenDesignerCommand = new AsyncRelayCommand(p => p is WorkflowRowDto row ? OpenWorkflowDesignerAsync(row) : Task.CompletedTask);
        BackToLibraryCommand = new RelayCommand(() => ActiveMode = "Library");

        CreateNextVersionCommand = new AsyncRelayCommand(ExecuteCreateNextVersionAsync, () => !IsBusy);
        AddNodeCommand = new RelayCommand(p => AddDesignerNode(p?.ToString() ?? "DEPARTMENT"));
        DeleteSelectedNodeCommand = new RelayCommand(DeleteSelectedNode);
        ConnectNodesCommand = new RelayCommand(ConnectSelectedNodes);
        DeleteEdgeCommand = new RelayCommand(p =>
        {
            if (p is DesignerEdgeModel edge) DesignerEdges.Remove(edge);
        });
        AutoLayoutCommand = new RelayCommand(ExecuteAutoLayout);
        SaveDraftCommand = new AsyncRelayCommand(ExecuteSaveDraftAsync, () => !IsBusy);
        ValidateWorkflowCommand = new AsyncRelayCommand(ExecuteValidateWorkflowAsync, () => !IsBusy);
        PublishWorkflowCommand = new AsyncRelayCommand(ExecutePublishWorkflowAsync, () => !IsBusy);
        AnalyzeAiWorkflowTextCommand = new AsyncRelayCommand(ExecuteAiAnalyzeWorkflowAsync, () => !IsBusy);

        _session.SessionChanged += (_, _) => ResetForSession();
    }

    private void ResetForSession()
    {
        ActiveMode = "List";
        ErrorMessage = "";
        SearchQuery = "";
        SelectedBusinessType = "";
        SelectedStatus = "";
        SelectedWorkflow = null;
        CurrentVersion = null;
        SelectedNode = null;
        SelectedEdge = null;
        ConnectFromNode = null;
        ConnectToNode = null;
        Workflows.Clear();
        VersionHistory.Clear();
        DesignerNodes.Clear();
        DesignerEdges.Clear();
        ValidationMessages.Clear();
        LookupDepartments.Clear();
        LookupRoles.Clear();
        LookupUsers.Clear();
        LookupServices.Clear();
        OnPropertyChanged(nameof(CanConfigureWorkflows));
    }

    public SessionManager Session => _session;
    public ObservableCollection<WorkflowRowDto> Workflows { get; }
    public ObservableCollection<WorkflowVersionSummaryDto> VersionHistory { get; }
    public ObservableCollection<DesignerNodeModel> DesignerNodes { get; }
    public ObservableCollection<DesignerEdgeModel> DesignerEdges { get; }
    public ObservableCollection<string> ValidationMessages { get; }
    public ObservableCollection<WorkflowLookupItemDto> LookupDepartments { get; }
    public ObservableCollection<WorkflowLookupItemDto> LookupRoles { get; }
    public ObservableCollection<WorkflowUserLookupDto> LookupUsers { get; }
    public ObservableCollection<WorkflowServiceLookupDto> LookupServices { get; }

    public IReadOnlyList<string> BusinessTypeOptions { get; }
    public IReadOnlyList<string> CreateBusinessTypeOptions { get; }
    public IReadOnlyList<string> StatusOptions { get; }
    public IReadOnlyList<string> NodeTypeOptions { get; }
    public IReadOnlyList<string> AssignmentMethodOptions { get; }
    public IReadOnlyList<string> ApprovalPolicyOptions { get; }
    public IReadOnlyList<string> MilestoneOptions { get; }

    public string ActiveMode { get => _activeMode; set => SetProperty(ref _activeMode, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool CanConfigureWorkflows => _session.HasPermission("workflows.configure");
    public bool IsDraftVersion => CurrentVersion is null || string.Equals(CurrentVersion.Status, "DRAFT", StringComparison.OrdinalIgnoreCase);

    public string SearchQuery { get => _searchQuery; set => SetProperty(ref _searchQuery, value); }
    public string SelectedBusinessType { get => _selectedBusinessType; set => SetProperty(ref _selectedBusinessType, value); }
    public string SelectedStatus { get => _selectedStatus; set => SetProperty(ref _selectedStatus, value); }
    public string NewWorkflowName { get => _newWorkflowName; set => SetProperty(ref _newWorkflowName, value); }
    public string NewWorkflowBusinessType { get => _newWorkflowBusinessType; set => SetProperty(ref _newWorkflowBusinessType, value); }

    public WorkflowRowDto? SelectedWorkflow { get => _selectedWorkflow; set => SetProperty(ref _selectedWorkflow, value); }
    public WorkflowVersionViewDto? CurrentVersion
    {
        get => _currentVersion;
        private set
        {
            if (SetProperty(ref _currentVersion, value))
                OnPropertyChanged(nameof(IsDraftVersion));
        }
    }
    public string EditableWorkflowName { get => _editableWorkflowName; set => SetProperty(ref _editableWorkflowName, value); }
    public DesignerNodeModel? SelectedNode { get => _selectedNode; set => SetProperty(ref _selectedNode, value); }
    public DesignerEdgeModel? SelectedEdge { get => _selectedEdge; set => SetProperty(ref _selectedEdge, value); }
    public DesignerNodeModel? ConnectFromNode { get => _connectFromNode; set => SetProperty(ref _connectFromNode, value); }
    public DesignerNodeModel? ConnectToNode { get => _connectToNode; set => SetProperty(ref _connectToNode, value); }
    public string NewEdgeLabel { get => _newEdgeLabel; set => SetProperty(ref _newEdgeLabel, value); }
    public WorkflowServiceLookupDto? PublishAssociateService { get => _publishAssociateService; set => SetProperty(ref _publishAssociateService, value); }
    public string AiWorkflowDocumentText { get => _aiWorkflowDocumentText; set => SetProperty(ref _aiWorkflowDocumentText, value); }

    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand CreateWorkflowCommand { get; }
    public AsyncRelayCommand ToggleStatusCommand { get; }
    public AsyncRelayCommand DeleteWorkflowCommand { get; }
    public AsyncRelayCommand OpenDesignerCommand { get; }
    public RelayCommand BackToLibraryCommand { get; }
    public AsyncRelayCommand CreateNextVersionCommand { get; }
    public RelayCommand AddNodeCommand { get; }
    public RelayCommand DeleteSelectedNodeCommand { get; }
    public RelayCommand ConnectNodesCommand { get; }
    public RelayCommand DeleteEdgeCommand { get; }
    public RelayCommand AutoLayoutCommand { get; }
    public AsyncRelayCommand SaveDraftCommand { get; }
    public AsyncRelayCommand ValidateWorkflowCommand { get; }
    public AsyncRelayCommand PublishWorkflowCommand { get; }
    public AsyncRelayCommand AnalyzeAiWorkflowTextCommand { get; }

    public async Task LoadLibraryAsync()
    {
        if (!_session.IsSignedIn) return;
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            OnPropertyChanged(nameof(CanConfigureWorkflows));
            var page = await _api.GetWorkflowsAsync(1, 50, SearchQuery, SelectedBusinessType, SelectedStatus);
            Workflows.Clear();
            foreach (var w in page.Items) Workflows.Add(w);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteCreateWorkflowAsync()
    {
        if (!CanConfigureWorkflows)
        {
            _toast.Warning("Chỉ Quản trị viên Doanh nghiệp mới có quyền cấu hình quy trình.");
            return;
        }
        if (string.IsNullOrWhiteSpace(NewWorkflowName))
        {
            _toast.Warning("Vui lòng nhập tên quy trình mới.");
            return;
        }

        IsBusy = true;
        try
        {
            var created = await _api.CreateWorkflowAsync(NewWorkflowName, NewWorkflowBusinessType);
            NewWorkflowName = "";
            _toast.Success($"Đã tạo quy trình bản nháp '{created.Name}'!");
            await LoadLibraryAsync();
            await OpenWorkflowDesignerAsync(created);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi khởi tạo quy trình");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteToggleStatusAsync(WorkflowRowDto row)
    {
        var nextStatus = row.Status == "ACTIVE" ? "INACTIVE" : "ACTIVE";
        IsBusy = true;
        try
        {
            await _api.UpdateWorkflowStatusAsync(row.WorkflowId, nextStatus);
            _toast.Success($"Đã chuyển trạng thái quy trình '{row.Name}' sang {nextStatus}.");
            await LoadLibraryAsync();
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

    private async Task ExecuteDeleteWorkflowAsync(WorkflowRowDto row)
    {
        IsBusy = true;
        try
        {
            await _api.DeleteWorkflowAsync(row.WorkflowId);
            _toast.Success($"Đã xóa quy trình bản nháp '{row.Name}'.");
            await LoadLibraryAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Không thể xóa quy trình");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenWorkflowDesignerAsync(WorkflowRowDto row)
    {
        SelectedWorkflow = row;
        IsBusy = true;
        ValidationMessages.Clear();
        try
        {
            await LoadDesignerLookupsAsync();
            var versions = await _api.GetWorkflowVersionsAsync(row.WorkflowId);
            VersionHistory.Clear();
            foreach (var v in versions) VersionHistory.Add(v);

            var versionId = row.LatestVersionId ?? versions.FirstOrDefault()?.VersionId;
            if (!versionId.HasValue)
            {
                _toast.Error("Quy trình chưa có phiên bản nào.");
                return;
            }

            var detail = await _api.GetWorkflowVersionDetailAsync(versionId.Value);
            CurrentVersion = detail;
            EditableWorkflowName = detail.Name;
            LoadGraphFromDefinition(detail);
            ActiveMode = "Designer";
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Không thể mở trình thiết kế quy trình");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDesignerLookupsAsync()
    {
        if (LookupDepartments.Count == 0)
        {
            try
            {
                var depts = await _api.GetWorkflowDepartmentsLookupAsync();
                foreach (var d in depts) LookupDepartments.Add(d);
            }
            catch { }
        }
        if (LookupRoles.Count == 0)
        {
            try
            {
                var roles = await _api.GetWorkflowRolesLookupAsync();
                foreach (var r in roles) LookupRoles.Add(r);
            }
            catch { }
        }
        if (LookupUsers.Count == 0)
        {
            try
            {
                var users = await _api.GetWorkflowUsersLookupAsync();
                foreach (var u in users) LookupUsers.Add(u);
            }
            catch { }
        }
        if (LookupServices.Count == 0)
        {
            try
            {
                var svcs = await _api.GetWorkflowServicesLookupAsync();
                foreach (var s in svcs) LookupServices.Add(s);
            }
            catch { }
        }
    }

    private void LoadGraphFromDefinition(WorkflowVersionViewDto detail)
    {
        DesignerNodes.Clear();
        DesignerEdges.Clear();

        try
        {
            if (detail.Definition.ValueKind == JsonValueKind.Object)
            {
                if (detail.Definition.TryGetProperty("nodes", out var nodesProp) && nodesProp.ValueKind == JsonValueKind.Array)
                {
                    var parsedNodes = JsonSerializer.Deserialize<List<DesignerNodeModel>>(nodesProp.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (parsedNodes is { Count: > 0 })
                    {
                        foreach (var n in parsedNodes) DesignerNodes.Add(n);
                    }
                }
                if (detail.Definition.TryGetProperty("edges", out var edgesProp) && edgesProp.ValueKind == JsonValueKind.Array)
                {
                    var parsedEdges = JsonSerializer.Deserialize<List<DesignerEdgeModel>>(edgesProp.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (parsedEdges is not null)
                    {
                        foreach (var e in parsedEdges) DesignerEdges.Add(e);
                    }
                }
            }
        }
        catch { }

        if (DesignerNodes.Count == 0)
        {
            InitDefaultGraph(detail.BusinessType);
        }

        SelectedNode = DesignerNodes.FirstOrDefault();
        ConnectFromNode = DesignerNodes.FirstOrDefault();
        ConnectToNode = DesignerNodes.Skip(1).FirstOrDefault();
    }

    private void InitDefaultGraph(string businessType)
    {
        var firstDept = LookupDepartments.FirstOrDefault();
        var start = new DesignerNodeModel
        {
            Id = "node-start",
            Type = "START",
            Name = businessType == "REQUEST" ? "Nộp yêu cầu" : "Tạo nhiệm vụ",
            X = 40,
            Y = 120,
            Trigger = businessType == "REQUEST" ? "REQUEST_CREATED" : "TASK_CREATED"
        };
        var step1 = new DesignerNodeModel
        {
            Id = "node-step-1",
            Type = "DEPARTMENT",
            Name = "Tiếp nhận & Xử lý chuyên môn",
            X = 280,
            Y = 120,
            DepartmentId = firstDept?.Id.ToString(),
            DepartmentName = firstDept?.Name ?? "Phòng Công nghệ & Chuyển đổi số",
            AssignmentMethod = "DEPARTMENT_QUEUE",
            SlaHours = 24
        };
        var end = new DesignerNodeModel
        {
            Id = "node-end",
            Type = "END",
            Name = "Hoàn tất quy trình",
            X = 540,
            Y = 120,
            CompletionState = "COMPLETED"
        };

        DesignerNodes.Add(start);
        DesignerNodes.Add(step1);
        DesignerNodes.Add(end);

        DesignerEdges.Add(new DesignerEdgeModel { Id = "e-1", Source = start.Id, Target = step1.Id, Label = "Khởi tạo" });
        DesignerEdges.Add(new DesignerEdgeModel { Id = "e-2", Source = step1.Id, Target = end.Id, Label = "Hoàn thành" });
    }

    public void AddDesignerNode(string nodeType)
    {
        if (!IsDraftVersion)
        {
            _toast.Warning("Phiên bản đã ban hành là bất biến. Hãy tạo phiên bản nháp mới để chỉnh sửa.");
            return;
        }

        var idx = DesignerNodes.Count + 1;
        var firstDept = LookupDepartments.FirstOrDefault();
        var firstRole = LookupRoles.FirstOrDefault();

        var node = new DesignerNodeModel
        {
            Id = $"node-{idx}-{Guid.NewGuid().ToString("N")[..4]}",
            Type = nodeType,
            Name = nodeType switch
            {
                "APPROVAL" => $"Phê duyệt cấp quản lý #{idx}",
                "DECISION" => $"Phân nhánh điều kiện #{idx}",
                "CONFIRMATION" => $"Xác nhận kết quả #{idx}",
                "END" => "Kết thúc quy trình",
                _ => $"Xử lý phòng ban #{idx}"
            },
            X = 60 + (idx % 4) * 230,
            Y = 100 + (idx / 4) * 140,
            DepartmentId = firstDept?.Id.ToString(),
            DepartmentName = firstDept?.Name,
            RoleId = firstRole?.Id.ToString(),
            RoleName = firstRole?.Name,
            SlaHours = 24
        };

        DesignerNodes.Add(node);
        SelectedNode = node;
    }

    private void DeleteSelectedNode()
    {
        if (!IsDraftVersion || SelectedNode is null) return;
        var id = SelectedNode.Id;
        var toRemoveEdges = DesignerEdges.Where(e => e.Source == id || e.Target == id).ToList();
        foreach (var e in toRemoveEdges) DesignerEdges.Remove(e);
        DesignerNodes.Remove(SelectedNode);
        SelectedNode = DesignerNodes.FirstOrDefault();
    }

    private void ConnectSelectedNodes()
    {
        if (!IsDraftVersion || ConnectFromNode is null || ConnectToNode is null) return;
        if (ConnectFromNode.Id == ConnectToNode.Id)
        {
            _toast.Warning("Không thể nối một bước vào chính nó.");
            return;
        }
        if (DesignerEdges.Any(e => e.Source == ConnectFromNode.Id && e.Target == ConnectToNode.Id))
        {
            _toast.Info("Liên kết giữa hai bước này đã tồn tại.");
            return;
        }

        DesignerEdges.Add(new DesignerEdgeModel
        {
            Id = $"e-{Guid.NewGuid().ToString("N")[..6]}",
            Source = ConnectFromNode.Id,
            Target = ConnectToNode.Id,
            Label = NewEdgeLabel.Trim()
        });
        NewEdgeLabel = "";
    }

    private void ExecuteAutoLayout()
    {
        for (var i = 0; i < DesignerNodes.Count; i++)
        {
            DesignerNodes[i].X = 40 + (i % 4) * 240;
            DesignerNodes[i].Y = 80 + (i / 4) * 140;
        }
        _toast.Info("Đã tự động căn chỉnh bố cục sơ đồ quy trình.");
    }

    private async Task ExecuteSaveDraftAsync()
    {
        if (CurrentVersion is null || !IsDraftVersion) return;

        IsBusy = true;
        try
        {
            var definitionObj = new
            {
                nodes = DesignerNodes.ToArray(),
                edges = DesignerEdges.ToArray(),
                updatedAt = DateTimeOffset.UtcNow.ToString("O")
            };
            var definitionJson = JsonSerializer.Serialize(definitionObj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var steps = DesignerNodes
                .Where(n => n.Type is "DEPARTMENT" or "APPROVAL" or "CONFIRMATION")
                .Select((n, idx) => new
                {
                    stepCode = $"STEP_{n.Id.Replace("-", "_").ToUpperInvariant()}",
                    name = n.Name,
                    type = n.Type == "DEPARTMENT" ? "Action" : n.Type == "APPROVAL" ? "Approval" : "Confirmation",
                    orderNo = idx + 1,
                    configJson = JsonSerializer.Serialize(new
                    {
                        nodeId = n.Id,
                        departmentId = n.DepartmentId,
                        roleId = n.RoleId,
                        slaHours = n.SlaHours,
                        assignmentMethod = n.AssignmentMethod,
                        specificUserId = n.SpecificUserId,
                        approverType = n.ApproverType,
                        approvalCountPolicy = n.ApprovalCountPolicy,
                        rejectionReasonRequired = n.RejectionReasonRequired,
                        milestone = n.Milestone,
                        description = n.Description
                    })
                }).ToArray();

            var transitions = DesignerEdges.Select(e => new
            {
                fromState = e.Source,
                toState = e.Target,
                guardJson = JsonSerializer.Serialize(new
                {
                    label = e.Label,
                    outcomeCode = e.OutcomeCode,
                    isDefault = e.IsDefault
                })
            }).ToArray();

            var updated = await _api.SaveWorkflowDraftAsync(CurrentVersion.VersionId, definitionJson, steps, transitions, EditableWorkflowName);
            CurrentVersion = updated;
            _toast.Success("Đã lưu bản nháp sơ đồ quy trình thành công!");
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi lưu bản nháp quy trình");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteValidateWorkflowAsync()
    {
        if (CurrentVersion is null) return;
        if (IsDraftVersion)
        {
            await ExecuteSaveDraftAsync();
        }

        IsBusy = true;
        ValidationMessages.Clear();
        try
        {
            var res = await _api.ValidateWorkflowVersionAsync(CurrentVersion.VersionId);
            foreach (var err in res.Errors) ValidationMessages.Add($"❌ LỖI: {err}");
            foreach (var warn in res.Warnings) ValidationMessages.Add($"⚠️ CẢNH BÁO: {warn}");

            if (res.IsValid)
            {
                ValidationMessages.Add("✅ Sơ đồ quy trình hoàn toàn hợp lệ và sẵn sàng xuất bản!");
                _toast.Success("Sơ đồ quy trình hoàn toàn hợp lệ!");
            }
            else
            {
                _toast.Warning($"Phát hiện {res.Errors.Count} lỗi cấu hình cần khắc phục trước khi xuất bản.");
            }
        }
        catch (Exception ex)
        {
            ValidationMessages.Add($"❌ Không thể kiểm tra quy trình: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecutePublishWorkflowAsync()
    {
        if (CurrentVersion is null || !IsDraftVersion) return;
        await ExecuteSaveDraftAsync();

        IsBusy = true;
        try
        {
            var published = await _api.PublishWorkflowVersionAsync(CurrentVersion.VersionId, PublishAssociateService?.Id);
            CurrentVersion = published;
            _toast.Success($"Đã xuất bản thành công phiên bản v{published.VersionNo}! Quy trình hiện đang áp dụng chính thức.");
            await LoadLibraryAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Xuất bản quy trình thất bại");
            await ExecuteValidateWorkflowAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteCreateNextVersionAsync()
    {
        if (CurrentVersion is null) return;
        IsBusy = true;
        try
        {
            var nextRow = await _api.CreateNextWorkflowVersionAsync(CurrentVersion.WorkflowId);
            _toast.Success($"Đã khởi tạo phiên bản nháp mới v{nextRow.LatestVersionNo}!");
            await OpenWorkflowDesignerAsync(nextRow);
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

    private async Task ExecuteAiAnalyzeWorkflowAsync()
    {
        if (!IsDraftVersion)
        {
            _toast.Warning("Hãy tạo hoặc mở phiên bản nháp (DRAFT) để nạp sơ đồ từ AI.");
            return;
        }
        if (string.IsNullOrWhiteSpace(AiWorkflowDocumentText))
        {
            _toast.Warning("Vui lòng nhập văn bản mô tả quy trình để AI phân tích.");
            return;
        }

        IsBusy = true;
        try
        {
            var bType = CurrentVersion?.BusinessType ?? "REQUEST";
            var graph = await _api.AnalyzeWorkflowTextAsync(AiWorkflowDocumentText, bType);
            if (graph.Nodes is { Count: > 0 })
            {
                DesignerNodes.Clear();
                foreach (var n in graph.Nodes) DesignerNodes.Add(n);
                DesignerEdges.Clear();
                if (graph.Edges is not null)
                {
                    foreach (var e in graph.Edges) DesignerEdges.Add(e);
                }
                SelectedNode = DesignerNodes.FirstOrDefault();
                _toast.Success($"✨ AI đã trích xuất {DesignerNodes.Count} bước quy trình và nạp vào Canvas!");
            }
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi phân tích tài liệu quy trình bằng AI");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
