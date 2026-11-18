# AI-Assisted BizFlow Platform (Visual Studio · WPF Desktop + ASP.NET Core API + EF Core + PostgreSQL)

An enterprise, multi-tenant work and service-request orchestration platform migrated to a native Windows Desktop Application (**WPF / .NET 10**) backed by a centralized **ASP.NET Core Web API (`BizFlow.Api`)**, **Application & Domain Layers (`BizFlow.Application`, `BizFlow.Domain`)**, **Entity Framework Core (`BizFlow.Infrastructure`)**, and **PostgreSQL 18**.

The approved v1.2 specification is [the project source of truth](AI-Assisted_BizFlow_Software_System_Specification.md). Architecture and development stages are in [docs/00-analysis](docs/00-analysis).

---

## 1. Visual Studio & Environment Requirements

- **IDE:** Microsoft Visual Studio 2026 (v18.10+) or Visual Studio 2022 (v17.12+ with .NET 10 SDK support)
- **Required Visual Studio Workloads:**
  - **.NET desktop development** (`Microsoft.VisualStudio.Workload.ManagedDesktop` — WPF support)
  - **ASP.NET and web development** (`Microsoft.VisualStudio.Workload.NetWeb`)
- **.NET SDK & Runtime:** `.NET SDK 10.0.401` (`global.json`), targeting `net10.0-windows` (`BizFlow.Desktop`) and `net10.0` (`BizFlow.Api`, `BizFlow.Application`, `BizFlow.Domain`, `BizFlow.Infrastructure`).
- **Database Engine:** PostgreSQL 18 (`127.0.0.1:5433`, container `bizflow-local-postgres-1` via `deploy/docker-compose.yml`).

---

## 2. Solution Architecture

```text
BizFlow.sln / BizFlow.slnx
├── backend/
│   ├── BizFlow.Desktop/          # WPF Desktop Client (net10.0-windows, MVVM, DI, Auto-Host Supervisor)
│   ├── BizFlow.Api/              # Centralized ASP.NET Core Web API (net10.0, 26 Controllers, RBAC, JWT)
│   ├── BizFlow.Application/      # Application Services, Use Cases, Validation & AI Orchestration
│   ├── BizFlow.Domain/           # Domain Entities, Value Objects, Business State Machines & Contracts
│   └── BizFlow.Infrastructure/   # EF Core 10.0.5 (BizFlowDbContext, 41 DbSets, 39 Migrations, Npgsql)
└── tests/
    ├── BizFlow.UnitTests/        # 575 Unit, WPF ViewModel/XAML STA, and Live End-to-End Desktop Tests
    └── BizFlow.IntegrationTests/ # 304 PostgreSQL 18 Testcontainers Integration & Migration Tests
```

**Multi-User Data Access Architecture:**
`BizFlow.Desktop (WPF MVVM Client)` → `BizFlow.Api (ASP.NET Core REST API)` → `BizFlow.Application` → `BizFlow.Infrastructure (EF Core 10)` → `PostgreSQL 18 Database`.

---

## 3. Running the Project (Chạy dự án)

