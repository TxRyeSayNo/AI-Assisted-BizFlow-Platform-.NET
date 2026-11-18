@echo off
chcp 65001 >nul
echo ==============================================================================
echo       BIZFLOW PLATFORM - TỰ ĐỘNG KHỞI TẠO VÀ CHẠY HỆ THỐNG TRÊN MÁY MỚI
echo ==============================================================================
echo.

:: 1. Kiểm tra .NET SDK
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [LỖI] Chưa cài đặt .NET SDK!
    echo Vui lòng cài đặt .NET SDK 10 (từ Microsoft): https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

:: 2. Kiểm tra Docker
where docker >nul 2>nul
if %errorlevel% neq 0 (
    echo [LỖI] Chưa cài đặt Docker Desktop!
    echo Dự án cần Docker Desktop để chạy PostgreSQL 18, Redis và MinIO.
    echo Vui lòng tải và cài đặt Docker Desktop: https://www.docker.com/products/docker-desktop/
    pause
    exit /b 1
)

docker info >nul 2>nul
if %errorlevel% neq 0 (
    echo [CẢNH BÁO] Docker Desktop chưa được bật!
    echo Đang thử khởi động Docker Desktop...
    start "" "C:\Program Files\Docker\Docker\Docker Desktop.exe"
    echo Đang chờ Docker Desktop khởi động (khoảng 15-20 giây)...
    timeout /t 15 /nobreak >nul
)

:: 3. Khởi động Docker containers (PostgreSQL, Redis, MinIO)
echo.
echo [1/4] Đang khởi động cơ sở dữ liệu PostgreSQL 18, Redis, MinIO...
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d
if %errorlevel% neq 0 (
    echo [LỖI] Không thể bật Docker containers. Hãy đảm bảo Docker Desktop đang chạy ở chế độ Linux containers!
    pause
    exit /b 1
)

:: Chờ PostgreSQL sẵn sàng kết nối trên cổng 5433
echo Đang chờ PostgreSQL sẵn sàng kết nối...
set /a attempts=0
:wait_postgres
set /a attempts+=1
docker exec bizflow-local-postgres-1 pg_isready -U bizflow -d bizflow >nul 2>nul
if %errorlevel% neq 0 (
    if %attempts% geq 30 (
        echo [LỖI] Quá thời gian chờ PostgreSQL khởi động.
        pause
        exit /b 1
    )
    timeout /t 1 /nobreak >nul
    goto wait_postgres
)
echo [OK] PostgreSQL đã sẵn sàng!

:: 4. Khôi phục công cụ dotnet ef và áp dụng migrations
echo.
echo [2/4] Đang áp dụng Entity Framework Core Migrations lên database...
dotnet tool restore >nul 2>nul
dotnet ef database update --project backend\BizFlow.Infrastructure --startup-project backend\BizFlow.Infrastructure
if %errorlevel% neq 0 (
    echo [LỖI] Không thể áp dụng migrations!
    pause
    exit /b 1
)

:: 5. Nạp dữ liệu mẫu (Sample Demo Users) nếu database mới tinh
echo.
echo [3/4] Kiểm tra tài khoản người dùng mẫu...
docker exec -i bizflow-local-postgres-1 psql -U bizflow -d bizflow -c "SELECT count(*) FROM \"User\";" | findstr /r "[1-9][0-9]*" >nul 2>nul
if %errorlevel% neq 0 (
    echo Nạp dữ liệu người dùng mẫu (ADMIN001, MGR001, EMP001, SUPERADMIN)...
    docker exec -i bizflow-local-postgres-1 psql -U bizflow -d bizflow < deploy\seed_sample_data.sql >nul 2>nul
    echo [OK] Đã nạp thành công tài khoản mẫu!
) else (
    echo [OK] Cơ sở dữ liệu đã có sẵn tài khoản người dùng.
)

:: 6. Build và khởi chạy BizFlow
echo.
echo [4/4] Đang khởi chạy hệ thống BizFlow Platform...
echo • Backend API sẽ chạy tại: http://localhost:5154
echo • Giao diện Windows Desktop WPF sẽ hiển thị ngay sau đây.
echo.

:: Khởi chạy backend API ở background
start "BizFlow.Api (Backend)" cmd /c "dotnet run --project backend\BizFlow.Api"

:: Chờ API live
echo Đang đợi Backend API trực tuyến...
timeout /t 3 /nobreak >nul

:: Khởi chạy ứng dụng Desktop WPF
dotnet run --project backend\BizFlow.Desktop

echo.
echo Cảm ơn bạn đã sử dụng BizFlow Platform!
