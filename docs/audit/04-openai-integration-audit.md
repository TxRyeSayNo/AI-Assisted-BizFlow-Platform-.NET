# 04. Kiểm Toán Tích Hợp OpenAI API (`PRN_KEY`) & An Toàn AI

Báo cáo đánh giá cơ chế nạp khóa `PRN_KEY`, mức độ kết nối OpenAI thực tế trên các dịch vụ AI, cơ chế chống Prompt Injection, Human-in-the-loop và khả năng hoạt động an toàn khi mất kết nối (heuristic fallback).

---

## 1. Cấu hình Khóa Môi trường `PRN_KEY` & Khởi chạy Desktop

- **Vị trí nạp `PRN_KEY`:**
  - `backend/BizFlow.Api/Program.cs`: Ưu tiên đọc `Environment.GetEnvironmentVariable("PRN_KEY")` cho cấu hình OpenAI API key.
  - `backend/BizFlow.Infrastructure/AI/OpenAiModelClient.cs` và `AiEmbeddingService.cs`: Ưu tiên `PRN_KEY` trực tiếp trước khi fallback sang cấu hình `OpenAI:ApiKey`.
  - `backend/BizFlow.Desktop/Services/BackendHostSupervisor.cs`: Khi Desktop tự khởi chạy tiến trình con `BizFlow.Api`, biến môi trường `PRN_KEY` và `OpenAI__ApiKey` được truyền tiếp (forward) an toàn sang môi trường tiến trình API, không bao giờ ghi giá trị khóa ra log hay file tạm.
- **Kiểm chứng thực tế:** Bài test `[Trait("Category", "RealOpenAi")]` trong `OpenAiModelClientTests` đã thực thi thành công kết nối OpenAI trực tiếp bằng `PRN_KEY` (`1/1 Passed`, 2 giây trong cả Debug và Release), và `EnterpriseAuditIntegrationTests` cũng đã gọi thành công `/api/v1/ai/task-risk` khi `PRN_KEY` hoạt động.

---

## 2. Bản đồ Tính năng AI: Thực tế gọi LLM vs Heuristic Fallback

Trước khi kiểm toán, một số hàm trong `AiAssistanceService` mới chỉ trả kết quả giả lập nội bộ (ghi model `"mock-gpt-4o"`) dù hệ thống đã có `OpenAiModelClient`. Sau khi nâng cấp:

| Dịch vụ / Endpoint | Gọi OpenAI (`modelClient.CompleteAsync`) | Cơ chế Fallback khi offline / thiếu `PRN_KEY` | Bảo vệ Prompt Injection & Access Control |
| :--- | :--- | :--- | :--- |
| Hỗ trợ soạn & tóm tắt Task (`task-assistance`, `task-breakdown`) | Có | Heuristic phân tích từ khóa & mẫu công việc (`deterministic-heuristic`) | Kiểm tra quyền RBAC/Tenant |
| Đánh giá rủi ro công việc (`/api/v1/ai/task-risk`, `EvaluateTaskRiskAsync`) | **Đã nâng cấp gọi thật OpenAI** | Deterministic deadline & overdue risk scorer (`deterministic-heuristic`) | Bọc dữ liệu trong `<untrusted_task_data>...</untrusted_task_data>`; **Cưỡng chế `TaskReadPolicy.Resolve`** (`403` với người ngoài) |
| Tóm tắt tiến độ công việc (`/api/v1/ai/task-summary`, `SummarizeTaskProgressAsync`) | **Đã nâng cấp gọi thật OpenAI** | Tổng hợp có cấu trúc từ tiến độ/comment hiện có (`deterministic-heuristic`) | Bọc dữ liệu trong `<untrusted_task_data>`; **Cưỡng chế `TaskReadPolicy.Resolve`** (`403` với người ngoài) |
| Gợi ý điều phối yêu cầu (`/api/v1/ai/request-routing`, `RecommendRequestRoutingAsync`) | **Đã nâng cấp gọi thật OpenAI** | Xếp hạng phòng ban/nhân sự theo lịch sử và tải công việc (`deterministic-heuristic`) | Bọc dữ liệu trong `<untrusted_request_data>` |
| Phân rã đa mục tiêu / AI Branching / Đọc văn bản quy trình / Robot Chat | Có (`OpenAiModelClient` / `WorkflowDocumentAnalysis` / `RobotChatService`) | Fallback mô tả/phản hồi nội bộ an toàn | Kiểm tra quyền theo module tương ứng |

