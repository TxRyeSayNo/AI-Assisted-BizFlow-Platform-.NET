# 01. Kiểm Kê Kiến Trúc & Danh Mục Chức Năng (Architecture & Feature Inventory)

**Dự án:** AI-Assisted BizFlow Platform (Nền tảng Quản lý & Điều phối Công việc/Yêu cầu Nội bộ tích hợp AI)  
**Ngôn ngữ / Nền tảng:** .NET 10 (`net10.0` / `net10.0-windows`), C#, WPF Desktop (MVVM), ASP.NET Core Web API, EF Core 10, PostgreSQL 18, OpenAI API (`PRN_KEY`).  
**Phạm vi kiểm kê:** Trực tiếp từ mã nguồn giải pháp `BizFlow.sln` sau đợt dọn dẹp kiến trúc và sửa lỗi toàn diện.

---

## 1. Cấu trúc Giải pháp & Kết quả Dọn dẹp Kiến trúc Cũ

### 1.1 Các thành phần không phù hợp đã loại bỏ
Trước khi kiểm toán nghiệp vụ, repository còn tồn đọng stack giao diện web/test JS cũ không còn thuộc kiến trúc WPF + .NET của môn PRN212:
- Đã xóa: `frontend/`, `node_modules/` gốc, `e2e/`, `tests/BizFlow.BrowserHarness/`, `tests/BizFlow.E2ETests/`, `package.json`, `package-lock.json`, `playwright.config.ts`, `build.log`, `skills-lock.json`, `backend/test_schema2.sql`, `.vscode/`, `deploy/web.Dockerfile`, `deploy/nginx.conf`, `deploy/docker-compose.app.yml`, `deploy/Test-Containers.ps1`.
- Đã đồng bộ lại: `BizFlow.sln`, `BizFlow.slnx`, `README.md`, `deploy/.env.example`, `deploy/README.md`, `.gitignore`, `.dockerignore`.

### 1.2 Cấu trúc 7 Project hiện hành trong `BizFlow.sln`
| Project | Tầng / Vai trò | Target Framework | Đường dẫn `.csproj` |
| :--- | :--- | :--- | :--- |
| `BizFlow.Domain` | Entity, Value Rules, State Machine Policy (`TaskLifecyclePolicy`, `RequestLifecyclePolicy`, `TaskReadPolicy`, `RequestReadPolicy`) | `net10.0` | `backend/BizFlow.Domain/BizFlow.Domain.csproj` |
| `BizFlow.Application` | Use-case Commands/Queries, RBAC Authorizer, AI Services (`AiAssistanceService`, `RobotChatService`, `AiTaskDecompositionService`, `WorkflowDocumentAnalysis`) | `net10.0` | `backend/BizFlow.Application/BizFlow.Application.csproj` |
| `BizFlow.Infrastructure` | EF Core 10 (`BizFlowDbContext`), Npgsql PostgreSQL 18, `39` Migrations, Stores, `OpenAiModelClient`, `AiEmbeddingService`, `ProactiveAgentDaemon`, `AiActionDispatcher` | `net10.0` | `backend/BizFlow.Infrastructure/BizFlow.Infrastructure.csproj` |
| `BizFlow.Api` | ASP.NET Core REST API (`27` Controllers), JWT Bearer + Cookie Auth, Exception Handler (`ApiExceptionHandler`), Health Probes (`/health/live`, `/health/ready`) | `net10.0` | `backend/BizFlow.Api/BizFlow.Api.csproj` |
| `BizFlow.Desktop` | WPF Desktop Client (MVVM thủ công với `MvvmPrimitives.cs`), `MainWindow.xaml` + `9` màn hình Views, `BackendHostSupervisor` tự quản lý tiến trình API | `net10.0-windows` | `backend/BizFlow.Desktop/BizFlow.Desktop.csproj` |
| `BizFlow.UnitTests` | Kiểm thử đơn vị Domain/Application/AI & WPF STA XAML/ViewModel (`579` test cases, trong đó `11` test Desktop) | `net10.0-windows` | `tests/BizFlow.UnitTests/BizFlow.UnitTests.csproj` |
| `BizFlow.IntegrationTests` | Kiểm thử tích hợp End-to-End API + EF Core Migrations + PostgreSQL 18 thật qua Testcontainers + `1` kiểm thử `RealOpenAi` (`309` test cases tổng cộng) | `net10.0` | `tests/BizFlow.IntegrationTests/BizFlow.IntegrationTests.csproj` |

