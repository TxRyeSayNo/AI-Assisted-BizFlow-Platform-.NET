# 08. Đánh Giá Sẵn Sàng & Hướng Dẫn Demo Bảo Vệ (Final Readiness Assessment)

---

## 1. Kết luận Đánh giá Cuối cùng: **MVP WITH MINOR CAVEATS (Đạt chuẩn Demo / Nghiệm thu môn PRN212)**

Sau quá trình kiểm toán nghiệp vụ doanh nghiệp, sửa toàn diện từ tầng Database/Migration, Domain Policies, ASP.NET Core API, dịch vụ AI (`OpenAiModelClient` với `PRN_KEY` + `ProactiveAgentDaemon`) đến WPF Desktop MVVM và xác minh qua **2 vòng hồi quy đầy đủ (Debug & Release, 888/888 test cases passed, 0 failed, 0 skipped)**:

### Lý do Đạt chuẩn Demo Doanh nghiệp MVP
1. **Kiến trúc sạch và nhất quán .NET 10 / WPF / EF Core 10 / PostgreSQL 18:** Đã loại bỏ hoàn toàn tàn dư web/Node/Playwright không liên quan, build sạch `0` warnings / `0` errors ở cả `Debug` và `Release`.
2. **Nghiệp vụ cốt lõi hoạt động chính xác & khép kín:** Luồng Yêu cầu (`DRAFT -> SUBMITTED -> ROUTED -> IN_PROGRESS -> RESOLVED -> CLOSED`, `Rework`, `Reject`, `Revise`, `Cancel`, `Archive`) và luồng Công việc cha–con (`ParentTaskId`, Manager gate, tiến độ, nộp kết quả, xác nhận, thông báo đúng người xử lý, kiểm toán, idempotency, `409 Conflict` concurrency) đã được kiểm chứng thực tế trên PostgreSQL 18.
3. **Tích hợp OpenAI thực (`PRN_KEY`) có bảo vệ an toàn:** Desktop tự truyền `PRN_KEY` sang backend; `TaskReadPolicy` chặn nhân viên ngoài phòng ban đọc trộm qua AI (`403`); `ConfirmationToken` + `AiActionDispatcher` buộc con người xác nhận và tuân thủ State Machine; có heuristic fallback khi mất kết nối OpenAI.

### Các Giới hạn MVP (Caveats) Cần Trình bày Trung thực Nếu Giảng viên Hỏi
1. **Chưa thao tác GUI thủ công bằng chuột trên cửa sổ Windows:** Tầng WPF Desktop đã được kiểm chứng tự động ở mức STA XAML instantiation (`9` Views + `MainWindow`), kiểm thử logic ViewModel, và khởi chạy backend `BizFlow.Api` thật (`BackendHostSupervisor`); người dùng vẫn nên mở `BizFlow.Desktop` chạy thử 1 lượt trước giờ bảo vệ để kiểm tra hiển thị trực quan DPI/màn hình máy chiếu.
2. **Các nhánh tính năng MVP chưa triển khai hoàn chỉnh (không che giấu):**
   - Trạng thái `WAITING_FOR_INFORMATION` chưa có endpoint/nút bấm UI.
   - Chưa sửa trực tiếp nội dung Request khi còn ở `DRAFT` (hỗ trợ tạo mới hoặc `Revise` sau khi `REJECTED`).
   - Checklist của Task chưa hỗ trợ tick cập nhật hoàn thành từng dòng lúc chạy (DB chặn update `TaskChecklistItem`).
   - Gửi link đăng nhập qua Email chỉ sinh token trong cache, chưa gửi email SMTP thật.

---

## 2. Hướng dẫn Kịch bản Demo Trực tiếp (Student Demonstration Guide)

### 2.1 Chuẩn bị Môi trường
1. Đảm bảo Docker Desktop và các container PostgreSQL (`bizflow-local-postgres-1`, cổng `5433`) đang chạy và đã nạp dữ liệu mẫu từ `deploy/seed_sample_data.sql` (hoặc chạy `run.bat`).
2. Kiểm tra biến môi trường `PRN_KEY` đã được thiết lập (nếu muốn demo gọi OpenAI trực tiếp).
3. Khởi chạy `BizFlow.Desktop` (hoặc `run.bat` — Desktop sẽ tự khởi động `BizFlow.Api` thông qua `BackendHostSupervisor` và đợi `/health/live` + `/health/ready`).

