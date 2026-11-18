# 02. Kiểm Toán Nghiệp Vụ Doanh Nghiệp (Business Domain Audit)

Báo cáo đánh giá tính đúng đắn của quy trình vận hành thực tế doanh nghiệp trực tiếp trên Domain Policies (`RequestLifecyclePolicy`, `TaskLifecyclePolicy`, `TaskWorkflowEngine`, `RequestReadPolicy`, `TaskReadPolicy`), Application Commands và phân quyền Seed/Migrations.

---

## 1. Ma trận Vai trò & Trách nhiệm thực tế (RBAC & Scope)

### 1.1 Sự khác biệt quan trọng giữa EF Migration gốc và Dữ liệu mẫu (`seed_sample_data.sql`)
- **Theo EF Migrations mặc định:**
  - `EMPLOYEE`: Gửi yêu cầu nội bộ, xử lý công việc được giao, bình luận, xác nhận kết quả **yêu cầu do chính mình tạo** (`requests.confirm` kiểm tra quyền sở hữu `IsRequester`).
  - `MANAGER`: Nắm các quyền `tasks.create`, `tasks.assign`, `tasks.confirm`, `requests.route`, `requests.receive`, phân rã task trong phạm vi `ManagementScope` phòng ban quản lý.
  - `COMPANY_ADMIN` (đứng độc lập theo migration): Nắm quyền quản trị công ty, người dùng, phòng ban, dịch vụ, audit, archival — **không mặc định có `tasks.create` / `tasks.assign` nếu không gán kèm vai trò `MANAGER`**.
- **Theo `deploy/seed_sample_data.sql` (môi trường demo mặc định):**
  - `ADMIN001` được gán đồng thời cả 3 vai trò `COMPANY_ADMIN`, `MANAGER`, `EMPLOYEE`, kèm `ManagementScope` trên phòng `TECH`.
  - `MGR001` được gán `MANAGER` + `EMPLOYEE`, kèm `ManagementScope`.
  - `EMP001` được gán `EMPLOYEE`.
  - Ngoài ra script demo còn cấp bổ sung `tasks.create` cho `COMPANY_ADMIN`, `MANAGER`, `EMPLOYEE` và `tasks.assign` cho `COMPANY_ADMIN`, `MANAGER`.

---

## 2. State Machine Yêu cầu Nội bộ (`WorkRequest` & `RequestLifecyclePolicy`)

### 2.1 Luồng chuyển trạng thái hợp lệ được mã nguồn thực thi
1. **Khởi tạo (`DRAFT` hoặc `SUBMITTED` nếu `submitImmediately = true`):** Bất kỳ nhân viên `ACTIVE` nào trong tenant có quyền tạo yêu cầu đều có thể gửi.
2. **Gửi duyệt (`DRAFT -> SUBMITTED`):** Chỉ người gửi hoặc người có quyền thực hiện trong tenant.
3. **Điều phối (`SUBMITTED -> ROUTED`):** Qua `RequestRoutingCommand` tới một `DepartmentId` cụ thể và/hoặc `UserId` phụ trách.
   - *Lỗi nghiêm trọng đã sửa:* Khi điều phối đến phòng ban có từ 2 quản lý trở lên (`ToUserId == null`), `RequestRoutingCommand.cs:157` trước đây dùng chung một `IdempotencyKey` (`"request-route/{routing.Id:N}"`) khiến PostgreSQL ném lỗi `23505` vi phạm unique index `IX_Notification_TenantId_IdempotencyKey` (`HTTP 500`). Đã sửa thành `$"request-route/{routing.Id:N}/{recipientId:N}"`.
4. **Tiếp nhận & Bắt đầu xử lý (`ROUTED -> IN_PROGRESS`):**
   - Hỗ trợ tiếp nhận qua `RequestReceiptCommand` (`POST /api/v1/requests/{id}/receive`, giữ/chuyển sang `IN_PROGRESS`) hoặc `RequestProcessingCommand` (`POST /api/v1/requests/{id}/start`, `resolve`).
5. **Hoàn tất xử lý (`IN_PROGRESS -> RESOLVED`):** Bộ phận/nhân sự xử lý ghi nhận `RequestResolution` kèm nội dung kết quả.
6. **Xác nhận đóng / Yêu cầu làm lại (`RESOLVED -> CLOSED` hoặc `RESOLVED -> IN_PROGRESS`):**
   - `RequestLifecyclePolicy` và `RequestConfirmationCommand` **cưỡng chế `IsRequester == true`** (chỉ người tạo yêu cầu gốc mới được xác nhận hài lòng để đóng `CLOSED` hoặc yêu cầu làm lại — ngay cả `COMPANY_ADMIN` hoặc `MANAGER` không phải người gửi cũng bị chặn `403 Forbidden`).
   - *Lỗi nghiêm trọng đã sửa:* Khi yêu cầu được điều phối theo phòng ban (`ToUserId == null`), trước đây `RequestConfirmationCommand` fallback sang gửi thông báo cho `actorId` (tức chính người gửi yêu cầu đang bấm confirm). Đã bổ sung `LatestResolverId` trong `RequestConfirmationStore.cs` để truy từ `db.RequestResolutions`, bảo đảm thông báo đóng/làm lại đến đúng nhân sự đã xử lý.