---

## 2. Cơ sở dữ liệu & Danh mục API

### 2.1 Thống kê EF Core & PostgreSQL (`BizFlowDbContext.cs`)
- **Database Provider:** `Npgsql.EntityFrameworkCore.PostgreSQL` (PostgreSQL 18).
- **Tổng số `DbSet` trong `BizFlowDbContext`:** `41` tập thực thể (Tenancy, Company, Organization/Department/ManagementScope, Identity/UserAccount/Role/Permission/UserRole/RolePermission/AuthenticationSession, Services/InternalService/ServiceCategory, SlaProfile/SlaVersion/BusinessCalendar, WorkRequest/RequestRouting/RequestResolution, WorkTask/TaskChecklistItem/TaskAssignment/TaskProgressReport/TaskResult/Confirmation, Comment/Attachment, AuditLog, Notification/DeadlineReminderSchedule/WebPushSubscription/NotificationLog, WorkflowDefinition/WorkflowVersion/WorkflowStep/WorkflowTransition, AIInteraction/AIRecommendation/AIAgentAction/AiVectorMemory/AiLearnedChoice).
- **Tổng số Migrations đã áp dụng:** `39` migration files (`2026*.cs` không tính `.Designer.cs`).
- **Tập dữ liệu mẫu / Demo (`deploy/`):** `seed_sample_data.sql` và `seed_enterprise_simulation.sql`.

### 2.2 Toàn bộ `27` API Controllers (`backend/BizFlow.Api/Controllers/`)
1. **Xác thực & Tài khoản:** `AuthenticationController.cs`, `EmailLinkAuthenticationController.cs`, `PasswordResetController.cs`, `PublicCompanyRegistrationsController.cs`, `CompanyRegistrationsController.cs`.
2. **Tổ chức & Phân quyền:** `UsersController.cs`, `DepartmentsController.cs`, `RolesController.cs`, `TenantCodeAndDocumentController.cs`.
3. **Nghiệp vụ Yêu cầu & Công việc:** `RequestsController.cs`, `TasksController.cs`, `WorkflowsController.cs`, `ServicesController.cs`, `SlaProfilesController.cs`.
4. **Cộng tác, Lưu trữ & Báo cáo:** `CommentsController.cs`, `AttachmentsController.cs`, `NotificationsController.cs`, `RecordsController.cs`, `DashboardController.cs`, `ReportsController.cs`, `AuditController.cs`.
5. **Tích hợp AI:** `AiController.cs`, `RobotChatController.cs`, `AiTaskDecompositionController.cs`, `AiDocumentSummarizerController.cs`, `AiBranchingController.cs`, `AiLearnedChoicesController.cs`.

---

## 3. Danh mục Giao diện WPF Desktop (`backend/BizFlow.Desktop/`)

