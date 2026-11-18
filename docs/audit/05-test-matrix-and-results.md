# 05. Ma Trận Kịch Bản Kiểm Thử & Kết Quả Thực Tế (Test Matrix & Results)

Báo cáo trình bày ma trận `30` kịch bản nghiệp vụ trọng yếu từ End-to-End, phân biệt rõ loại kiểm thử đã thực thi (**Integration API + PostgreSQL 18**, **Unit / WPF Desktop STA ViewModel**, **Live OpenAI `PRN_KEY`**) và các **giới hạn MVP / thao tác giao diện cửa sổ trực tiếp chưa kiểm thử thủ công bằng chuột**.

---

## 1. Tổng hợp Quy mô Kiểm thử Tự động (Không có test nào Fail hay Skipped)

- **BizFlow.UnitTests (bao gồm Domain, Application, AI & Desktop ViewModel/XAML STA):** `579` test cases (`579/579 Passed`, `0 Failed`, `0 Skipped`).
  - Trong đó có `11` test kiểm thử tầng WPF Desktop (`BizFlowDesktopMigrationTests` + `EnterpriseDesktopViewModelAuditTests`), bao gồm khởi tạo toàn bộ 9 Views + `MainWindow` trên STA thread và bài test `BackendHostSupervisor` tự khởi chạy `BizFlow.Api` thật.
- **BizFlow.IntegrationTests (`Category!=RealOpenAi`, Testcontainers PostgreSQL 18):** `308` test cases (`308/308 Passed`, `0 Failed`, `0 Skipped`), bao gồm `3` kịch bản tổng hợp cấp doanh nghiệp trong `EnterpriseAuditIntegrationTests.cs`.
- **BizFlow.IntegrationTests (`Category=RealOpenAi`, dùng khóa thật `PRN_KEY`):** `1` test case (`1/1 Passed`, `0 Failed`, `0 Skipped`) + endpoint `/api/v1/ai/task-risk` gọi OpenAI thực trong `EnterpriseAuditIntegrationTests`.
- **Tổng số test tự động toàn giải pháp:** `888` test cases chạy lặp lại **2 vòng Regression liên tiếp (Debug & Release)** đều đạt `0 Failed, 0 Skipped`.

---

## 2. Ma trận 30 Kịch bản Nghiệp vụ Chi tiết (`SC01` – `SC30`)

> **Ký hiệu mức xác minh:**
> - **VERIFIED (API/DB):** Xác minh End-to-End qua HTTP API + EF Core + PostgreSQL 18 Testcontainer.
> - **VERIFIED (WPF VM/XAML):** Xác minh khởi tạo XAML STA + logic ViewModel + `HttpClient`/`BizFlowApiClient`.
> - **VERIFIED (Live OpenAI):** Đã gọi thật OpenAI API qua `PRN_KEY`.
> - **PARTIAL / MVP LIMITATION:** Có giới hạn nghiệp vụ trong MVP (đã ghi rõ ràng).

