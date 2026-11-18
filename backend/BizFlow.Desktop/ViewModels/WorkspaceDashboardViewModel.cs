using System.Collections.ObjectModel;
using BizFlow.Desktop.Core;
using BizFlow.Desktop.Models;
using BizFlow.Desktop.Services;

namespace BizFlow.Desktop.ViewModels;

public sealed class WorkspaceDashboardViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;
    private readonly Action<string> _navigateTo;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private bool _isLoading;
    private int _tasksActiveCount;
    private int _tasksTotalCount;
    private int _requestsPendingCount;
    private int _requestsTotalCount;
    private int _auditCount;
    private int _unreadNotificationsCount;
    private int _totalNotificationsCount;
    private string _slaRate = "100%";
    private string _slaStatus = "Tối ưu";

    public WorkspaceDashboardViewModel(
        BizFlowApiClient api,
        SessionManager session,
        Action<string> navigateTo)
    {
        _api = api;
        _session = session;
        _navigateTo = navigateTo;

        KpiCards = [];
        ModuleCards = [];

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string target && !string.IsNullOrWhiteSpace(target))
                _navigateTo(target);
        });

        _session.SessionChanged += (_, _) => ResetForSession();
    }

    private void ResetForSession()
    {
        _tasksActiveCount = 0;
        _tasksTotalCount = 0;
        _requestsPendingCount = 0;
        _requestsTotalCount = 0;
        _auditCount = 0;
        _totalNotificationsCount = 0;
        UnreadNotificationsCount = 0;
        _slaRate = "100%";
        _slaStatus = "Tối ưu";
        KpiCards.Clear();
        ModuleCards.Clear();
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanCreateRequest));
        OnPropertyChanged(nameof(CanAccessApprovals));
    }

    public SessionManager Session => _session;
    public ObservableCollection<KpiCardModel> KpiCards { get; }
    public ObservableCollection<ModuleCardModel> ModuleCards { get; }

    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public int UnreadNotificationsCount { get => _unreadNotificationsCount; private set => SetProperty(ref _unreadNotificationsCount, value); }

    public bool CanCreateTask => _session.HasPermission("tasks.create");
    public bool CanCreateRequest => _session.HasPermission("requests.create");
    public bool CanAccessApprovals => _session.HasAnyPermission("tasks.confirm", "tasks.read.managed", "roles.configure");

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand NavigateCommand { get; }

    public async Task LoadAsync()
    {
        if (!_session.IsSignedIn) return;
        await _loadLock.WaitAsync();
        IsLoading = true;
        try
        {
            OnPropertyChanged(nameof(CanCreateTask));
            OnPropertyChanged(nameof(CanCreateRequest));
            OnPropertyChanged(nameof(CanAccessApprovals));

            BuildModuleCards();

            // 1. Notifications count
            try
            {
                var notifs = await _api.GetNotificationsAsync(1, 5);
                UnreadNotificationsCount = notifs.UnreadCount;
                _totalNotificationsCount = (int)notifs.Total;
            }
            catch { }

            // 2. Platform Admin vs Tenant User metrics
            if (_session.IsPlatformAdmin)
            {
                try
                {
                    var pendingCompanies = await _api.GetCompanyRegistrationsAsync(1, 1, "PENDING");
                    _tasksActiveCount = (int)pendingCompanies.Total;
                }
                catch { }

                try
                {
                    var audits = await _api.GetAuditLogsAsync(1, 1);
                    _auditCount = (int)audits.Total;
                }
                catch { }
            }
            else
            {
                try
                {
                    var sla = await _api.GetSlaReportAsync();
                    _slaRate = $"{sla.ComplianceRatePercent:F1}%";
                    _slaStatus = sla.ComplianceRatePercent >= 95 ? "Tối ưu"
                        : sla.ComplianceRatePercent >= 80 ? "Khá"
                        : "Cần chú ý";
                }
                catch { }

                if (_session.HasAnyPermission("reports.manager", "roles.configure", "tasks.read.tenant"))
                {
                    try
                    {
                        var dash = await _api.GetManagerDashboardAsync();
                        _tasksActiveCount = dash.TaskMetrics.ActiveTasks;
                        _tasksTotalCount = dash.TaskMetrics.TotalTasks;
                        _requestsPendingCount = dash.RequestMetrics.PendingRequests;
                        _requestsTotalCount = dash.RequestMetrics.TotalRequests;
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        var allTasks = await _api.GetTasksAsync(1, 1);
                        var completedTasks = await _api.GetTasksAsync(1, 1, status: "COMPLETED");
                        var cancelledTasks = await _api.GetTasksAsync(1, 1, status: "CANCELLED");
                        _tasksTotalCount = (int)allTasks.Total;
                        _tasksActiveCount = Math.Max(0, _tasksTotalCount - (int)completedTasks.Total - (int)cancelledTasks.Total);
                    }
                    catch { }

                    try
                    {
                        var allReqs = await _api.GetRequestsAsync(1, 1);
                        var closedReqs = await _api.GetRequestsAsync(1, 1, status: "CLOSED");
                        var cancelledReqs = await _api.GetRequestsAsync(1, 1, status: "CANCELLED");
                        var rejectedReqs = await _api.GetRequestsAsync(1, 1, status: "REJECTED");
                        _requestsTotalCount = (int)allReqs.Total;
                        _requestsPendingCount = Math.Max(0, _requestsTotalCount - (int)closedReqs.Total - (int)cancelledReqs.Total - (int)rejectedReqs.Total);
                    }
                    catch { }
                }
            }

            BuildKpiCards();
        }
        finally
        {
            IsLoading = false;
            _loadLock.Release();
        }
    }

    private void BuildKpiCards()
    {
        KpiCards.Clear();
        switch (_session.UserRoleCode)
        {
            case "SUPERADMIN":
                KpiCards.Add(new KpiCardModel
                {
                    Title = "HỒ SƠ ĐĂNG KÝ",
                    Badge = "Chờ duyệt",
                    Metric = _tasksActiveCount.ToString(),
                    Sublabel = $"{_tasksActiveCount} doanh nghiệp chờ duyệt",
                    TargetView = "PlatformCompanies",
                    ActionLabel = "Duyệt đăng ký doanh nghiệp →",
                    AccentColor = "#2563EB"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "KIỂM TOÁN HỆ THỐNG",
                    Badge = "Nhật ký bảo mật",
                    Metric = _auditCount.ToString(),
                    Sublabel = $"{_auditCount} bản ghi kiểm toán",
                    TargetView = "PlatformAudit",
                    ActionLabel = "Xem nhật ký kiểm toán →",
                    AccentColor = "#145DA0"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "TRẠNG THÁI NỀN TẢNG",
                    Badge = "100% hoạt động",
                    Metric = "Ổn định",
                    Sublabel = "Tất cả dịch vụ trực tuyến",
                    TargetView = "PlatformCompanies",
                    ActionLabel = "Giám sát vận hành →",
                    AccentColor = "#059669"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "THÔNG BÁO HỆ THỐNG",
                    Badge = "Chưa đọc",
                    Metric = UnreadNotificationsCount.ToString(),
                    Sublabel = $"{UnreadNotificationsCount} thông báo mới",
                    TargetView = "Notifications",
                    ActionLabel = "Xem thông báo →",
                    AccentColor = "#17436F"
                });
                break;

            case "COMPANY_ADMIN":
                KpiCards.Add(new KpiCardModel
                {
                    Title = "NHIỆM VỤ TOÀN CÔNG TY",
                    Badge = "Đang làm",
                    Metric = _tasksActiveCount.ToString(),
                    Sublabel = $"{_tasksActiveCount} đang làm · {_tasksTotalCount} tổng số",
                    TargetView = "Tasks",
                    ActionLabel = "Xem danh sách công việc →",
                    AccentColor = "#2563EB"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "YÊU CẦU TOÀN CÔNG TY",
                    Badge = "Chưa xong",
                    Metric = _requestsPendingCount.ToString(),
                    Sublabel = $"{_requestsPendingCount} chưa xong · {_requestsTotalCount} tổng số",
                    TargetView = "Requests",
                    ActionLabel = "Xem danh sách yêu cầu →",
                    AccentColor = "#145DA0"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "TUÂN THỦ SLA DOANH NGHIỆP",
                    Badge = _slaRate,
                    Metric = _slaStatus,
                    Sublabel = "Cam kết chất lượng vận hành",
                    TargetView = "Reports",
                    ActionLabel = "Báo cáo & thống kê →",
                    AccentColor = "#059669"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "HỘP THƯ CẬP NHẬT",
                    Badge = "Chưa đọc",
                    Metric = UnreadNotificationsCount.ToString(),
                    Sublabel = $"{UnreadNotificationsCount} chưa đọc · {_totalNotificationsCount} tổng số",
                    TargetView = "Notifications",
                    ActionLabel = "Xem thông báo →",
                    AccentColor = "#17436F"
                });
                break;

            case "MANAGER":
                KpiCards.Add(new KpiCardModel
                {
                    Title = "NHIỆM VỤ PHÒNG BAN",
                    Badge = "Phụ trách",
                    Metric = _tasksActiveCount.ToString(),
                    Sublabel = $"{_tasksActiveCount} đang làm · {_tasksTotalCount} tổng số",
                    TargetView = "Tasks",
                    ActionLabel = "Xem danh sách công việc →",
                    AccentColor = "#2563EB"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "YÊU CẦU PHÒNG BAN",
                    Badge = "Cần duyệt",
                    Metric = _requestsPendingCount.ToString(),
                    Sublabel = $"{_requestsPendingCount} chưa xong · {_requestsTotalCount} tổng số",
                    TargetView = "Requests",
                    ActionLabel = "Xem danh sách yêu cầu →",
                    AccentColor = "#145DA0"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "TUÂN THỦ SLA PHÒNG BAN",
                    Badge = _slaRate,
                    Metric = _slaStatus,
                    Sublabel = "Tiến độ xử lý phòng ban",
                    TargetView = "SlaMonitor",
                    ActionLabel = "Giám sát SLA →",
                    AccentColor = "#059669"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "HỘP THƯ CẬP NHẬT",
                    Badge = "Chưa đọc",
                    Metric = UnreadNotificationsCount.ToString(),
                    Sublabel = $"{UnreadNotificationsCount} chưa đọc · {_totalNotificationsCount} tổng số",
                    TargetView = "Notifications",
                    ActionLabel = "Xem thông báo →",
                    AccentColor = "#17436F"
                });
                break;

            default: // EMPLOYEE
                var empRate = _tasksTotalCount > 0
                    ? $"{Math.Round(((_tasksTotalCount - _tasksActiveCount) / (double)_tasksTotalCount) * 100)}%"
                    : "100%";
                KpiCards.Add(new KpiCardModel
                {
                    Title = "NHIỆM VỤ CỦA TÔI",
                    Badge = "Được giao",
                    Metric = _tasksActiveCount.ToString(),
                    Sublabel = $"{_tasksActiveCount} đang làm · {_tasksTotalCount} tổng số",
                    TargetView = "Tasks",
                    ActionLabel = "Xem nhiệm vụ cá nhân →",
                    AccentColor = "#2563EB"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "YÊU CẦU CỦA TÔI",
                    Badge = "Đã gửi",
                    Metric = _requestsPendingCount.ToString(),
                    Sublabel = $"{_requestsPendingCount} chưa xong · {_requestsTotalCount} tổng số",
                    TargetView = "Requests",
                    ActionLabel = "Xem yêu cầu của tôi →",
                    AccentColor = "#145DA0"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "TIẾN ĐỘ HOÀN THÀNH",
                    Badge = empRate,
                    Metric = "Đạt chuẩn",
                    Sublabel = "Chỉ số năng suất cá nhân",
                    TargetView = "Tasks",
                    ActionLabel = "Chi tiết tiến độ →",
                    AccentColor = "#059669"
                });
                KpiCards.Add(new KpiCardModel
                {
                    Title = "HỘP THƯ CẬP NHẬT",
                    Badge = "Chưa đọc",
                    Metric = UnreadNotificationsCount.ToString(),
                    Sublabel = $"{UnreadNotificationsCount} chưa đọc · {_totalNotificationsCount} tổng số",
                    TargetView = "Notifications",
                    ActionLabel = "Xem thông báo →",
                    AccentColor = "#17436F"
                });
                break;
        }
    }

    private void BuildModuleCards()
    {
        ModuleCards.Clear();
        if (_session.IsPlatformAdmin)
        {
            ModuleCards.Add(new ModuleCardModel(
                "🏢",
                "Quản lý doanh nghiệp & Cung cấp workspace",
                "Phê duyệt hồ sơ đăng ký doanh nghiệp, cấp phát tenant key và quản lý trạng thái tài khoản công ty.",
                "Quản lý doanh nghiệp →",
                "PlatformCompanies"));
            ModuleCards.Add(new ModuleCardModel(
                "🛡️",
                "Kiểm toán hệ thống nền tảng",
                "Truy vết toàn diện nhật ký bảo mật, định tuyến xác thực và truy cập tài nguyên toàn hệ thống.",
                "Xem nhật ký kiểm toán →",
                "PlatformAudit"));
            return;
        }

        if (_session.HasAnyPermission("tasks.read.tenant", "tasks.read.managed", "tasks.read.own", "tasks.read.assigned"))
        {
            ModuleCards.Add(new ModuleCardModel(
                "📋",
                "Quản lý Nhiệm vụ",
                "Khởi tạo, phân công, theo dõi danh mục kiểm tra, nộp bằng chứng và phê duyệt kết quả công việc.",
                "Xem danh sách công việc →",
                "Tasks"));
        }

        if (_session.HasAnyPermission("requests.read.tenant", "requests.read.managed", "requests.read.own"))
        {
            ModuleCards.Add(new ModuleCardModel(
                "📨",
                "Yêu cầu Dịch vụ",
                "Gửi yêu cầu dịch vụ nội bộ, điều phối về phòng ban chuyên trách và xác nhận kết quả xử lý.",
                "Xem danh sách yêu cầu →",
                "Requests"));
        }

        if (_session.HasAnyPermission("tasks.confirm", "tasks.read.managed", "roles.configure"))
        {
            ModuleCards.Add(new ModuleCardModel(
                "✅",
                "Trung tâm Phê duyệt",
                "Đánh giá kết quả bàn giao công việc, cột mốc xác nhận và giải quyết yêu cầu dịch vụ của bộ phận.",
                "Mở trung tâm phê duyệt →",
                "Approvals"));
        }

        if (_session.HasAnyPermission("workflows.configure", "workflows.read"))
        {
            ModuleCards.Add(new ModuleCardModel(
                "🔀",
                "Thiết kế Quy trình (Workflow Designer)",
                "Cấu hình máy trạng thái quy trình, các bước phê duyệt, điều kiện phân nhánh và phiên bản bất biến.",
                "Thiết kế quy trình →",
                "Workflows"));
        }

        if (_session.HasAnyPermission("reports.manager", "reports.company", "reports.workload", "reports.sla"))
        {
            ModuleCards.Add(new ModuleCardModel(
                "📊",
                "Báo cáo & Phân tích thông minh",
                "Bảng điều hành quản trị, phân bổ tải công việc và biểu đồ tuân thủ SLA theo thời gian thực.",
                "Xem Báo cáo & Chỉ số →",
                "Reports"));
        }

        if (_session.HasPermission("records.search"))
        {
            ModuleCards.Add(new ModuleCardModel(
                "🔍",
                "Tra cứu Hồ sơ Hợp nhất",
                "Tìm kiếm toàn văn nâng cao trên toàn bộ nhiệm vụ, yêu cầu, thảo luận và hồ sơ lưu trữ.",
                "Tra cứu hồ sơ →",
                "RecordSearch"));
        }

        if (_session.HasAnyPermission("users.read", "departments.read", "roles.configure", "service.create", "sla.read"))
        {
            ModuleCards.Add(new ModuleCardModel(
                "👥",
                "Tổ chức, Nhân sự & Cài đặt Doanh nghiệp",
                "Quản lý phòng ban, danh bạ nhân sự (nhập Excel/CSV), vai trò RBAC, dịch vụ nội bộ và hồ sơ SLA.",
                "Quản trị tổ chức & cài đặt →",
                "Organization"));
        }

        if (_session.HasPermission("audit.read"))
        {
            ModuleCards.Add(new ModuleCardModel(
                "🛡️",
                "Nhật ký Kiểm toán Doanh nghiệp",
                "Bản ghi bất biến ghi lại toàn bộ giao dịch, thay đổi trạng thái và quyết định phê duyệt.",
                "Xem nhật ký kiểm toán →",
                "Audit"));
        }
    }
}
