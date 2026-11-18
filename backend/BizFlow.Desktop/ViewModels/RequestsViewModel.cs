using System.Collections.ObjectModel;
using System.IO;
using BizFlow.Desktop.Core;
using BizFlow.Desktop.Models;
using BizFlow.Desktop.Services;
using Microsoft.Win32;

namespace BizFlow.Desktop.ViewModels;

public sealed class RequestsViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;
    private readonly ToastNotificationService _toast;

    // Modes: "List", "Create", "Detail"
    private string _activeMode = "List";
    private bool _isBusy;
    private string _errorMessage = "";

    // Filters
    private string _searchQuery = "";
    private string _selectedStatus = "";
    private string _selectedPriority = "";
    private ServiceRowDto? _filterService;
    private int _currentPage = 1;
    private long _totalItems;

    // Create Request Form
    private ServiceRowDto? _selectedCreateService;
    private ServiceCategoryRowDto? _selectedCreateCategory;
    private string _createTitle = "";
    private string _createDescription = "";
    private string _createPriority = "MEDIUM";
    private bool _submitImmediately = true;

    // Detail View State
    private RequestRowDto? _selectedRequestRow;
    private RequestDetailDto? _requestDetail;

    // Detail Action Inputs
    private DepartmentRowDto? _routeDepartment;
    private UserRowDto? _routeUser;
    private string _routeReason = "";
    private string _receiveNote = "";
    private string _resolveContent = "";
    private string _confirmDecision = "CONFIRMED"; // CONFIRMED or REWORK
    private string _confirmNote = "";
    private string _rejectReason = "";
    private string _reviseTitle = "";
    private string _reviseDescription = "";
    private string _linkedTaskTitle = "";
    private string _linkedTaskDescription = "";
    private string _newCommentText = "";

    public RequestsViewModel(BizFlowApiClient api, SessionManager session, ToastNotificationService toast)
    {
        _api = api;
        _session = session;
        _toast = toast;

        Requests = [];
        AvailableServices = [];
        AvailableCategories = [];
        Resolutions = [];
        Confirmations = [];
        LinkedTasks = [];
        Comments = [];
        Attachments = [];
        LookupDepartments = [];
        LookupUsers = [];

        StatusOptions = ["", "DRAFT", "SUBMITTED", "ROUTED", "RECEIVED", "IN_PROGRESS", "WAITING_FOR_INFORMATION", "RESOLVED", "CONFIRMED", "CLOSED", "REJECTED", "CANCELLED", "OVERDUE"];
        PriorityOptions = ["", "LOW", "MEDIUM", "HIGH", "CRITICAL"];
        CreatePriorityOptions = ["LOW", "MEDIUM", "HIGH", "CRITICAL"];
        ConfirmDecisionOptions = ["CONFIRMED", "REWORK"];

        LoadCommand = new AsyncRelayCommand(() => LoadPageAsync(1));
        ResetFiltersCommand = new AsyncRelayCommand(async () =>
        {
            SearchQuery = "";
            SelectedStatus = "";
            SelectedPriority = "";
            FilterService = null;
            await LoadPageAsync(1);
        });
        PreviousPageCommand = new AsyncRelayCommand(() => LoadPageAsync(Math.Max(1, CurrentPage - 1)), () => CurrentPage > 1 && !IsBusy);
        NextPageCommand = new AsyncRelayCommand(() => LoadPageAsync(CurrentPage + 1), () => CurrentPage * 25 < TotalItems && !IsBusy);

        OpenCreateRequestCommand = new AsyncRelayCommand(OpenCreateRequestAsync);
        BackToListCommand = new RelayCommand(() => ActiveMode = "List");
        InspectRequestCommand = new AsyncRelayCommand(p =>
        {
            if (p is RequestRowDto row) return OpenDetailAsync(row.RequestId);
            if (p is Guid id) return OpenDetailAsync(id);
            return Task.CompletedTask;
        });

        SubmitCreateRequestCommand = new AsyncRelayCommand(ExecuteCreateRequestAsync, () => !IsBusy);

        SubmitDraftRequestCommand = new AsyncRelayCommand(ExecuteSubmitDraftAsync, () => !IsBusy);
        RecommendRoutingCommand = new AsyncRelayCommand(ExecuteRecommendRoutingAsync, () => !IsBusy);
        RouteRequestCommand = new AsyncRelayCommand(ExecuteRouteRequestAsync, () => !IsBusy);
        ReceiveRequestCommand = new AsyncRelayCommand(ExecuteReceiveRequestAsync, () => !IsBusy);
        StartProcessingCommand = new AsyncRelayCommand(ExecuteStartProcessingAsync, () => !IsBusy);
        ResolveRequestCommand = new AsyncRelayCommand(ExecuteResolveRequestAsync, () => !IsBusy);
        ConfirmRequestCommand = new AsyncRelayCommand(ExecuteConfirmRequestAsync, () => !IsBusy);
        RejectRequestCommand = new AsyncRelayCommand(ExecuteRejectRequestAsync, () => !IsBusy);
        CancelRequestCommand = new AsyncRelayCommand(ExecuteCancelRequestAsync, () => !IsBusy);
        ReviseRequestCommand = new AsyncRelayCommand(ExecuteReviseRequestAsync, () => !IsBusy);
        CreateLinkedTaskCommand = new AsyncRelayCommand(ExecuteCreateLinkedTaskAsync, () => !IsBusy);
        ArchiveRequestCommand = new AsyncRelayCommand(ExecuteArchiveRequestAsync, () => !IsBusy);

        AddCommentCommand = new AsyncRelayCommand(ExecuteAddCommentAsync, () => !IsBusy);
        UploadAttachmentCommand = new AsyncRelayCommand(ExecuteUploadAttachmentAsync, () => !IsBusy);
        DownloadAttachmentCommand = new AsyncRelayCommand(p => p is AttachmentItemDto a ? ExecuteDownloadAttachmentAsync(a) : Task.CompletedTask);
        ClearRouteUserCommand = new RelayCommand(() => RouteUser = null);

        _session.SessionChanged += (_, _) => ResetForSession();
    }

    private void ResetForSession()
    {
        ActiveMode = "List";
        ErrorMessage = "";
        SearchQuery = "";
        SelectedStatus = "";
        SelectedPriority = "";
        FilterService = null;
        CurrentPage = 1;
        TotalItems = 0;
        SelectedRequestRow = null;
        RequestDetail = null;
        RouteDepartment = null;
        RouteUser = null;
        RouteReason = "";
        ReceiveNote = "";
        ResolveContent = "";
        ConfirmNote = "";
        RejectReason = "";
        ReviseTitle = "";
        ReviseDescription = "";
        LinkedTaskTitle = "";
        LinkedTaskDescription = "";
        NewCommentText = "";
        Requests.Clear();
        AvailableServices.Clear();
        AvailableCategories.Clear();
        Resolutions.Clear();
        Confirmations.Clear();
        LinkedTasks.Clear();
        Comments.Clear();
        Attachments.Clear();
        LookupDepartments.Clear();
        LookupUsers.Clear();
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(CanCreateRequest));
        RaiseDetailPermissions();
    }

    public SessionManager Session => _session;
    public ObservableCollection<RequestRowDto> Requests { get; }
    public ObservableCollection<ServiceRowDto> AvailableServices { get; }
    public ObservableCollection<ServiceCategoryRowDto> AvailableCategories { get; }
    public ObservableCollection<RequestResolutionItemDto> Resolutions { get; }
    public ObservableCollection<RequestConfirmationItemDto> Confirmations { get; }
    public ObservableCollection<TaskRowDto> LinkedTasks { get; }
    public ObservableCollection<CommentItemDto> Comments { get; }
    public ObservableCollection<AttachmentItemDto> Attachments { get; }
    public ObservableCollection<DepartmentRowDto> LookupDepartments { get; }
    public ObservableCollection<UserRowDto> LookupUsers { get; }

    public IReadOnlyList<string> StatusOptions { get; }
    public IReadOnlyList<string> PriorityOptions { get; }
    public IReadOnlyList<string> CreatePriorityOptions { get; }
    public IReadOnlyList<string> ConfirmDecisionOptions { get; }

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

    public string SearchQuery { get => _searchQuery; set => SetProperty(ref _searchQuery, value); }
    public string SelectedStatus { get => _selectedStatus; set => SetProperty(ref _selectedStatus, value); }
    public string SelectedPriority { get => _selectedPriority; set => SetProperty(ref _selectedPriority, value); }
    public ServiceRowDto? FilterService { get => _filterService; set => SetProperty(ref _filterService, value); }
    public int CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public long TotalItems { get => _totalItems; private set => SetProperty(ref _totalItems, value); }
    public string PageSummary => $"Tổng số: {TotalItems} yêu cầu dịch vụ · Trang {CurrentPage}";

    public ServiceRowDto? SelectedCreateService
    {
        get => _selectedCreateService;
        set
        {
            if (SetProperty(ref _selectedCreateService, value))
            {
                AvailableCategories.Clear();
                if (value?.Categories is not null)
                {
                    foreach (var cat in value.Categories.Where(c => c.Status == "ACTIVE"))
                        AvailableCategories.Add(cat);
                }
                SelectedCreateCategory = AvailableCategories.FirstOrDefault();
            }
        }
    }

    public ServiceCategoryRowDto? SelectedCreateCategory { get => _selectedCreateCategory; set => SetProperty(ref _selectedCreateCategory, value); }
    public string CreateTitle { get => _createTitle; set => SetProperty(ref _createTitle, value); }
    public string CreateDescription { get => _createDescription; set => SetProperty(ref _createDescription, value); }
    public string CreatePriority { get => _createPriority; set => SetProperty(ref _createPriority, value); }
    public bool SubmitImmediately { get => _submitImmediately; set => SetProperty(ref _submitImmediately, value); }

    public RequestRowDto? SelectedRequestRow
    {
        get => _selectedRequestRow;
        set
        {
            if (SetProperty(ref _selectedRequestRow, value) && value is not null)
            {
                _ = OpenDetailAsync(value.RequestId);
            }
        }
    }

    public RequestDetailDto? RequestDetail { get => _requestDetail; private set => SetProperty(ref _requestDetail, value); }

    // Lifecycle RBAC flags aligned strictly with RequestLifecyclePolicy & Application Commands
    public bool CanCreateRequest => _session.HasPermission("requests.create");
    public bool CanSubmitDraft => RequestDetail is { Status: "DRAFT" } r && r.RequesterId == _session.Identity?.UserId && _session.HasPermission("requests.submit");
    public bool CanRouteCurrent => RequestDetail is { Status: "SUBMITTED" } && _session.HasPermission("requests.route");
    public bool CanReceiveCurrent =>
        RequestDetail is { Status: "ROUTED" } r &&
        _session.HasPermission("requests.receive") &&
        (!r.CurrentRoutingUserId.HasValue || r.CurrentRoutingUserId == _session.Identity?.UserId);
    public bool CanStartCurrent =>
        RequestDetail is { Status: "RECEIVED" or "OVERDUE" or "WAITING_FOR_INFORMATION" } r &&
        _session.HasPermission("requests.process") &&
        (!r.CurrentRoutingUserId.HasValue || r.CurrentRoutingUserId == _session.Identity?.UserId);
    public bool CanResolveCurrent =>
        RequestDetail is { Status: "IN_PROGRESS" } r &&
        _session.HasPermission("requests.resolve") &&
        (!r.CurrentRoutingUserId.HasValue || r.CurrentRoutingUserId == _session.Identity?.UserId || _session.IsCompanyAdmin);
    public bool CanConfirmCurrent => RequestDetail is { Status: "RESOLVED" } r && r.RequesterId == _session.Identity?.UserId && _session.HasPermission("requests.confirm");
    public bool CanRejectCurrent => RequestDetail is { Status: "SUBMITTED" or "ROUTED" } && _session.HasPermission("requests.reject");
    public bool CanCancelCurrent => RequestDetail is { Status: "DRAFT" or "SUBMITTED" or "ROUTED" or "RECEIVED" } r && (r.RequesterId == _session.Identity?.UserId || _session.IsCompanyAdmin) && _session.HasPermission("requests.cancel");
    public bool CanReviseCurrent => RequestDetail is { Status: "REJECTED" } r && (r.RequesterId == _session.Identity?.UserId || _session.IsCompanyAdmin) && _session.HasPermission("requests.create");
    public bool CanArchiveCurrent => RequestDetail is { Status: "CLOSED" or "CANCELLED" } && _session.HasPermission("records.archive");
    public bool CanCreateLinkedTask => RequestDetail is { Status: not ("CLOSED" or "CANCELLED" or "REJECTED" or "DRAFT") } && _session.HasPermission("tasks.create");

    public DepartmentRowDto? RouteDepartment { get => _routeDepartment; set => SetProperty(ref _routeDepartment, value); }
    public UserRowDto? RouteUser { get => _routeUser; set => SetProperty(ref _routeUser, value); }
    public string RouteReason { get => _routeReason; set => SetProperty(ref _routeReason, value); }
    public string ReceiveNote { get => _receiveNote; set => SetProperty(ref _receiveNote, value); }
    public string ResolveContent { get => _resolveContent; set => SetProperty(ref _resolveContent, value); }
    public string ConfirmDecision { get => _confirmDecision; set => SetProperty(ref _confirmDecision, value); }
    public string ConfirmNote { get => _confirmNote; set => SetProperty(ref _confirmNote, value); }
    public string RejectReason { get => _rejectReason; set => SetProperty(ref _rejectReason, value); }
    public string ReviseTitle { get => _reviseTitle; set => SetProperty(ref _reviseTitle, value); }
    public string ReviseDescription { get => _reviseDescription; set => SetProperty(ref _reviseDescription, value); }
    public string LinkedTaskTitle { get => _linkedTaskTitle; set => SetProperty(ref _linkedTaskTitle, value); }
    public string LinkedTaskDescription { get => _linkedTaskDescription; set => SetProperty(ref _linkedTaskDescription, value); }
    public string NewCommentText { get => _newCommentText; set => SetProperty(ref _newCommentText, value); }

    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand ResetFiltersCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public AsyncRelayCommand OpenCreateRequestCommand { get; }
    public RelayCommand BackToListCommand { get; }
    public AsyncRelayCommand InspectRequestCommand { get; }
    public AsyncRelayCommand SubmitCreateRequestCommand { get; }

    public AsyncRelayCommand SubmitDraftRequestCommand { get; }
    public AsyncRelayCommand RecommendRoutingCommand { get; }
    public AsyncRelayCommand RouteRequestCommand { get; }
    public RelayCommand ClearRouteUserCommand { get; }
    public AsyncRelayCommand ReceiveRequestCommand { get; }
    public AsyncRelayCommand StartProcessingCommand { get; }
    public AsyncRelayCommand ResolveRequestCommand { get; }
    public AsyncRelayCommand ConfirmRequestCommand { get; }
    public AsyncRelayCommand RejectRequestCommand { get; }
    public AsyncRelayCommand CancelRequestCommand { get; }
    public AsyncRelayCommand ReviseRequestCommand { get; }
    public AsyncRelayCommand CreateLinkedTaskCommand { get; }
    public AsyncRelayCommand ArchiveRequestCommand { get; }

    public AsyncRelayCommand AddCommentCommand { get; }
    public AsyncRelayCommand UploadAttachmentCommand { get; }
    public AsyncRelayCommand DownloadAttachmentCommand { get; }

    public async Task LoadPageAsync(int page = 1)
    {
        if (!_session.IsSignedIn) return;
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            OnPropertyChanged(nameof(CanCreateRequest));
            await LoadCatalogAndLookupsAsync();

            var res = await _api.GetRequestsAsync(page, 25, SearchQuery, SelectedStatus, SelectedPriority, FilterService?.ServiceId);
            Requests.Clear();
            foreach (var r in res.Items) Requests.Add(r);
            CurrentPage = res.Page;
            TotalItems = res.Total;
            OnPropertyChanged(nameof(PageSummary));
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

    public async Task OpenCreateRequestAsync()
    {
        if (!CanCreateRequest)
        {
            _toast.Warning("Tài khoản của bạn không có quyền tạo yêu cầu dịch vụ.");
            return;
        }

        await LoadCatalogAndLookupsAsync();
        CreateTitle = "";
        CreateDescription = "";
        CreatePriority = "MEDIUM";
        SubmitImmediately = true;
        ErrorMessage = "";
        ActiveMode = "Create";
    }

    public async Task OpenDetailAsync(Guid requestId)
    {
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            await LoadCatalogAndLookupsAsync();
            var detail = await _api.GetRequestDetailAsync(requestId);
            RequestDetail = detail;

            ReviseTitle = $"Bản hiệu chỉnh: {detail.Title}";
            ReviseDescription = detail.Description ?? "";
            LinkedTaskTitle = $"Xử lý yêu cầu: {detail.Title}";
            LinkedTaskDescription = detail.Description ?? "";

            Resolutions.Clear();
            if (detail.Resolutions is not null)
            {
                foreach (var r in detail.Resolutions) Resolutions.Add(r);
            }

            Confirmations.Clear();
            if (detail.Confirmations is not null)
            {
                foreach (var c in detail.Confirmations) Confirmations.Add(c);
            }

            LinkedTasks.Clear();
            if (detail.Tasks is not null)
            {
                foreach (var t in detail.Tasks) LinkedTasks.Add(t);
            }

            await RefreshCollaborationAsync(requestId);
            RaiseDetailPermissions();
            ActiveMode = "Detail";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage, "Không thể tải chi tiết yêu cầu");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCatalogAndLookupsAsync()
    {
        if (AvailableServices.Count == 0)
        {
            try
            {
                var svcs = await _api.GetServicesAsync(1, 100);
                foreach (var s in svcs.Items.Where(x => x.Status == "ACTIVE"))
                    AvailableServices.Add(s);
                SelectedCreateService = AvailableServices.FirstOrDefault();
            }
            catch { }
        }

        if (LookupDepartments.Count == 0)
        {
            try
            {
                var depts = await _api.GetDepartmentsAsync(1, 100);
                foreach (var d in depts.Items.Where(x => x.Status == "ACTIVE"))
                    LookupDepartments.Add(d);
                RouteDepartment = LookupDepartments.FirstOrDefault();
            }
            catch { }
        }

        if (LookupUsers.Count == 0)
        {
            try
            {
                var users = await _api.GetUsersAsync(1, 100);
                foreach (var u in users.Items.Where(x => x.Status == "ACTIVE"))
                    LookupUsers.Add(u);
            }
            catch { }
        }
    }

    private async Task RefreshCollaborationAsync(Guid requestId)
    {
        try
        {
            var comments = await _api.GetCommentsAsync("REQUEST", requestId);
            Comments.Clear();
            foreach (var c in comments) Comments.Add(c);
        }
        catch { }

        try
        {
            var attachments = await _api.GetAttachmentsAsync("REQUEST", requestId);
            Attachments.Clear();
            foreach (var a in attachments) Attachments.Add(a);
        }
        catch { }
    }

    private async Task ExecuteCreateRequestAsync()
    {
        if (SelectedCreateService is null || SelectedCreateCategory is null)
        {
            ErrorMessage = "Vui lòng chọn Dịch vụ nội bộ và Phân loại dịch vụ.";
            _toast.Warning(ErrorMessage);
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateTitle))
        {
            ErrorMessage = "Vui lòng nhập tiêu đề yêu cầu dịch vụ.";
            _toast.Warning(ErrorMessage);
            return;
        }

        IsBusy = true;
        try
        {
            var created = await _api.CreateRequestAsync(
                SelectedCreateService.ServiceId,
                SelectedCreateCategory.ServiceCategoryId,
                CreateTitle,
                CreateDescription,
                CreatePriority,
                SubmitImmediately);

            _toast.Success($"Đã khởi tạo yêu cầu '{CreateTitle}' thành công!");
            if (created.TryGetProperty("requestId", out var idProp) && Guid.TryParse(idProp.GetString(), out var newId))
            {
                await LoadPageAsync(1);
                await OpenDetailAsync(newId);
            }
            else
            {
                ActiveMode = "List";
                await LoadPageAsync(1);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage, "Lỗi tạo yêu cầu dịch vụ");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteSubmitDraftAsync()
    {
        if (RequestDetail is not { } d) return;
        IsBusy = true;
        try
        {
            await _api.SubmitRequestAsync(d.RequestId);
            _toast.Success("Đã gửi yêu cầu dịch vụ thành công!");
            await OpenDetailAsync(d.RequestId);
            await LoadPageAsync(CurrentPage);
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

    private async Task ExecuteRecommendRoutingAsync()
    {
        if (RequestDetail is not { } d) return;
        IsBusy = true;
        try
        {
            var rec = await _api.RecommendRequestRoutingAsync(d.RequestId, d.Title, d.Description, d.ServiceId);
            if (rec.TryGetProperty("recommendedDepartmentId", out var deptProp) && Guid.TryParse(deptProp.GetString(), out var deptId))
            {
                RouteDepartment = LookupDepartments.FirstOrDefault(x => x.DepartmentId == deptId) ?? RouteDepartment;
            }
            if (rec.TryGetProperty("rationale", out var ratProp))
            {
                RouteReason = ratProp.GetString() ?? "Điều phối theo gợi ý AI";
            }
            _toast.Info(RouteReason, "Gợi ý điều phối AI");
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

    private async Task ExecuteRouteRequestAsync()
    {
        if (RequestDetail is not { } d) return;
        if (RouteDepartment is null && RouteUser is null)
        {
            _toast.Warning("Vui lòng chọn Phòng ban hoặc Nhân sự tiếp nhận điều phối.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.RouteRequestAsync(d.RequestId, RouteDepartment?.DepartmentId, RouteUser?.UserId, RouteReason);
            _toast.Success("Đã điều phối yêu cầu dịch vụ thành công!");
            await OpenDetailAsync(d.RequestId);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi điều phối yêu cầu");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteReceiveRequestAsync()
    {
        if (RequestDetail is not { } d) return;
        IsBusy = true;
        try
        {
            await _api.ReceiveRequestAsync(d.RequestId, ReceiveNote);
            _toast.Success("Đã tiếp nhận yêu cầu vào hàng đợi xử lý!");
            await OpenDetailAsync(d.RequestId);
            await LoadPageAsync(CurrentPage);
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

    private async Task ExecuteStartProcessingAsync()
    {
        if (RequestDetail is not { } d) return;
        IsBusy = true;
        try
        {
            await _api.StartRequestProcessingAsync(d.RequestId);
            _toast.Success("Đã chuyển yêu cầu sang trạng thái Đang xử lý (IN_PROGRESS)!");
            await OpenDetailAsync(d.RequestId);
            await LoadPageAsync(CurrentPage);
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

    private async Task ExecuteResolveRequestAsync()
    {
        if (RequestDetail is not { } d) return;
        if (string.IsNullOrWhiteSpace(ResolveContent))
        {
            _toast.Warning("Vui lòng nhập nội dung giải quyết yêu cầu.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.ResolveRequestAsync(d.RequestId, ResolveContent);
            ResolveContent = "";
            _toast.Success("Đã hoàn tất giải quyết yêu cầu (RESOLVED)!");
            await OpenDetailAsync(d.RequestId);
            await LoadPageAsync(CurrentPage);
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

    private async Task ExecuteConfirmRequestAsync()
    {
        if (RequestDetail is not { } d) return;
        if (ConfirmDecision == "REWORK" && string.IsNullOrWhiteSpace(ConfirmNote))
        {
            _toast.Warning("Vui lòng nhập lý do yêu cầu xử lý lại (REWORK).");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.ConfirmRequestAsync(d.RequestId, ConfirmDecision, ConfirmNote);
            ConfirmNote = "";
            _toast.Success(ConfirmDecision == "CONFIRMED"
                ? "Đã xác nhận kết quả giải quyết và đóng yêu cầu!"
                : "Đã gửi yêu cầu xử lý lại (REWORK)!");
            await OpenDetailAsync(d.RequestId);
            await LoadPageAsync(CurrentPage);
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

    private async Task ExecuteRejectRequestAsync()
    {
        if (RequestDetail is not { } d) return;
        if (string.IsNullOrWhiteSpace(RejectReason))
        {
            _toast.Warning("Vui lòng nhập lý do từ chối yêu cầu.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.RejectRequestAsync(d.RequestId, RejectReason);
            RejectReason = "";
            _toast.Info("Yêu cầu dịch vụ đã bị từ chối.");
            await OpenDetailAsync(d.RequestId);
            await LoadPageAsync(CurrentPage);
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

    private async Task ExecuteCancelRequestAsync()
    {
        if (RequestDetail is not { } d) return;
        IsBusy = true;
        try
        {
            await _api.CancelRequestAsync(d.RequestId);
            _toast.Info("Đã hủy yêu cầu dịch vụ.");
            await OpenDetailAsync(d.RequestId);
            await LoadPageAsync(CurrentPage);
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

    private async Task ExecuteReviseRequestAsync()
    {
        if (RequestDetail is not { } d) return;
        if (string.IsNullOrWhiteSpace(ReviseTitle))
        {
            _toast.Warning("Vui lòng nhập tiêu đề cho yêu cầu hiệu chỉnh.");
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _api.ReviseRequestAsync(d.RequestId, ReviseTitle, ReviseDescription, d.Priority, true);
            _toast.Success("Đã tạo bản hiệu chỉnh yêu cầu thành công!");
            if (res.TryGetProperty("requestId", out var idProp) && Guid.TryParse(idProp.GetString(), out var newId))
            {
                await OpenDetailAsync(newId);
            }
            await LoadPageAsync(1);
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

    private async Task ExecuteCreateLinkedTaskAsync()
    {
        if (RequestDetail is not { } d) return;
        if (string.IsNullOrWhiteSpace(LinkedTaskTitle))
        {
            _toast.Warning("Vui lòng nhập tiêu đề nhiệm vụ liên kết.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.CreateTaskForRequestAsync(d.RequestId, LinkedTaskTitle, LinkedTaskDescription, d.Priority, DateTimeOffset.Now.AddDays(3), ["Tiếp nhận từ yêu cầu dịch vụ", "Thực hiện xử lý", "Báo cáo hoàn tất"]);
            _toast.Success($"Đã khởi tạo nhiệm vụ liên kết '{LinkedTaskTitle}'!");
            await OpenDetailAsync(d.RequestId);
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

    private async Task ExecuteArchiveRequestAsync()
    {
        if (RequestDetail is not { } d) return;
        IsBusy = true;
        try
        {
            await _api.ArchiveRecordAsync("request", d.RequestId);
            _toast.Success($"Đã lưu trữ yêu cầu '{d.Title}'.");
            ActiveMode = "List";
            await LoadPageAsync(CurrentPage);
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

    private async Task ExecuteAddCommentAsync()
    {
        if (RequestDetail is not { } d || string.IsNullOrWhiteSpace(NewCommentText)) return;
        IsBusy = true;
        try
        {
            await _api.AddCommentAsync("REQUEST", d.RequestId, NewCommentText);
            NewCommentText = "";
            await RefreshCollaborationAsync(d.RequestId);
            _toast.Success("Đã gửi bình luận vào yêu cầu!");
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

    private async Task ExecuteUploadAttachmentAsync()
    {
        if (RequestDetail is not { } d) return;
        var dlg = new OpenFileDialog { Title = "Chọn tệp đính kèm cho Yêu cầu dịch vụ" };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var uploaded = await _api.UploadAttachmentAsync("REQUEST", d.RequestId, dlg.FileName);
            await RefreshCollaborationAsync(d.RequestId);
            _toast.Success($"Đã tải lên tệp '{uploaded.FileName}'!");
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

    private async Task ExecuteDownloadAttachmentAsync(AttachmentItemDto attachment)
    {
        var dlg = new SaveFileDialog { Title = "Lưu tệp đính kèm", FileName = attachment.FileName };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            await _api.DownloadAttachmentAsync(attachment.AttachmentId, dlg.FileName);
            _toast.Success($"Đã tải xuống '{Path.GetFileName(dlg.FileName)}'!");
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

    private void RaiseDetailPermissions()
    {
        OnPropertyChanged(nameof(CanSubmitDraft));
        OnPropertyChanged(nameof(CanRouteCurrent));
        OnPropertyChanged(nameof(CanReceiveCurrent));
        OnPropertyChanged(nameof(CanStartCurrent));
        OnPropertyChanged(nameof(CanResolveCurrent));
        OnPropertyChanged(nameof(CanConfirmCurrent));
        OnPropertyChanged(nameof(CanRejectCurrent));
        OnPropertyChanged(nameof(CanCancelCurrent));
        OnPropertyChanged(nameof(CanReviseCurrent));
        OnPropertyChanged(nameof(CanArchiveCurrent));
        OnPropertyChanged(nameof(CanCreateLinkedTask));
    }
}
