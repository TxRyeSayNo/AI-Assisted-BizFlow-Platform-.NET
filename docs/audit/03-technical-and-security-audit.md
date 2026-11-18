# 03. Kiểm Toán Kỹ Thuật, Dữ Liệu & Bảo Mật (Technical & Security Audit)

Báo cáo tổng hợp kiểm tra tầng API, Persistence/EF Core, WPF MVVM Desktop, cơ chế cách ly dữ liệu đa tenant và các lỗ hổng bảo mật đã phát hiện/khắc phục.

---

## 1. Kiến trúc Persistence, EF Core 10 & PostgreSQL 18

### 1.1 Điểm mạnh nền tảng
- **Cô lập dữ liệu đa Tenant (`BizFlowDbContext.cs` lines 69-110):** Toàn bộ các bảng nghiệp vụ chính đều gắn `HasQueryFilter` bắt buộc `CurrentUserId != null && CurrentTenantId != null && TenantId == CurrentTenantId` (fail-closed khi thiếu context). Riêng bảng `Notification` lọc thêm `x.RecipientId == CurrentUserId` để người dùng chỉ đọc được thông báo của chính mình.
- **Bảo vệ chống xóa/ghi đè ngoài kiểm soát:** Kết hợp Soft Delete (`DeletedAt == null`), PostgreSQL `xmin` Optimistic Concurrency, Constraint kiểm tra idempotency trên `AuditLog` (`UX_AuditLog_TaskCreationReplay`, `CK_AuditLog_TaskReplay`), và `SaveChangesAsync` tenant write guards (`AssertTenant`).
- **Migration Rollback Safety:** Các migration đều kèm khối kiểm tra dữ liệu trước khi `DropTable`/`DropColumn`.

### 1.2 Lỗi tầng Persistence / API đã phát hiện và khắc phục triệt để
1. **Thiếu ánh xạ `DbUpdateConcurrencyException` sang HTTP `409 Conflict` (`backend/BizFlow.Api/Errors/ApiExceptionHandler.cs`):**
   - *Hiện tượng cũ:* Khi hai người cùng thao tác đồng thời làm sai lệch `xmin`, `DbUpdateConcurrencyException` rơi vào lỗi chung trả về HTTP `500 InternalServerError`.
   - *Khắc phục:* Ánh xạ `DbUpdateConcurrencyException` thành `409 Conflict` với mã `CONCURRENCY.CONFLICT` và thông điệp thân thiện, không rò rỉ chi tiết schema PostgreSQL.
2. **Lỗi Migration Rollback Test Contradiction (`20261111090436_TaskParentTaskAndAiLearnedChoice.cs` & `TaskDraftPersistenceTests.cs`):**
   - *Hiện tượng:* Khi siết `20261111090436.Down()` chặn mọi bản ghi `"Task"`, test cũ `TaskListApiTests.Replay_constraint_and_index_are_present_and_populated_downgrade_is_refused` thất bại vì `20261111090436` chặn trước khi kịp lùi về `20261006033038_TaskCreationReplayProtection`. Ngược lại, khi `20261111090436.Down()` chỉ chặn `"ParentTaskId" IS NOT NULL` để cho phép lùi về trước nữa, `TaskDraftPersistenceTests.Empty_rollback_reapplies_and_nonempty_task_history_blocks_rollback` lại truy vấn `db.WorkTasks` bằng schema mới sau khi `ParentTaskId` vừa bị gỡ khiến Npgsql báo `42703: column t.ParentTaskId does not exist`.
   - *Khắc phục chuẩn xác:* Giữ điều kiện `WHERE "ParentTaskId" IS NOT NULL` ở `20261111090436.Down()` (chỉ chặn khi mất dữ liệu phân cấp mới hoặc `AiLearnedChoice`), đồng thời cập nhật `TaskDraftPersistenceTests.cs:195` gọi `await migration.Database.MigrateAsync()` sau khi xác nhận migration gốc `TaskDraftFoundation` đã chặn mất dữ liệu trước khi truy vấn `db.WorkTasks`.
3. **Lỗi trùng khóa `IX_Notification_TenantId_IdempotencyKey` (`RequestRoutingCommand.cs:157`):**
   - Đã thêm `/{recipientId:N}` (tổng độ dài <= 79 ký tự, nằm trong giới hạn 180 của `Notification.Create`) để mỗi người quản lý trong phòng ban nhận thông báo riêng không bị đụng unique key.
4. **Lỗi `CK_AIAgentAction_ExecutionStatus` (`AIModelConfiguration.cs:110`):**
   - Sửa ValueConverter ánh xạ đúng hai chiều giữa `AgentExecutionStatus.NotExecuted` và `"NOT_EXECUTED"` trong PostgreSQL.

---

## 2. An toàn & Chất lượng Tầng WPF Desktop (`BizFlow.Desktop`)

