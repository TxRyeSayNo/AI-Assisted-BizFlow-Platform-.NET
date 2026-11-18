using System.Collections.ObjectModel;
using System.IO;
using BizFlow.Desktop.Core;
using BizFlow.Desktop.Models;
using BizFlow.Desktop.Services;
using Microsoft.Win32;

namespace BizFlow.Desktop.ViewModels;

public sealed class TasksViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;
    private readonly ToastNotificationService _toast;

    // Modes: "List", "Create", "AiDecomposition", "Detail"
    private string _activeMode = "List";
    private bool _isBusy;
    private string _errorMessage = "";

    // Filters & Pagination
    private string _searchQuery = "";
    private string _selectedStatus = "";
    private string _selectedPriority = "";
    private int _currentPage = 1;
    private long _totalItems;

    // Selected Task Detail
    private TaskRowDto? _selectedTaskRow;
    private TaskDetailDto? _taskDetail;
    private string _aiEvaluationText = "";

    // Action inputs in Detail View
    private string _assignTargetType = "USER"; // USER or DEPARTMENT
    private UserRowDto? _selectedAssignUser;
    private DepartmentRowDto? _selectedAssignDepartment;
    private string _assignNote = "";
    private string _acceptNote = "";
    private string _submitResultContent = "";
    private string _confirmDecision = "CONFIRMED"; // CONFIRMED or REWORK
    private string _confirmNote = "";
    private string _newCommentText = "";

    // Create Task Form
    private string _createTitle = "";
    private string _createDescription = "";
    private string _createPriority = "MEDIUM";
    private string _createDeadlineText = DateTime.Now.AddDays(5).ToString("yyyy-MM-dd HH:mm");
    private string _createChecklistText = "Tiếp nhận và rà soát yêu cầu\nTriển khai thực hiện công việc\nKiểm tra chất lượng và nộp báo cáo";
    private Guid? _createParentTaskId;
    private string? _createParentTaskTitle;
    private Guid? _createRequestId;
    private string _aiPromptText = "";
    private string _aiDraftFeedback = "";

    // AI Decomposition Preview
    private string _decompositionPrompt = "Triển khai nâng cấp hệ thống mạng văn phòng, kiểm tra bảo mật máy chủ và đào tạo nhân sự sử dụng quy trình số hóa mới.";
    private string _decompositionSummary = "";

    public TasksViewModel(BizFlowApiClient api, SessionManager session, ToastNotificationService toast)
    {
        _api = api;
        _session = session;
        _toast = toast;

        Tasks = [];
        ChecklistItems = [];
        ProgressReports = [];
        SubmittedResults = [];
        Subtasks = [];
        Comments = [];
        Attachments = [];
        LookupUsers = [];
        LookupDepartments = [];
        DecompositionCandidates = [];

        StatusOptions = ["", "DRAFT", "ASSIGNED", "ACCEPTED", "IN_PROGRESS", "SUBMITTED", "CONFIRMED", "COMPLETED", "REJECTED", "CANCELLED", "OVERDUE"];
        PriorityOptions = ["", "LOW", "MEDIUM", "HIGH", "CRITICAL"];
        CreatePriorityOptions = ["LOW", "MEDIUM", "HIGH", "CRITICAL"];
        TargetTypeOptions = ["USER", "DEPARTMENT"];
        ConfirmDecisionOptions = ["CONFIRMED", "REWORK"];

        LoadCommand = new AsyncRelayCommand(() => LoadPageAsync(1));
        ResetFiltersCommand = new AsyncRelayCommand(async () =>
        {
            SearchQuery = "";
            SelectedStatus = "";
            SelectedPriority = "";
            await LoadPageAsync(1);
        });
        PreviousPageCommand = new AsyncRelayCommand(() => LoadPageAsync(Math.Max(1, CurrentPage - 1)), () => CurrentPage > 1 && !IsBusy);
        NextPageCommand = new AsyncRelayCommand(() => LoadPageAsync(CurrentPage + 1), () => CurrentPage * 25 < TotalItems && !IsBusy);

        OpenCreateTaskCommand = new RelayCommand(() => OpenCreateTask(null, null, null));
        OpenAiDecompositionCommand = new RelayCommand(() => ActiveMode = "AiDecomposition");
        BackToListCommand = new RelayCommand(() => ActiveMode = "List");
        InspectTaskCommand = new AsyncRelayCommand(p =>
        {
            if (p is TaskRowDto row) return OpenDetailAsync(row.TaskId);
            if (p is Guid id) return OpenDetailAsync(id);
            return Task.CompletedTask;
        });

        AssistWithAiCommand = new AsyncRelayCommand(ExecuteAiTaskAssistanceAsync, () => !IsBusy);
        BreakdownWithAiCommand = new AsyncRelayCommand(ExecuteAiTaskBreakdownAsync, () => !IsBusy);
        SubmitCreateTaskCommand = new AsyncRelayCommand(ExecuteCreateTaskAsync, () => !IsBusy);

        PreviewDecompositionCommand = new AsyncRelayCommand(ExecutePreviewDecompositionAsync, () => !IsBusy);
        ConfirmDecompositionCommand = new AsyncRelayCommand(ExecuteConfirmDecompositionAsync, () => !IsBusy);

        RecommendAssignmentCommand = new AsyncRelayCommand(ExecuteRecommendAssignmentAsync, () => !IsBusy);
        AssignTaskCommand = new AsyncRelayCommand(ExecuteAssignTaskAsync, () => !IsBusy);
        AcceptTaskCommand = new AsyncRelayCommand(ExecuteAcceptTaskAsync, () => !IsBusy);
        StartTaskCommand = new AsyncRelayCommand(ExecuteStartTaskAsync, () => !IsBusy);
        SubmitResultCommand = new AsyncRelayCommand(ExecuteSubmitResultAsync, () => !IsBusy);
        ConfirmResultCommand = new AsyncRelayCommand(ExecuteConfirmResultAsync, () => !IsBusy);
        OpenCreateSubtaskCommand = new RelayCommand(() =>
        {
            if (TaskDetail?.Task is { } t)
                OpenCreateTask(t.TaskId, t.Title, t.RequestId);
        });
        ArchiveTaskCommand = new AsyncRelayCommand(ExecuteArchiveTaskAsync, () => !IsBusy);
        EvaluateRiskCommand = new AsyncRelayCommand(ExecuteEvaluateRiskAsync, () => !IsBusy);
        SummarizeProgressCommand = new AsyncRelayCommand(ExecuteSummarizeProgressAsync, () => !IsBusy);

        AddCommentCommand = new AsyncRelayCommand(ExecuteAddCommentAsync, () => !IsBusy);
        DeleteCommentCommand = new AsyncRelayCommand(p => p is CommentItemDto c ? ExecuteDeleteCommentAsync(c.CommentId) : Task.CompletedTask);
        UploadAttachmentCommand = new AsyncRelayCommand(ExecuteUploadAttachmentAsync, () => !IsBusy);
        DownloadAttachmentCommand = new AsyncRelayCommand(p => p is AttachmentItemDto a ? ExecuteDownloadAttachmentAsync(a) : Task.CompletedTask);
        DeleteAttachmentCommand = new AsyncRelayCommand(p => p is AttachmentItemDto a ? ExecuteDeleteAttachmentAsync(a.AttachmentId) : Task.CompletedTask);

        _session.SessionChanged += (_, _) => ResetForSession();
    }

    private void ResetForSession()
    {
        ActiveMode = "List";
        ErrorMessage = "";
        SearchQuery = "";
        SelectedStatus = "";
        SelectedPriority = "";
        CurrentPage = 1;
        TotalItems = 0;
        SelectedTaskRow = null;
        TaskDetail = null;
        AiEvaluationText = "";
        AiDraftFeedback = "";
        AssignNote = "";
        AcceptNote = "";
        SubmitResultContent = "";
        ConfirmNote = "";
        NewCommentText = "";
        SelectedAssignUser = null;
        SelectedAssignDepartment = null;
        Tasks.Clear();
        ChecklistItems.Clear();
        ProgressReports.Clear();
        SubmittedResults.Clear();
        Subtasks.Clear();
        Comments.Clear();
        Attachments.Clear();
        LookupUsers.Clear();
        LookupDepartments.Clear();
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(CanCreateTask));
        RaiseDetailPermissions();
    }

    public SessionManager Session => _session;
    public ObservableCollection<TaskRowDto> Tasks { get; }
    public ObservableCollection<TaskChecklistDto> ChecklistItems { get; }
    public ObservableCollection<TaskReportDto> ProgressReports { get; }
    public ObservableCollection<TaskResultDto> SubmittedResults { get; }
    public ObservableCollection<TaskRowDto> Subtasks { get; }
    public ObservableCollection<CommentItemDto> Comments { get; }
    public ObservableCollection<AttachmentItemDto> Attachments { get; }
    public ObservableCollection<UserRowDto> LookupUsers { get; }
    public ObservableCollection<DepartmentRowDto> LookupDepartments { get; }
    public ObservableCollection<DecompositionCandidateItem> DecompositionCandidates { get; }

    public IReadOnlyList<string> StatusOptions { get; }
    public IReadOnlyList<string> PriorityOptions { get; }
    public IReadOnlyList<string> CreatePriorityOptions { get; }
    public IReadOnlyList<string> TargetTypeOptions { get; }
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
    public int CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public long TotalItems { get => _totalItems; private set => SetProperty(ref _totalItems, value); }
    public string PageSummary => $"Tổng số: {TotalItems} nhiệm vụ · Trang {CurrentPage}";

    public TaskRowDto? SelectedTaskRow
    {
        get => _selectedTaskRow;
        set
        {
            if (SetProperty(ref _selectedTaskRow, value) && value is not null)
            {
                _ = OpenDetailAsync(value.TaskId);
            }
        }
    }

    public TaskDetailDto? TaskDetail { get => _taskDetail; private set => SetProperty(ref _taskDetail, value); }
    public string AiEvaluationText { get => _aiEvaluationText; set => SetProperty(ref _aiEvaluationText, value); }

    // Role & Lifecycle visibility flags
    public bool CanCreateTask => _session.HasPermission("tasks.create");
    public bool CanAssignCurrentTask =>
        TaskDetail?.Task is { } t &&
        _session.HasPermission("tasks.assign") &&
        t.Status is "DRAFT" or "REJECTED";

    public bool CanAcceptCurrentTask =>
        TaskDetail?.Task is { } t &&
        _session.HasPermission("tasks.accept") &&
        t.Status == "ASSIGNED" &&
        (!t.AssignedUserId.HasValue || t.AssignedUserId == _session.Identity?.UserId);

    public bool CanStartCurrentTask =>
        TaskDetail?.Task is { } t &&
        _session.HasPermission("tasks.execute") &&
        t.Status is "ACCEPTED" or "OVERDUE" &&
        t.AssignedUserId == _session.Identity?.UserId;

    public bool CanSubmitCurrentTask =>
        TaskDetail?.Task is { } t &&
        _session.HasPermission("tasks.submit") &&
        t.Status == "IN_PROGRESS" &&
        t.AssignedUserId == _session.Identity?.UserId;

    public bool CanConfirmCurrentTask =>
        TaskDetail?.Task is { } t &&
        _session.HasPermission("tasks.confirm") &&
        t.Status == "SUBMITTED";

    public bool CanCreateSubtask =>
        TaskDetail?.Task is { } t &&
        _session.HasPermission("tasks.create") &&
        _session.HasPermission("tasks.assign") &&
        t.Status is not ("CANCELLED" or "COMPLETED");

    public bool CanArchiveCurrentTask =>
        TaskDetail?.Task is { } t &&
        _session.HasPermission("records.archive") &&
        t.Status is "COMPLETED" or "CANCELLED";

    // Detail action bindings
    public string AssignTargetType { get => _assignTargetType; set => SetProperty(ref _assignTargetType, value); }
    public UserRowDto? SelectedAssignUser { get => _selectedAssignUser; set => SetProperty(ref _selectedAssignUser, value); }
    public DepartmentRowDto? SelectedAssignDepartment { get => _selectedAssignDepartment; set => SetProperty(ref _selectedAssignDepartment, value); }
    public string AssignNote { get => _assignNote; set => SetProperty(ref _assignNote, value); }
    public string AcceptNote { get => _acceptNote; set => SetProperty(ref _acceptNote, value); }
    public string SubmitResultContent { get => _submitResultContent; set => SetProperty(ref _submitResultContent, value); }
    public string ConfirmDecision { get => _confirmDecision; set => SetProperty(ref _confirmDecision, value); }
    public string ConfirmNote { get => _confirmNote; set => SetProperty(ref _confirmNote, value); }
    public string NewCommentText { get => _newCommentText; set => SetProperty(ref _newCommentText, value); }

    // Create form bindings
    public string CreateTitle { get => _createTitle; set => SetProperty(ref _createTitle, value); }
    public string CreateDescription { get => _createDescription; set => SetProperty(ref _createDescription, value); }
    public string CreatePriority { get => _createPriority; set => SetProperty(ref _createPriority, value); }
    public string CreateDeadlineText { get => _createDeadlineText; set => SetProperty(ref _createDeadlineText, value); }
    public string CreateChecklistText { get => _createChecklistText; set => SetProperty(ref _createChecklistText, value); }
    public Guid? CreateParentTaskId { get => _createParentTaskId; set => SetProperty(ref _createParentTaskId, value); }
    public string? CreateParentTaskTitle { get => _createParentTaskTitle; set => SetProperty(ref _createParentTaskTitle, value); }
    public Guid? CreateRequestId { get => _createRequestId; set => SetProperty(ref _createRequestId, value); }
    public string AiPromptText { get => _aiPromptText; set => SetProperty(ref _aiPromptText, value); }
    public string AiDraftFeedback { get => _aiDraftFeedback; set => SetProperty(ref _aiDraftFeedback, value); }

    public string DecompositionPrompt { get => _decompositionPrompt; set => SetProperty(ref _decompositionPrompt, value); }
    public string DecompositionSummary { get => _decompositionSummary; set => SetProperty(ref _decompositionSummary, value); }

    // Commands
    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand ResetFiltersCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public RelayCommand OpenCreateTaskCommand { get; }
    public RelayCommand OpenAiDecompositionCommand { get; }
    public RelayCommand BackToListCommand { get; }
    public AsyncRelayCommand InspectTaskCommand { get; }

    public AsyncRelayCommand AssistWithAiCommand { get; }
    public AsyncRelayCommand BreakdownWithAiCommand { get; }
    public AsyncRelayCommand SubmitCreateTaskCommand { get; }
    public AsyncRelayCommand PreviewDecompositionCommand { get; }
    public AsyncRelayCommand ConfirmDecompositionCommand { get; }

    public AsyncRelayCommand RecommendAssignmentCommand { get; }
    public AsyncRelayCommand AssignTaskCommand { get; }
    public AsyncRelayCommand AcceptTaskCommand { get; }
    public AsyncRelayCommand StartTaskCommand { get; }
    public AsyncRelayCommand SubmitResultCommand { get; }
    public AsyncRelayCommand ConfirmResultCommand { get; }
    public RelayCommand OpenCreateSubtaskCommand { get; }
    public AsyncRelayCommand ArchiveTaskCommand { get; }
    public AsyncRelayCommand EvaluateRiskCommand { get; }
    public AsyncRelayCommand SummarizeProgressCommand { get; }

    public AsyncRelayCommand AddCommentCommand { get; }
    public AsyncRelayCommand DeleteCommentCommand { get; }
    public AsyncRelayCommand UploadAttachmentCommand { get; }
    public AsyncRelayCommand DownloadAttachmentCommand { get; }
    public AsyncRelayCommand DeleteAttachmentCommand { get; }

    public async Task LoadPageAsync(int page = 1)
    {
        if (!_session.IsSignedIn) return;
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            OnPropertyChanged(nameof(CanCreateTask));
            await LoadLookupsIfEmptyAsync();

            var res = await _api.GetTasksAsync(page, 25, SearchQuery, SelectedStatus, SelectedPriority);
            Tasks.Clear();
            foreach (var item in res.Items) Tasks.Add(item);
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

    public void OpenCreateTask(Guid? parentTaskId, string? parentTitle, Guid? requestId)
    {
        if (!CanCreateTask)
        {
            _toast.Warning("Tài khoản của bạn không có quyền tạo nhiệm vụ mới.", "Phân quyền từ chối");
            return;
        }

        CreateParentTaskId = parentTaskId;
        CreateParentTaskTitle = parentTitle;
        CreateRequestId = requestId;
        CreateTitle = parentTitle is not null ? $"Nhiệm vụ con: {parentTitle}" : "";
        CreateDescription = "";
        CreatePriority = "MEDIUM";
        CreateDeadlineText = DateTime.Now.AddDays(5).ToString("yyyy-MM-dd HH:mm");
        CreateChecklistText = "Bước 1: Tiếp nhận và chuẩn bị\nBước 2: Triển khai thực hiện\nBước 3: Nghiệm thu và bàn giao";
        AiDraftFeedback = "";
        ErrorMessage = "";
        ActiveMode = "Create";
    }

    public async Task OpenDetailAsync(Guid taskId)
    {
        IsBusy = true;
        ErrorMessage = "";
        AiEvaluationText = "";
        try
        {
            await LoadLookupsIfEmptyAsync();
            var detail = await _api.GetTaskDetailAsync(taskId);
            TaskDetail = detail;

            ChecklistItems.Clear();
            foreach (var c in detail.Checklist.Items) ChecklistItems.Add(c);

            ProgressReports.Clear();
            foreach (var r in detail.Reports.Items) ProgressReports.Add(r);

            SubmittedResults.Clear();
            foreach (var res in detail.Results.Items) SubmittedResults.Add(res);

            Subtasks.Clear();
            if (detail.Subtasks is not null)
            {
                foreach (var s in detail.Subtasks) Subtasks.Add(s);
            }

            await RefreshCollaborationAsync(taskId);

            RaiseDetailPermissions();
            ActiveMode = "Detail";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage, "Không thể tải chi tiết nhiệm vụ");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCollaborationAsync(Guid taskId)
    {
        try
        {
            var comments = await _api.GetCommentsAsync("TASK", taskId);
            Comments.Clear();
            foreach (var c in comments) Comments.Add(c);
        }
        catch { }

        try
        {
            var attachments = await _api.GetAttachmentsAsync("TASK", taskId);
            Attachments.Clear();
            foreach (var a in attachments) Attachments.Add(a);
        }
        catch { }
    }

    private async Task LoadLookupsIfEmptyAsync()
    {
        if (LookupDepartments.Count == 0)
        {
            try
            {
                var depts = await _api.GetDepartmentsAsync(1, 100);
                foreach (var d in depts.Items.Where(x => x.Status == "ACTIVE"))
                    LookupDepartments.Add(d);
                SelectedAssignDepartment = LookupDepartments.FirstOrDefault();
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
                SelectedAssignUser = LookupUsers.FirstOrDefault();
            }
            catch { }
        }
    }

    private async Task ExecuteAiTaskAssistanceAsync()
    {
        if (string.IsNullOrWhiteSpace(AiPromptText))
        {
            _toast.Warning("Vui lòng nhập yêu cầu mô tả công việc để AI tạo bản thảo.");
            return;
        }

        IsBusy = true;
        try
        {
            var draft = await _api.AssistTaskCreationAsync(AiPromptText);
            CreateTitle = draft.Title;
            CreateDescription = draft.Description;
            CreatePriority = string.IsNullOrWhiteSpace(draft.Priority) ? "MEDIUM" : draft.Priority;
            if (draft.ChecklistItems is { Count: > 0 })
            {
                CreateChecklistText = string.Join(Environment.NewLine, draft.ChecklistItems);
            }
            AiDraftFeedback = $"✨ AI đã tạo bản thảo (Độ tin cậy: {draft.Confidence * 100:F0}%): {draft.Reasoning}";
            _toast.Success("Đã áp dụng bản thảo nhiệm vụ từ Trợ lý AI!");
        }
        catch (Exception ex)
        {
            AiDraftFeedback = $"Không thể kết nối AI: {ex.Message}";
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteAiTaskBreakdownAsync()
    {
        if (string.IsNullOrWhiteSpace(CreateTitle))
        {
            _toast.Warning("Vui lòng nhập tiêu đề nhiệm vụ trước khi phân rã bằng AI.");
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _api.BreakdownTaskAsync(CreateTitle, CreateDescription, 4);
            if (res.Subtasks is { Count: > 0 })
            {
                CreateChecklistText = string.Join(Environment.NewLine, res.Subtasks.Select(s => s.Title));
                AiDraftFeedback = $"✨ AI đã gợi ý phân rã thành {res.Subtasks.Count} hạng mục kiểm tra.";
                _toast.Success(AiDraftFeedback);
            }
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

    private async Task ExecuteCreateTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(CreateTitle))
        {
            ErrorMessage = "Vui lòng nhập tiêu đề nhiệm vụ.";
            _toast.Warning(ErrorMessage);
            return;
        }

        DateTimeOffset? deadline = null;
        if (!string.IsNullOrWhiteSpace(CreateDeadlineText))
        {
            if (!DateTimeOffset.TryParse(CreateDeadlineText, out var parsed) || parsed <= DateTimeOffset.Now)
            {
                ErrorMessage = "Hạn chót phải đúng định dạng ngày giờ và nằm trong tương lai.";
                _toast.Warning(ErrorMessage);
                return;
            }
            deadline = parsed;
        }

        var checklist = CreateChecklistText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToArray();

        IsBusy = true;
        try
        {
            var created = await _api.CreateTaskAsync(
                CreateTitle,
                CreateDescription,
                CreatePriority,
                deadline,
                checklist,
                CreateRequestId,
                CreateParentTaskId);

            _toast.Success($"Đã khởi tạo nhiệm vụ '{CreateTitle}' thành công!");
            if (created.TryGetProperty("taskId", out var idProp) && Guid.TryParse(idProp.GetString(), out var newId))
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
            _toast.Error(ErrorMessage, "Tạo nhiệm vụ thất bại");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecutePreviewDecompositionAsync()
    {
        if (string.IsNullOrWhiteSpace(DecompositionPrompt))
        {
            _toast.Warning("Vui lòng nhập nội dung kế hoạch để AI phân rã.");
            return;
        }

        IsBusy = true;
        try
        {
            await LoadLookupsIfEmptyAsync();
            var preview = await _api.PreviewTaskDecompositionAsync(DecompositionPrompt);
            DecompositionSummary = preview.Summary ?? $"Đã bóc tách thành {preview.Proposals.Count} đề xuất công việc.";
            DecompositionCandidates.Clear();
            foreach (var p in preview.Proposals)
            {
                DecompositionCandidates.Add(new DecompositionCandidateItem
                {
                    IsSelected = true,
                    Title = p.Title,
                    Description = p.Description ?? p.Title,
                    Type = string.IsNullOrWhiteSpace(p.Type) ? "Assignment" : p.Type,
                    DepartmentId = p.DepartmentId ?? LookupDepartments.FirstOrDefault()?.DepartmentId,
                    DepartmentName = p.DepartmentName ?? LookupDepartments.FirstOrDefault()?.Name ?? "Phòng Kỹ thuật & CNTT",
                    Priority = string.IsNullOrWhiteSpace(p.Priority) ? "MEDIUM" : p.Priority,
                    SuggestedDurationDays = p.SuggestedDurationDays ?? 3,
                    ChecklistText = p.ChecklistItems is { Count: > 0 } ? string.Join(" | ", p.ChecklistItems) : "Chuẩn bị | Thực hiện | Nghiệm thu",
                    Reasoning = p.Reasoning ?? "Đề xuất từ AI Decomposition Engine"
                });
            }
            _toast.Success(DecompositionSummary);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi phân rã công việc");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteConfirmDecompositionAsync()
    {
        var selected = DecompositionCandidates.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _toast.Warning("Vui lòng chọn ít nhất 1 nhánh công việc để xác nhận khởi tạo.");
            return;
        }

        IsBusy = true;
        try
        {
            var itemsPayload = selected.Select(b => new
            {
                title = b.Title.Trim(),
                description = b.Description.Trim(),
                type = b.Type,
                departmentId = b.DepartmentId,
                departmentName = b.DepartmentName,
                suggestedDurationDays = b.SuggestedDurationDays,
                priority = b.Priority,
                checklistItems = b.ChecklistText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            }).ToArray();

            var res = await _api.ConfirmTaskDecompositionAsync(itemsPayload);
            _toast.Success($"Đã khởi tạo hàng loạt {res.CreatedTasksCount} nhiệm vụ và {res.CreatedRequestsCount} yêu cầu hỗ trợ!");
            ActiveMode = "List";
            await LoadPageAsync(1);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi xác nhận tạo việc hàng loạt");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteRecommendAssignmentAsync()
    {
        if (TaskDetail?.Task is not { } t) return;
        IsBusy = true;
        try
        {
            var rec = await _api.RecommendTaskAssignmentAsync(t.Title, TaskDetail.Description);
            if (rec.RecommendedUserId.HasValue)
            {
                AssignTargetType = "USER";
                SelectedAssignUser = LookupUsers.FirstOrDefault(u => u.UserId == rec.RecommendedUserId.Value) ?? SelectedAssignUser;
            }
            else if (rec.RecommendedDepartmentId.HasValue)
            {
                AssignTargetType = "DEPARTMENT";
                SelectedAssignDepartment = LookupDepartments.FirstOrDefault(d => d.DepartmentId == rec.RecommendedDepartmentId.Value) ?? SelectedAssignDepartment;
            }
            AssignNote = $"[Gợi ý AI - Độ tin cậy {rec.Confidence * 100:F0}%]: {rec.Rationale}";
            _toast.Info(AssignNote, "Gợi ý phân công AI");
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

    private async Task ExecuteAssignTaskAsync()
    {
        if (TaskDetail?.Task is not { } t) return;

        Guid? targetId = AssignTargetType == "USER"
            ? SelectedAssignUser?.UserId
            : SelectedAssignDepartment?.DepartmentId;

        if (!targetId.HasValue)
        {
            _toast.Warning("Vui lòng chọn Nhân sự hoặc Phòng ban tiếp nhận nhiệm vụ.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.AssignTaskAsync(t.TaskId, AssignTargetType, targetId.Value, AssignNote);
            _toast.Success("Đã phân công nhiệm vụ thành công!");
            await OpenDetailAsync(t.TaskId);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi phân công nhiệm vụ");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteAcceptTaskAsync()
    {
        if (TaskDetail?.Task is not { } t) return;
        IsBusy = true;
        try
        {
            await _api.AcceptTaskAsync(t.TaskId, AcceptNote);
            _toast.Success("Đã tiếp nhận nhiệm vụ!");
            await OpenDetailAsync(t.TaskId);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tiếp nhận nhiệm vụ");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteStartTaskAsync()
    {
        if (TaskDetail?.Task is not { } t) return;
        IsBusy = true;
        try
        {
            await _api.StartTaskAsync(t.TaskId);
            _toast.Success("Đã bắt đầu thực thi nhiệm vụ (IN_PROGRESS)!");
            await OpenDetailAsync(t.TaskId);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi bắt đầu nhiệm vụ");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteSubmitResultAsync()
    {
        if (TaskDetail?.Task is not { } t) return;
        if (string.IsNullOrWhiteSpace(SubmitResultContent))
        {
            _toast.Warning("Vui lòng nhập nội dung báo cáo kết quả bàn giao.");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.SubmitTaskResultAsync(t.TaskId, SubmitResultContent);
            SubmitResultContent = "";
            _toast.Success("Đã nộp kết quả công việc để cấp quản lý nghiệm thu!");
            await OpenDetailAsync(t.TaskId);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi nộp kết quả");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteConfirmResultAsync()
    {
        if (TaskDetail?.Task is not { } t) return;
        if (ConfirmDecision == "REWORK" && string.IsNullOrWhiteSpace(ConfirmNote))
        {
            _toast.Warning("Vui lòng nhập ghi chú lý do yêu cầu làm lại (REWORK).");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.ConfirmTaskResultAsync(t.TaskId, ConfirmDecision, ConfirmNote);
            ConfirmNote = "";
            _toast.Success(ConfirmDecision == "CONFIRMED"
                ? "Đã nghiệm thu và xác nhận hoàn thành nhiệm vụ!"
                : "Đã gửi yêu cầu chỉnh sửa/làm lại cho nhân sự thực hiện.");
            await OpenDetailAsync(t.TaskId);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi nghiệm thu nhiệm vụ");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteArchiveTaskAsync()
    {
        if (TaskDetail?.Task is not { } t) return;
        IsBusy = true;
        try
        {
            await _api.ArchiveRecordAsync("task", t.TaskId);
            _toast.Success($"Đã lưu trữ nhiệm vụ '{t.Title}' vào hồ sơ lịch sử.");
            ActiveMode = "List";
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi lưu trữ nhiệm vụ");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteEvaluateRiskAsync()
    {
        if (TaskDetail?.Task is not { } t) return;
        IsBusy = true;
        try
        {
            var risk = await _api.EvaluateTaskRiskAsync(t.TaskId);
            var factors = risk.RiskFactors is { Count: > 0 } ? string.Join("; ", risk.RiskFactors) : "Không phát hiện yếu tố bất thường";
            var mitigations = risk.Mitigations is { Count: > 0 } ? string.Join("; ", risk.Mitigations) : "Duy trì tiến độ hiện tại";
            AiEvaluationText = $"🛡️ ĐÁNH GIÁ RỦI RO AI [{risk.RiskLevel} - Điểm: {risk.RiskScore:F2}]:\n• Tóm tắt: {risk.Summary}\n• Yếu tố rủi ro: {factors}\n• Khuyến nghị giảm thiểu: {mitigations}";
        }
        catch (Exception ex)
        {
            AiEvaluationText = $"Không thể đánh giá rủi ro: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteSummarizeProgressAsync()
    {
        if (TaskDetail?.Task is not { } t) return;
        IsBusy = true;
        try
        {
            var sum = await _api.SummarizeTaskProgressAsync(t.TaskId);
            var highlights = sum.KeyHighlights is { Count: > 0 } ? string.Join("; ", sum.KeyHighlights) : "Đang cập nhật";
            var next = sum.NextSteps is { Count: > 0 } ? string.Join("; ", sum.NextSteps) : "Tiếp tục theo dõi";
            AiEvaluationText = $"📊 TÓM TẮT TIẾN ĐỘ AI (Ước tính hoàn thành: {sum.CompletionEstimatePercent}%):\n• Tổng quan: {sum.ExecutiveSummary}\n• Điểm nhấn: {highlights}\n• Bước tiếp theo: {next}";
        }
        catch (Exception ex)
        {
            AiEvaluationText = $"Không thể tóm tắt tiến độ: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteAddCommentAsync()
    {
        if (TaskDetail?.Task is not { } t || string.IsNullOrWhiteSpace(NewCommentText)) return;
        IsBusy = true;
        try
        {
            await _api.AddCommentAsync("TASK", t.TaskId, NewCommentText);
            NewCommentText = "";
            await RefreshCollaborationAsync(t.TaskId);
            _toast.Success("Đã gửi bình luận trao đổi!");
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

    private async Task ExecuteDeleteCommentAsync(Guid commentId)
    {
        if (TaskDetail?.Task is not { } t) return;
        IsBusy = true;
        try
        {
            await _api.DeleteCommentAsync(commentId);
            await RefreshCollaborationAsync(t.TaskId);
            _toast.Info("Đã xóa bình luận.");
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
        if (TaskDetail?.Task is not { } t) return;
        var dlg = new OpenFileDialog
        {
            Title = "Chọn tệp đính kèm cho Nhiệm vụ"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var uploaded = await _api.UploadAttachmentAsync("TASK", t.TaskId, dlg.FileName);
            await RefreshCollaborationAsync(t.TaskId);
            _toast.Success($"Đã đính kèm tệp '{uploaded.FileName}'!");
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tải lên tệp đính kèm");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteDownloadAttachmentAsync(AttachmentItemDto attachment)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Lưu tệp đính kèm",
            FileName = attachment.FileName
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            await _api.DownloadAttachmentAsync(attachment.AttachmentId, dlg.FileName);
            _toast.Success($"Đã tải tệp '{Path.GetFileName(dlg.FileName)}' thành công!");
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tải xuống tệp");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteDeleteAttachmentAsync(Guid attachmentId)
    {
        if (TaskDetail?.Task is not { } t) return;
        IsBusy = true;
        try
        {
            await _api.DeleteAttachmentAsync(attachmentId);
            await RefreshCollaborationAsync(t.TaskId);
            _toast.Info("Đã xóa tệp đính kèm.");
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
        OnPropertyChanged(nameof(CanAssignCurrentTask));
        OnPropertyChanged(nameof(CanAcceptCurrentTask));
        OnPropertyChanged(nameof(CanStartCurrentTask));
        OnPropertyChanged(nameof(CanSubmitCurrentTask));
        OnPropertyChanged(nameof(CanConfirmCurrentTask));
        OnPropertyChanged(nameof(CanCreateSubtask));
        OnPropertyChanged(nameof(CanArchiveCurrentTask));
    }
}