### 2.2 Tài khoản Demo Chuẩn (`deploy/seed_sample_data.sql`, Tenant Key: `bizflow-corp`, Mật khẩu chung: `Password123!`)
| Mã Nhân viên | Email | Vai trò trong DB Seed | Mục đích Demo |
| :--- | :--- | :--- | :--- |
| **`EMP001`** | `employee@bizflow.local` | `EMPLOYEE` | Gửi Yêu cầu nội bộ mới, thực hiện công việc con, nhận thông báo, **tự xác nhận đóng (`CLOSED`)** yêu cầu của mình |
| **`MGR001`** | `manager@bizflow.local` | `MANAGER`, `EMPLOYEE` | Tiếp nhận/điều phối yêu cầu, tiếp nhận Task chính, phân rã **Task con (`ParentTaskId`)** cho nhân viên, xác nhận kết quả Task |
| **`ADMIN001`** | `admin@bizflow.local` | `COMPANY_ADMIN`, `MANAGER`, `EMPLOYEE` | Điều phối yêu cầu cấp phòng ban, giao Task cấp công ty cho Manager, sử dụng AI Đánh giá rủi ro / Lưu trữ (`Archive`) hồ sơ & xem Nhật ký kiểm toán (`Audit`) |

### 2.3 Kịch bản 5 Bước Gợi ý Trước Giảng viên (7–10 phút)
1. **Bước 1 – Đăng nhập & Bảo mật (`EMP001`):**
   - Mở `AuthView`, cho thấy ô mật khẩu được che bằng `PasswordBox`. Đăng nhập `EMP001` / `Password123!` / `bizflow-corp`.
   - Mở mục **Yêu cầu (`Requests`)**, tạo 1 yêu cầu hỗ trợ IT khẩn và chọn gửi ngay (`SUBMITTED`).
2. **Bước 2 – Điều phối thông minh có AI hỗ trợ (`ADMIN001` / `MGR001`):**
   - Đăng xuất `EMP001` -> đăng nhập `ADMIN001` (cho thấy giao diện làm sạch cache cũ ngay lập tức, Dashboard hiển thị KPI và menu theo quyền).
   - Chọn yêu cầu vừa gửi, bấm **Gợi ý điều phối AI** (gọi OpenAI qua `PRN_KEY`), xem đề xuất, rồi bấm **Điều phối (`Route`)** về phòng ban hoặc `MGR001`.
3. **Bước 3 – Điều phối xuống Công việc & Phân rã AI (`MGR001`):**
   - Đăng nhập `MGR001`, mở **Phê duyệt & Tiếp nhận (`Approvals`)** để thấy yêu cầu đã `ROUTED` (hoặc mở thẳng **Yêu cầu**) và bấm **Tiếp nhận (`Receive`)** / **Hoàn tất (`Resolve`)** sau khi xử lý.
   - Chuyển sang mục **Công việc (`Tasks`)**, tạo Task chính -> dùng **AI Phân rã / Đánh giá rủi ro**, giao Task chính cho `MGR001`, sau đó tạo Task con (`ParentTaskId`) giao cho `EMP001` (nhãn hiển thị rõ `FullName (Role · Department)`).
4. **Bước 4 – Nhân viên Thực thi, Quản lý Xác nhận & Người gửi Chốt đóng (`EMP001` & `MGR001`):**
   - `EMP001` đăng nhập -> nhận Task, cập nhật tiến độ & nộp kết quả; `MGR001` vào `Approvals` để **Xác nhận hoàn thành Task**.
   - Quay lại `EMP001` -> vào `Approvals` hoặc `Requests` để **Xác nhận đóng Yêu cầu (`CLOSED`)** *(giải thích với giảng viên: hệ thống bắt buộc chính người gửi gốc mới được nghiệm thu đóng yêu cầu, kể cả Admin cũng không được bấm thay)*.
5. **Bước 5 – Lưu trữ & Kiểm toán (`ADMIN001`):**
   - Đăng nhập `ADMIN001`, bấm **Lưu trữ (`Archive`)** hồ sơ vừa `CLOSED`, chứng minh khi tìm mặc định trong `Operations -> Records` sẽ ẩn đi và hiện lại khi bật tùy chọn bao gồm hồ sơ lưu trữ; mở tab **Audit Logs** để cho thấy toàn bộ dấu vết chuyển trạng thái & tương tác AI đều được lưu bất biến.