### Cách nhanh nhất: Bấm đúp chạy `run.bat` (Hoặc mở Terminal gõ `.\run.bat`)
Tôi đã tạo sẵn file tự động [run.bat](file:///c:/Users/Admin/Desktop/AI-Assisted-BizFlow-Platform_VS/run.bat) (hoặc [setup-and-run.ps1](file:///c:/Users/Admin/Desktop/AI-Assisted-BizFlow-Platform_VS/setup-and-run.ps1)).
File này sẽ **tự động kiểm tra môi trường, bật Docker PostgreSQL, áp dụng EF Core Migrations, tự nạp dữ liệu mẫu demo nếu máy mới và khởi chạy cả Backend lẫn Desktop WPF** mà không cần gõ lệnh thủ công.

---

### Cách chạy trong Visual Studio (Phím F5)

1. **Open the Solution in Visual Studio:**
   Open **`BizFlow.sln`** (or **`BizFlow.slnx`**) in Microsoft Visual Studio.
2. **Startup Project Configuration (Pre-Configured Out of the Box):**
   - **`BizFlow.Desktop`** is listed as the first executable project in `BizFlow.sln` and `BizFlow.slnx`, making it the default Startup Project automatically.
   - **`BizFlow.slnLaunch`** also provides two launch profiles in Visual Studio:
     - **`Full Stack (BizFlow.Api + BizFlow.Desktop WPF)`**: Starts both `BizFlow.Api` and `BizFlow.Desktop` simultaneously in the Visual Studio debugger.
     - **`BizFlow.Desktop WPF Only (Auto-Supervises Backend)`**: Starts `BizFlow.Desktop`, whose built-in `BackendHostSupervisor` automatically verifies Docker PostgreSQL (`127.0.0.1:5433`) and auto-starts `BizFlow.Api` (`http://localhost:5154`) in the background if it is not already running.
3. **Press `F5` (Start Debugging) or `Ctrl+F5` (Start Without Debugging):**
   - The WPF Desktop window (`MainWindow`) opens immediately.
   - The bottom-left/top-bar live health indicator connects to `http://localhost:5154/health/ready` (`Hệ thống sẵn sàng · Đã kết nối PostgreSQL & EF Core`).
   - Use the **1-Click Quick Login** buttons on the login screen (or enter credentials manually) to sign in as any role:

| Role | Tenant Key | Identifier | Password |
|---|---|---|---|
| **Company Admin** | `bizflow-corp` | `ADMIN001` (`admin@bizflow.local`) | `Password123!` |
| **Department Manager** | `bizflow-corp` | `MGR001` (`manager@bizflow.local`) | `Password123!` |
| **Employee** | `bizflow-corp` | `EMP001` (`employee@bizflow.local`) | `Password123!` |
| **Platform Admin** | *(leave empty)* | `SUPERADMIN` (`platform@bizflow.local`) | `Password123!` |

---

## 4. Database & EF Core Migration Commands

Local infrastructure (PostgreSQL 18 on `127.0.0.1:5433`, Redis on `127.0.0.1:6379`, MinIO on `127.0.0.1:9000`) is defined in `deploy/docker-compose.yml`:

```powershell
# 1. Start local PostgreSQL, Redis, and MinIO containers
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d

# 2. Apply all 39 EF Core migrations to PostgreSQL
dotnet tool restore
dotnet ef database update --project backend/BizFlow.Infrastructure --startup-project backend/BizFlow.Infrastructure
```

Default local development connection string and JWT settings are pre-configured in `backend/BizFlow.Api/appsettings.json` and `backend/BizFlow.Api/appsettings.Development.json`:
- `ConnectionStrings:BizFlow`: `Host=127.0.0.1;Port=5433;Database=bizflow;Username=bizflow;Password=bizflow_secret_123`
- `API Base URL`: `http://localhost:5154` (`/health/live`, `/health/ready`, `/openapi/v1.json`)

---

## 5. Build & Test Verification Commands

From the repository root (`C:\Users\Admin\Desktop\AI-Assisted-BizFlow-Platform_VS`):

```powershell
# 1. Restore NuGet packages and verify lockfiles
dotnet restore BizFlow.sln --locked-mode

# 2. Build entire solution in Debug and Release (0 Warnings, 0 Errors)
dotnet build BizFlow.sln -c Debug --no-restore
dotnet build BizFlow.sln -c Release --no-restore

# 3. Run Unit, WPF ViewModel/XAML STA, and Live End-to-End Desktop Tests (575 tests)
dotnet test tests/BizFlow.UnitTests/BizFlow.UnitTests.csproj

# 4. Run PostgreSQL 18 Testcontainers Integration Tests (304 tests)
$env:DOCKER_HOST='npipe://./pipe/dockerDesktopLinuxEngine'
dotnet test tests/BizFlow.IntegrationTests/BizFlow.IntegrationTests.csproj
```

---

## 6. Reference Documentation

- [Software System Specification (v1.2)](AI-Assisted_BizFlow_Software_System_Specification.md)
- [Current Implementation Baseline](docs/project-audit/CURRENT_IMPLEMENTATION_BASELINE.md)
- [Operational Readiness Contract](docs/02-traceability/operational-readiness.md)
- [Authentication Slice](docs/02-traceability/authentication-slice.md)
- [Password Recovery Slice](docs/02-traceability/password-reset-slice.md)