### 2.1 Các lỗi thực thi & bảo mật giao diện đã khắc phục
| Vấn đề | Vị trí mã nguồn | Cách khắc phục |
| :--- | :--- | :--- |
| Lộ mật khẩu dạng plain-text khi gõ đăng nhập | `Views/AuthView.xaml`, `AuthView.xaml.cs`, `Resources/BrandTheme.xaml` | Bổ sung `ModernPasswordBoxStyle`, chuyển ô nhập mật khẩu sang `PasswordBox` (`LoginPasswordBox`) đồng bộ hai chiều với `AuthViewModel.Password` (hỗ trợ cả thao tác ẩn/hiện nếu có và bảo vệ clipboard/visual) |
| Rò rỉ dữ liệu màn hình giữa hai phiên đăng nhập trên cùng app | `TasksViewModel`, `RequestsViewModel`, `ApprovalsViewModel`, `WorkflowsViewModel`, `OrganizationViewModel`, `ReportsViewModel`, `OperationsAndPlatformViewModel`, `AiRobotAssistantViewModel`, `WorkspaceDashboardViewModel` | Đăng ký lắng nghe `SessionChanged` để xóa sạch bộ nhớ đệm danh sách, chi tiết chọn hiện tại và reset cờ tải lại khi đăng xuất / đổi tài khoản |
| Nút hành động bị bấm đúp khi đang xử lý hoặc sai trạng thái | `RequestsViewModel`, `ApprovalsViewModel`, `TasksViewModel` | Ràng buộc `CanExecute` theo `RequestLifecyclePolicy`, phát `CommandManager.InvalidateRequerySuggested()` ngay khi `IsBusy` thay đổi |
| Bị nuốt lỗi bất đồng bộ trong shell chính (`_ = ...`) | `MainWindowViewModel.cs` | Thay thế bằng helper `RunSafelyAsync` bắt exception có kiểm soát và hiển thị thông báo rõ ràng qua `ToastNotificationService` |
| Sai chính tả hiển thị & nhãn chọn người xử lý thiếu ngữ cảnh | `Views/WorkflowsView.xaml:124`, `Views/TasksView.xaml:299`, `Models/ApiDtos.cs:552` | Sửa ký tự Cyrillic `ТRÍCH` -> `TRÍCH`; thêm `UserRowDto.AssigneeOptionLabel` (`FullName (Role · Department)`) |
| Tự bịa lý do khi từ chối/yêu cầu làm lại để trống ghi chú | `ApprovalsViewModel` (`ExecuteQuickDecisionAsync`) | Chặn thao tác từ chối/trả lại nếu `ApprovalNote` rỗng, hiển thị Toast cảnh báo yêu cầu người dùng nhập lý do thật |
| KPI nhân viên luôn hiển thị `0%` do sai cách đếm trang | `WorkspaceDashboardViewModel.cs` | Truy vấn đúng tổng số bản ghi theo trạng thái (`pageSize=1`) cho `COMPLETED`, `CANCELLED`, `CLOSED`, `REJECTED` |

### 2.2 Quản lý tiến trình Backend (`BackendHostSupervisor.cs`)
- `BackendHostSupervisor` tự khởi chạy `BizFlow.Api` cục bộ nếu chưa có backend, kiểm tra đồng thời cả `/health/live` và `/health/ready` trước khi cho Desktop gửi request (tránh lỗi race condition lúc khởi động).
- Cho phép truyền `HttpClient? customHttpClient = null` vào `BizFlowApiClient` để kiểm thử tự động các ViewModel không cần mocking cồng kềnh.

---

## 3. Kiểm toán Bảo mật API & Cách ly Quyền truy cập

1. **Tài khoản bị vô hiệu hóa (`INACTIVE`):** Đã kiểm chứng trong `EnterpriseAuditIntegrationTests`: người dùng trạng thái `INACTIVE` bị từ chối ngay từ bước login và không thể sử dụng hệ thống.
2. **Cách ly dữ liệu chéo tài khoản cùng Tenant (Data Isolation):**
   - `RequestConfirmationCommand`: Không cho phép Admin hoặc Manager xác nhận đóng yêu cầu thay cho người gửi (`403 Forbidden`).
   - `AiAssistanceService` (`EnsureTaskReadAccessAsync`): Đã bổ sung kiểm tra `TaskReadPolicy.Resolve` thông qua `IAccessSnapshotProvider` cho `/api/v1/ai/task-risk` và `/api/v1/ai/task-summary`. Nhân viên ngoài phòng ban / không liên quan (`OutsiderEmployee`) nhận `403 Forbidden` thay vì đọc trộm nội dung công việc mật qua AI.
3. **Giới hạn chưa có của lớp Bảo mật MVP:**
   - Endpoint gửi liên kết đăng nhập qua email (`EmailLinkAuthenticationController`) hiện chỉ lưu token nội bộ chứ chưa có kênh gửi mail SMTP được xác thực DKIM/SPF. Khuyến nghị tắt/ẩn tính năng gửi mail hoặc gắn nhãn "Local Dev Token Only" nếu triển khai ra ngoài mạng nội bộ.