---

## 3. Kiểm soát Hành động Tự động & Con người Phê duyệt (Human-in-the-Loop)

### 3.1 Cơ chế Confirmation Token trên `/api/v1/ai/actions/execute`
1. **Bước đề xuất (`confirmed = false`):** Hệ thống kiểm tra tool nằm trong whitelist (`AiToolRegistry`), kiểm tra quyền `tool.RequiredPermission`, ghi `AIInteraction` và lưu `AIAgentAction` với `ExecutionStatus = NOT_EXECUTED`, đồng thời cấp `ConfirmationToken` có chữ ký xác thực theo `(tenantId, userId, toolName, argsJson)`.
   - *Lỗi nghiêm trọng đã sửa:* `AIModelConfiguration.cs:110` trước đây lưu `AgentExecutionStatus.NotExecuted` thành `"NOTEXECUTED"`, bị PostgreSQL chặn ngay bởi constraint `CK_AIAgentAction_ExecutionStatus` (`HTTP 500`). Đã sửa ValueConverter hai chiều sang `"NOT_EXECUTED"`.
2. **Bước thực thi (`confirmed = true`):** Bắt buộc cung cấp đúng `confirmationToken` hợp lệ chưa tiêu thụ trước khi `AiActionDispatcher` thực thi hành động.

### 3.2 Đóng lỗ hổng bỏ qua State Machine ở `AiActionDispatcher.cs`
- *Trước khi sửa:* `AiActionDispatcher` gọi thao tác sửa đổi thẳng xuống tầng dữ liệu/store, bỏ qua chuyển trạng thái hợp lệ (`DRAFT -> ASSIGNED`), bỏ qua `AuditLog` và ném `InvalidOperationException` (`HTTP 500`) khi trạng thái không phù hợp.
- *Sau khi sửa:* `AiActionDispatcher` ủy quyền trực tiếp cho `TaskAssignmentCommand`, `RequestRoutingCommand` và `CommentService`, đồng thời chặn trạng thái không hợp lệ bằng `FaultKind.Conflict` (`TASK.TRANSITION_DENIED` / `REQUEST.TRANSITION_DENIED` -> HTTP `409 Conflict`).

---

## 4. `ProactiveAgentDaemon`: Quét Rủi ro Chủ động Đúng Scope

- **Lỗi nền đã phát hiện & sửa (`ProactiveAgentDaemon.cs`):**
  - Trước đây, dịch vụ nền quét các công việc sắp quá hạn hoặc lệch tải chạy với `DbContext` không có `CurrentTenantId`/`CurrentUserId`, khiến toàn bộ query trên `WorkTasks` bị `HasQueryFilter` lọc rỗng (`0` dòng) hoặc văng `PERSISTENCE.SCOPE_DENIED` khi gọi `SaveChangesAsync()`.
  - Đã sửa bằng cách giải quyết `DbContextOptions<BizFlowDbContext>`, chọn một người dùng `ACTIVE` đại diện hợp lệ của từng tenant đang hoạt động để khởi tạo `BizFlowDbContext` gắn `DaemonTenantContext(daemonActorId, tenantId)`, sau đó chạy `ProactiveAgentScanner` tạo `PROACTIVE_RISK_ALERT` trong `AIRecommendations` và gửi thông báo tới `assigneeId ?? task.CreatorId`.
  - Đồng thời bổ sung cờ cấu hình `AI:ProactiveDaemonEnabled` (mặc định bật `true` trên ứng dụng thật, cho phép tắt bộ đếm 15 giây trong kiểm thử tích hợp để tránh race condition).