| Mã | Kịch bản Nghiệp vụ | Bằng chứng kiểm thử (File test cụ thể) | Kết quả & Ghi chú trung thực |
| :--- | :--- | :--- | :--- |
| **SC01** | Đăng nhập đa tenant (`EmployeeCode`/Email + Password), từ chối tài khoản `INACTIVE` | `AuthenticationApiTests.cs`, `EnterpriseAuditIntegrationTests.cs` (`Reject_Revise_Cancel_InactiveUser...`), `AuthViewModel` / `AuthView.xaml` | **VERIFIED (API/DB + XAML STA)** — Đã chuyển `PasswordBox` trên Desktop |
| **SC02** | Refresh token, đăng xuất & xóa cache ViewModel khi chuyển phiên (`SessionChanged`) | `AuthenticationApiTests.cs`, `EnterpriseDesktopViewModelAuditTests.cs` | **VERIFIED (API/DB + WPF VM)** |
| **SC03** | Khởi tạo Yêu cầu nội bộ dạng Nháp (`DRAFT`) & gửi duyệt (`SUBMITTED`) | `RequestApiTests.cs`, `RequestLifecycleApiTests.cs`, `RequestsViewModel` | **VERIFIED (API/DB + WPF VM)** |
| **SC04** | Chỉnh sửa nội dung Yêu cầu khi đang ở trạng thái Nháp (`DRAFT`) | *Kiểm tra mã nguồn `RequestsController.cs` & `RequestsView.xaml`* | **MVP LIMITATION (Chưa hỗ trợ `PUT/PATCH` sửa `DRAFT`)** — Chỉ hỗ trợ gửi hoặc hủy để tạo mới |
| **SC05** | Điều phối Yêu cầu (`SUBMITTED -> ROUTED`) tới phòng ban có >= 2 Manager mà không trùng khóa Notification | `RequestRoutingCommand.cs`, `RequestLifecycleApiTests.cs`, `EnterpriseAuditIntegrationTests.cs` | **VERIFIED (API/DB)** — Đã sửa lỗi `23505` trùng `IdempotencyKey` |
| **SC06** | Tiếp nhận Yêu cầu (`ROUTED -> IN_PROGRESS`) trên API và hàng chờ `ApprovalsViewModel` | `RequestLifecycleApiTests.cs`, `EnterpriseAuditIntegrationTests.cs`, `EnterpriseDesktopViewModelAuditTests.cs` | **VERIFIED (API/DB + WPF VM)** — Sửa lỗi lọc nhầm `SUBMITTED` |
| **SC07** | Bắt đầu & Hoàn tất xử lý Yêu cầu (`IN_PROGRESS -> RESOLVED`) kèm ghi chú kết quả | `RequestResolutionAndConfirmationApiTests.cs`, `EnterpriseAuditIntegrationTests.cs` | **VERIFIED (API/DB)** |
| **SC08** | Xác nhận đóng (`RESOLVED -> CLOSED`) và Yêu cầu làm lại (`Rework`) chỉ cho phép Người gửi gốc (`IsRequester`) | `RequestResolutionAndConfirmationApiTests.cs`, `EnterpriseAuditIntegrationTests.cs`, `RequestsViewModel` | **VERIFIED (API/DB + WPF VM)** — Sửa thông báo `LatestResolverId` |
| **SC09** | Tạm dừng Yêu cầu để yêu cầu bổ sung thông tin (`WAITING_FOR_INFORMATION`) | *Kiểm tra mã nguồn `RequestLifecyclePolicy.cs`, `RequestsController.cs`* | **MVP LIMITATION (Chưa hoàn thiện)** — Mới có enum/policy, chưa có endpoint & UI |
| **SC10** | Từ chối Yêu cầu (`SUBMITTED`/`ROUTED -> REJECTED`) kèm lý do bắt buộc | `EnterpriseAuditIntegrationTests.cs`, `EnterpriseDesktopViewModelAuditTests.cs` | **VERIFIED (API/DB + WPF VM)** — Cấm từ chối rỗng ghi chú |
| **SC11** | Tạo bản sửa đổi (`Revise`) từ Yêu cầu bị từ chối & Hủy Yêu cầu (`CANCELLED`) | `RequestLifecycleCommandTests.cs`, `EnterpriseAuditIntegrationTests.cs`, `RequestsView.xaml` | **VERIFIED (API/DB + WPF VM)** — Đã gắn nút `Revise` lên XAML |
| **SC12** | Tạo Công việc chính (`DRAFT`) kèm Checklist khởi tạo và Hạn chót | `TaskCreationApiTests.cs`, `EnterpriseAuditIntegrationTests.cs` | **VERIFIED (API/DB)** |
| **SC13** | Chặn Admin giao trực tiếp Công việc chính cho Nhân viên (`TASK.MANAGER_ASSIGNMENT_REQUIRED` 422) | `EnterpriseAuditIntegrationTests.cs` (line 117) | **VERIFIED (API/DB)** |
| **SC14** | Giao Công việc chính cho Manager, Manager tạo & giao Công việc con (`ParentTaskId`) cho Nhân viên | `RequestTaskLinkageApiTests.cs`, `EnterpriseAuditIntegrationTests.cs` | **VERIFIED (API/DB)** |
| **SC15** | Chọn người nhận việc rõ vai trò & phòng ban trên Desktop (`AssigneeOptionLabel`) | `ApiDtos.cs`, `TasksView.xaml:299`, `BizFlowDesktopMigrationTests.cs` | **VERIFIED (WPF XAML/DTO)** |
| **SC16** | Nhân viên Nhận việc (`ACCEPTED`) / Từ chối việc (`REJECTED`) / Cập nhật tiến độ | `TaskAcceptanceApiTests.cs`, `TaskExecutionApiTests.cs`, `EnterpriseAuditIntegrationTests.cs` | **VERIFIED (API/DB)** |
| **SC17** | Đánh dấu hoàn thành từng dòng Checklist trong Task lúc chạy | *Kiểm tra `BizFlowDbContext.cs` & `TasksController.cs`* | **MVP LIMITATION (Không hỗ trợ toggle runtime)** — DB chặn cập nhật `TaskChecklistItem` |
| **SC18** | Nộp kết quả công việc (`SUBMITTED`) & Quản lý xác nhận hoàn thành (`COMPLETED`) / Trả lại (`InProgress`) | `TaskResultAndConfirmationApiTests.cs`, `EnterpriseAuditIntegrationTests.cs`, `ApprovalsViewModel` | **VERIFIED (API/DB)** *(Lưu ý: cho phép same-manager confirm nếu có `tasks.confirm`)* |
| **SC19** | Đảm bảo người thực hiện cuối (`OpsEmployee`) nhận được Notification xác nhận/làm lại Yêu cầu | `EnterpriseAuditIntegrationTests.cs` (`RequestConfirmationStore.cs`) | **VERIFIED (API/DB)** |
| **SC20** | Lưu trữ (`Archive`) hồ sơ đã đóng & tìm kiếm (`RecordsSearch` ẩn mặc định, hiện khi `includeArchived=true`) | `RecordArchivalAndSearchApiTests.cs`, `EnterpriseAuditIntegrationTests.cs`, `RequestsView.xaml` | **VERIFIED (API/DB + WPF XAML)** |
| **SC21** | Chống ghi đè đồng thời Optimistic Concurrency (`DbUpdateConcurrencyException` -> HTTP `409`) | `ApiExceptionHandler.cs`, `EnterpriseAuditIntegrationTests.cs` (`_audit-probe/concurrency`) | **VERIFIED (API/DB)** |
| **SC22** | Chống gửi trùng bằng `Idempotency-Key` (replay trả kết quả cũ; sai payload trả `409 Conflict`) | `RequestLifecycleApiTests.cs`, `TaskCreationApiTests.cs` | **VERIFIED (API/DB)** |
| **SC23** | Bảo vệ Migration Rollback an toàn trên PostgreSQL (Empty rollback ok; Non-empty giữ nguyên dữ liệu) | `TaskDraftPersistenceTests.cs`, `TaskEvidencePersistenceTests.cs`, `TaskCreationApiTests.cs` | **VERIFIED (API/DB)** |
| **SC24** | Cách ly quyền đọc AI (`TaskReadPolicy`): Nhân viên ngoài (`OutsiderEmployee`) bị chặn `403 Forbidden` | `EnterpriseAuditIntegrationTests.cs` (lines 286-290) | **VERIFIED (API/DB)** |
| **SC25** | Gọi thật OpenAI API bằng biến môi trường `PRN_KEY` & Fallback khi offline | `OpenAiModelClientTests.cs` (`RealOpenAi`), `AiAssistanceServiceTests.cs`, `EnterpriseAuditIntegrationTests.cs` | **VERIFIED (Live OpenAI + Unit Fallback)** |
| **SC26** | Đề xuất & Thực thi công cụ AI có `ConfirmationToken` (`NOT_EXECUTED` -> `SUCCEEDED`, chặn trạng thái sai `409`) | `AiAutonomousAgentTests.cs`, `EnterpriseAuditIntegrationTests.cs` (lines 296-342) | **VERIFIED (API/DB)** — Đã sửa bug `CK_AIAgentAction_ExecutionStatus` |
| **SC27** | `ProactiveAgentDaemon` quét rủi ro trễ hạn trong scope tenant & phát sinh `AIRecommendation` + `Notification` | `ProactiveAgentDaemon.cs`, `EnterpriseAuditIntegrationTests.cs` (lines 345-363) | **VERIFIED (API/DB)** |
| **SC28** | KPI Dashboard Nhân viên tính đúng số lượng hoàn thành / tổng hồ sơ | `WorkspaceDashboardViewModel.cs`, `EnterpriseDesktopViewModelAuditTests.cs` | **VERIFIED (WPF VM)** |
| **SC29** | Khởi tạo toàn bộ 9 Views + `MainWindow` Desktop không lỗi `XamlParseException` & khởi chạy `BizFlow.Api` tự động | `BizFlowDesktopMigrationTests.cs` | **VERIFIED (WPF STA + Live API Process)** |
| **SC30** | Chạy thao tác bấm chuột/nhập liệu tương tác trực tiếp của con người trên cửa sổ Windows Desktop GUI | *Chưa chạy UI Automation tương tác bàn phím/chuột thực tế trên màn hình* | **NOT EXECUTED INTERACTIVELY** — Đã xác minh ở mức STA ViewModel + XAML Instantiation + Live HTTP Backend |