| Thành phần XAML | ViewModel tương ứng | Chức năng đã xác minh trong mã nguồn |
| :--- | :--- | :--- |
| `MainWindow.xaml` | `MainWindowViewModel.cs` | Khung điều hướng theo quyền RBAC, hộp thông báo `Notifications`, thanh chuyển chế độ AI Robot / workspace, cơ chế `RunSafelyAsync` bắt lỗi bất đồng bộ ra Toast |
| `Views/AuthView.xaml` | `AuthViewModel.cs` | Đăng nhập (đã chuyển ô mật khẩu sang `PasswordBox`), đổi mật khẩu, yêu cầu/đặt lại mật khẩu, đăng ký công ty, yêu cầu email link |
| `Views/DashboardView.xaml` | `WorkspaceDashboardViewModel.cs` | Tổng quan KPI cá nhân/phòng ban/công ty theo `pageSize=1` status totals |
| `Views/RequestsView.xaml` | `RequestsViewModel.cs` | Danh sách, lọc, tạo mới (nháp/gửi ngay), gửi duyệt, điều phối (`Route` kèm nút bỏ chọn người xử lý), tiếp nhận, bắt đầu, hoàn tất, xác nhận (`Requester`), yêu cầu làm lại, từ chối (`SUBMITTED`/`ROUTED`), hủy, tạo bản sửa đổi (`Revise`), lưu trữ (`Archive`), gợi ý điều phối AI |
| `Views/TasksView.xaml` | `TasksViewModel.cs` | Tạo/gán công việc chính & công việc con (`ParentTaskId`), chọn người thực hiện theo `AssigneeOptionLabel` (`FullName (Role · Department)`), nhận/từ chối/cập nhật tiến độ/nộp kết quả/xác nhận kết quả, phân rã AI & đánh giá rủi ro AI |
| `Views/ApprovalsView.xaml` | `ApprovalsViewModel` (`ApprovalsAndWorkflowsViewModel.cs`) | Hàng chờ hợp nhất: Task `SUBMITTED` chờ xác nhận, Request `ROUTED` chờ tiếp nhận, Request `RESOLVED` của chính người gửi chờ xác nhận đóng/làm lại; bắt buộc ghi chú khi từ chối/yêu cầu làm lại |
| `Views/WorkflowsView.xaml` | `WorkflowsViewModel` (`ApprovalsAndWorkflowsViewModel.cs`) | Quản lý thư viện quy trình, phân tích tài liệu/văn bản quy trình với AI (`AnalyzeWorkflowText`) |
| `Views/OrganizationView.xaml` | `OrganizationViewModel` (`OrganizationAndAnalyticsViewModel.cs`) | Quản lý phòng ban, danh bạ nhân sự, vai trò, dịch vụ nội bộ, cấu hình SLA |
| `Views/ReportsView.xaml` | `ReportsViewModel` (`OrganizationAndAnalyticsViewModel.cs`) | Báo cáo tổng hợp công việc/yêu cầu theo trạng thái và hiệu suất |
| `Views/OperationsView.xaml` | `OperationsAndPlatformViewModel` & `AiRobotAssistantViewModel` (`OrganizationAndAnalyticsViewModel.cs`) | Tìm kiếm/lưu trữ hồ sơ (`Records`), nhật ký kiểm toán (`Audit`), quản trị công ty chờ duyệt (Platform), AI Robot Chat |

---

## 4. Tóm tắt Tính năng Đã hoàn thiện vs Giới hạn MVP thực tế

- **Đã hoạt động đầy đủ end-to-end (API + PostgreSQL + Desktop ViewModel):**
  - Xác thực đa tenant JWT (`accessToken` + `refreshToken`), khóa tài khoản `INACTIVE` trước và sau khi phát hành token.
  - Chu trình Yêu cầu nội bộ (`DRAFT -> SUBMITTED -> ROUTED -> IN_PROGRESS -> RESOLVED -> CLOSED`, kèm `Rework`, `Reject`, `Cancel`, `Revise`, `Archive`).
  - Chu trình Công việc & Công việc con (`DRAFT -> ASSIGNED -> ACCEPTED/REJECTED -> IN_PROGRESS -> SUBMITTED -> COMPLETED`, hỗ trợ phân rã `ParentTaskId` cho phòng ban/nhân viên).
  - Cộng tác đa tầng: Bình luận, tệp đính kèm metadata, thông báo theo người nhận kèm chống trùng idempotency, kiểm toán (`AuditLog`) và tìm kiếm lưu trữ (`Records`).
  - Các endpoint AI kết nối OpenAI (`PRN_KEY`) có fallback heuristic nội bộ và lớp bảo vệ con người xác nhận (`ConfirmationToken`).
- **Giới hạn MVP được ghi nhận minh bạch (không tự bịa đã có):**
  1. Trạng thái `WAITING_FOR_INFORMATION` mới có ở enum/policy tên miền, **chưa có Application Command, API endpoint và UI Desktop**.
  2. Yêu cầu ở trạng thái `DRAFT` **chưa có API cập nhật nội dung trực tiếp** (`PUT/PATCH`), chỉ hỗ trợ tạo bản mới (`Revise`) khi yêu cầu đã bị `REJECTED`.
  3. Checklist đính kèm khi tạo Task (`TaskChecklistItem`) **chưa thể tick cập nhật hoàn thành từng mục lúc chạy** vì `BizFlowDbContext.SaveChangesAsync()` chủ động chặn sửa `TaskChecklistItem` (`Unimplemented edit`).
  4. `EmailLinkAuthenticationService.RequestLinkAsync` mới sinh mã xác thực trong bộ nhớ đệm, **chưa tích hợp SMTP gửi email thật**.