7. **Từ chối (`SUBMITTED` hoặc `ROUTED` -> `REJECTED`):**
   - `RequestLifecyclePolicy` chỉ cho phép từ chối ở `SUBMITTED` hoặc `ROUTED` kèm lý do bắt buộc (không cho phép từ chối ngang khi đang `IN_PROGRESS` hay đã `RESOLVED`).
   - Trạng thái `REJECTED` là điểm cuối của bản ghi cũ; để gửi lại, người dùng dùng chức năng **Tạo bản sửa đổi (`Revise`)** (`POST /api/v1/requests/{id}/revise`) tạo một bản ghi yêu cầu liên kết mới.
8. **Hủy (`CANCELLED`) & Lưu trữ (`Archive`):** Hỗ trợ hủy ở các trạng thái chưa kết thúc và lưu trữ hồ sơ sau khi kết thúc thông qua `RecordsController`.

### 2.2 Vấn đề đồng bộ UI Desktop đã được xử lý theo đúng State Machine
- Trước kiểm toán, `RequestsViewModel` cho phép bấm **Từ chối** khi yêu cầu đã `IN_PROGRESS`/`RESOLVED` (dẫn tới lỗi `409`), cho phép bấm **Xác nhận đóng** dù không phải người gửi gốc (lỗi `403`), chưa có nút **Gửi lại bản sửa (`Revise`)** và **Lưu trữ (`Archive`)**, và `ApprovalsViewModel` nạp cả yêu cầu `SUBMITTED` vào hàng chờ **Tiếp nhận (`Receive`)** (luôn lỗi `409` vì API đòi hỏi `ROUTED`). Toàn bộ các lệch pha này đã được hiệu chỉnh chính xác theo `RequestLifecyclePolicy`.

---

## 3. State Machine Công việc & Phân cấp Task (`WorkTask` & `TaskWorkflowEngine`)

### 3.1 Quy trình và Phân cấp Quản lý - Nhân viên
1. **Tạo công việc chính (`DRAFT`):** Người tạo tạo công việc kèm danh sách kiểm tra ban đầu (`checklist`) và hạn chót (`Deadline`).
2. **Giao việc theo cấp (`DRAFT -> ASSIGNED`):**
   - `TaskAssignmentCommand` chặn cấp quản lý/admin giao trực tiếp công việc gốc cấp công ty cho nhân viên bình thường không có vai trò quản lý (`TASK.MANAGER_ASSIGNMENT_REQUIRED`, trả về `422 UnprocessableEntity`), yêu cầu giao qua Manager phụ trách phòng ban.
   - Manager sau đó tạo **Công việc con (`ParentTaskId`)** và giao trực tiếp cho nhân viên thực hiện trong `ManagementScope`.
3. **Nhận / Từ chối thực hiện (`ASSIGNED -> ACCEPTED` / `REJECTED`):** Nhân viên được giao tự xác nhận nhận việc hoặc từ chối có lý do.
4. **Thực thi & Báo cáo tiến độ (`ACCEPTED -> IN_PROGRESS -> SUBMITTED`):** Cập nhật báo cáo tiến độ, nộp kết quả kèm bằng chứng.
5. **Xác nhận hoàn thành hoặc Trả lại làm lại (`SUBMITTED -> COMPLETED` / `IN_PROGRESS`):**
   - *Ghi nhận hành vi thực tế từ mã nguồn:* `TaskWorkflowEngine.ConfirmResult` kiểm tra quyền `tasks.confirm` trong tenant/scope, **không cấm cứng** việc quản lý tự xác nhận một công việc mà chính họ đứng tên nhận (`TaskResultAndConfirmationApiTests` cũng đã kiểm thử tình huống này). Đánh giá mức độ chấp nhận được ở MVP vì doanh nghiệp nhỏ quản lý có thể tự tạo việc theo dõi và tự đóng, nhưng cần ghi rõ trong tài liệu phân quyền.

---

## 4. Điểm chưa hoàn thiện về Nghiệp vụ (Ghi nhận trung thực)

| Hạng mục | Thực trạng trong mã nguồn | Ảnh hưởng tới Demo / Vận hành |
| :--- | :--- | :--- |
| Chờ bổ sung thông tin (`WAITING_FOR_INFORMATION`) | Chỉ có trong enum/policy domain, chưa có Command, Controller hay nút UI | Không thể tạm dừng yêu cầu để hỏi ngược người gửi rồi tiếp tục; cần giao tiếp qua Comments trước khi từ chối hoặc xử lý |
| Chỉnh sửa yêu cầu nháp (`DRAFT`) | Có tạo nháp và gửi nháp (`Submit`), chưa có API `PUT`/`PATCH` sửa tiêu đề/mô tả của yêu cầu `DRAFT` | Nếu gõ sai ở bản nháp, người dùng nên hủy và tạo yêu cầu mới |
| Đánh dấu hoàn thành từng dòng Checklist trong Task | `BizFlowDbContext.SaveChangesAsync()` chặn `Modified` trên `TaskChecklistItem` (`Unimplemented edit`) | Danh sách checklist hiển thị được khi tạo Task nhưng chưa tick trạng thái từng dòng trong runtime |
| Gửi email thật khi đăng nhập qua Email Link | `EmailLinkAuthenticationService` tạo token trên cache, không gọi SMTP | Không gửi email ra ngoài; luồng đăng nhập chuẩn demo dùng EmployeeCode/Email + Mật khẩu |
