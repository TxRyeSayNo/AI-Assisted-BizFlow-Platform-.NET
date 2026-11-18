# 07. Nhật Ký Sửa Lỗi & Kiểm Chứng Hồi Quy (Fix & Regression Log)

Tài liệu ghi nhận chính xác lệnh chạy và kết quả quan sát trực tiếp sau **lần chỉnh sửa mã nguồn cuối cùng** (không có bất kỳ thay đổi code hoặc test nào giữa Round 1 và Round 2).

---

## 1. Môi trường Thực thi Kiểm thử
- **Hệ điều hành:** Windows + PowerShell (`net10.0` / `net10.0-windows`)
- **Database Kiểm thử Tích hợp:** PostgreSQL 18 cô lập tự động thông qua Testcontainers (`DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine`)
- **OpenAI API Key:** Sử dụng biến môi trường `PRN_KEY` cục bộ (không ghi giá trị khóa vào log hay mã nguồn)

---

## 2. Kết quả Regression Round 1 (Cấu hình `Debug` + Build `Debug`/`Release`)

1. **Build Solution (`Debug` & `Release`):**
   - Lệnh:
     ```powershell
     dotnet build BizFlow.sln -c Debug --no-restore
     dotnet build BizFlow.sln -c Release --no-restore
     ```
   - Kết quả `Debug`: `Build succeeded. 0 Warning(s), 0 Error(s). Time Elapsed 00:00:04.52`
   - Kết quả `Release`: `Build succeeded. 0 Warning(s), 0 Error(s). Time Elapsed 00:00:22.86`

2. **Full Unit & Desktop Test Suite (`BizFlow.UnitTests` - Debug):**
   - Lệnh:
     ```powershell
     dotnet test tests\BizFlow.UnitTests\BizFlow.UnitTests.csproj -c Debug --no-build
     ```
   - Kết quả thực tế: **`Passed! - Failed: 0, Passed: 579, Skipped: 0, Total: 579, Duration: 25 s`**

3. **Full Integration Suite loại trừ OpenAI thực (`BizFlow.IntegrationTests` - Debug):**
   - Lệnh:
     ```powershell
     $env:DOCKER_HOST='npipe://./pipe/dockerDesktopLinuxEngine'; dotnet test tests\BizFlow.IntegrationTests\BizFlow.IntegrationTests.csproj -c Debug --no-build --filter "Category!=RealOpenAi"
     ```
   - Kết quả thực tế: **`Passed! - Failed: 0, Passed: 308, Skipped: 0, Total: 308, Duration: 6 m 41 s`**

4. **Live OpenAI Smoke Test (`Category=RealOpenAi` - Debug):**
   - Lệnh:
     ```powershell
     $env:DOCKER_HOST='npipe://./pipe/dockerDesktopLinuxEngine'; dotnet test tests\BizFlow.IntegrationTests\BizFlow.IntegrationTests.csproj -c Debug --no-build --filter "Category=RealOpenAi"
     ```
   - Kết quả thực tế: **`Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 2 s`**

---

## 3. Kết quả Regression Round 2 (Cấu hình `Release`, giữ nguyên 100% mã nguồn)

1. **Full Unit & Desktop Test Suite (`BizFlow.UnitTests` - Release):**
   - Lệnh:
     ```powershell
     dotnet test tests\BizFlow.UnitTests\BizFlow.UnitTests.csproj -c Release --no-build
     ```
   - Kết quả thực tế: **`Passed! - Failed: 0, Passed: 579, Skipped: 0, Total: 579, Duration: 17 s`**

2. **Full Integration Suite loại trừ OpenAI thực (`BizFlow.IntegrationTests` - Release):**
   - Lệnh:
     ```powershell
     $env:DOCKER_HOST='npipe://./pipe/dockerDesktopLinuxEngine'; dotnet test tests\BizFlow.IntegrationTests\BizFlow.IntegrationTests.csproj -c Release --no-build --filter "Category!=RealOpenAi"
     ```
   - Kết quả thực tế: **`Passed! - Failed: 0, Passed: 308, Skipped: 0, Total: 308, Duration: 5 m 29 s`**

3. **Live OpenAI Smoke Test (`Category=RealOpenAi` - Release):**
   - Lệnh:
     ```powershell
     $env:DOCKER_HOST='npipe://./pipe/dockerDesktopLinuxEngine'; dotnet test tests\BizFlow.IntegrationTests\BizFlow.IntegrationTests.csproj -c Release --no-build --filter "Category=RealOpenAi"
     ```
   - Kết quả thực tế: **`Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 2 s`**

---

## 4. Ghi chú về Sự cố Môi trường Trước Khi Chốt Round 1
- Trong một lượt chạy nền dài trước đó (`task-1110`), hệ điều hành chuyển sang trạng thái nghỉ giữa chừng (~2 giờ) khiến `Request_routing_idempotency_replay_and_conflict` hết hạn JWT token (`401`) và lộ ra 3 lỗi cần hoàn thiện dứt điểm (`ProactiveAgentDaemon` race condition 15s, `20261111090436` rollback test vs `TaskDraftPersistenceTests`). Sau khi xử lý triệt để tất cả lỗi trên, cả hai vòng **Regression Round 1 (Debug)** và **Regression Round 2 (Release)** đều chạy thông suốt từ đầu đến cuối với `0` lỗi và `0` test bị bỏ qua (`888/888 Passed` mỗi vòng).
