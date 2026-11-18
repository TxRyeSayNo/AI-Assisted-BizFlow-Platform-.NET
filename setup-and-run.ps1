# ==============================================================================
# BIZFLOW PLATFORM - TỰ ĐỘNG KHỞI TẠO VÀ CHẠY HỆ THỐNG TRÊN MÁY MỚI (POWERSHELL)
# ==============================================================================

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "      BIZFLOW PLATFORM - TỰ ĐỘNG KHỞI TẠO VÀ CHẠY HỆ THỐNG TRÊN MÁY MỚI        " -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Kiểm tra .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[LỖI] Máy chưa cài đặt .NET SDK!" -ForegroundColor Red
    Write-Host "Vui lòng cài đặt .NET SDK 10 tại: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Yellow
    exit 1
}

# 2. Kiểm tra Docker
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "[LỖI] Máy chưa cài đặt Docker Desktop!" -ForegroundColor Red
    Write-Host "Dự án cần Docker Desktop để chạy PostgreSQL 18, Redis và MinIO." -ForegroundColor Yellow
    Write-Host "Tải Docker Desktop tại: https://www.docker.com/products/docker-desktop/" -ForegroundColor Yellow
    exit 1
}

# 3. Khởi động Docker containers
Write-Host "[1/4] Đang khởi động cơ sở dữ liệu PostgreSQL 18, Redis, MinIO..." -ForegroundColor Green
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d
if ($LASTEXITCODE -ne 0) {
    Write-Host "[LỖI] Không thể bật Docker containers. Hãy đảm bảo Docker Desktop đang chạy!" -ForegroundColor Red
    exit 1
}

# Chờ PostgreSQL sẵn sàng
Write-Host "Đang chờ PostgreSQL sẵn sàng..." -ForegroundColor Gray
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    $res = docker exec bizflow-local-postgres-1 pg_isready -U bizflow -d bizflow 2>&1
    if ($LASTEXITCODE -eq 0) {
        $ready = $true
        break
    }
    Start-Sleep -Seconds 1
}
if (-not $ready) {
    Write-Host "[LỖI] Quá thời gian chờ PostgreSQL!" -ForegroundColor Red
    exit 1
}
Write-Host "[OK] PostgreSQL đã sẵn sàng!" -ForegroundColor Green

# 4. Áp dụng Migrations
Write-Host ""
Write-Host "[2/4] Đang áp dụng Entity Framework Core Migrations..." -ForegroundColor Green
dotnet tool restore | Out-Null
dotnet ef database update --project backend/BizFlow.Infrastructure --startup-project backend/BizFlow.Infrastructure
if ($LASTEXITCODE -ne 0) {
    Write-Host "[LỖI] Không thể áp dụng migrations!" -ForegroundColor Red
    exit 1
}

# 5. Nạp dữ liệu mẫu nếu DB trống
Write-Host ""
Write-Host "[3/4] Kiểm tra tài khoản người dùng mẫu..." -ForegroundColor Green
$userCheck = 'SELECT count(*) FROM "User";' | docker exec -i bizflow-local-postgres-1 psql -U bizflow -d bizflow -t 2>&1
$userCount = 0
if ($userCheck -match '\d+') {
    $userCount = [int]$matches[0]
}

if ($userCount -eq 0) {
    Write-Host "Cơ sở dữ liệu mới tinh -> Nạp dữ liệu người dùng mẫu (ADMIN001, MGR001, EMP001, SUPERADMIN)..." -ForegroundColor Yellow
    Get-Content deploy/seed_sample_data.sql | docker exec -i bizflow-local-postgres-1 psql -U bizflow -d bizflow | Out-Null
    Write-Host "[OK] Đã nạp thành công tài khoản mẫu!" -ForegroundColor Green
} else {
    Write-Host "[OK] Cơ sở dữ liệu đã có sẵn $userCount tài khoản người dùng." -ForegroundColor Green
}

# 6. Khởi chạy
Write-Host ""
Write-Host "[4/4] Khởi chạy hệ thống BizFlow..." -ForegroundColor Green
Write-Host "• Backend API chạy tại: http://localhost:5154" -ForegroundColor Cyan
Write-Host "• Ứng dụng Desktop WPF đang khởi động..." -ForegroundColor Cyan

Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run --project backend/BizFlow.Api"
Start-Sleep -Seconds 3
dotnet run --project backend/BizFlow.Desktop
