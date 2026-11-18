# BizFlow Infrastructure Deployment & Local Services

This directory manages the local dependencies and infrastructure for the BizFlow Platform:
- **PostgreSQL 18** (`deploy/docker-compose.yml`) — Database engine (listening on port 5433 or 5432).
- **Redis 7** — Cache & distributed state.
- **MinIO** — S3-compatible evidence & attachment object storage.
- **API Dockerfile** (`deploy/api.Dockerfile`) — Multi-stage production container build for `BizFlow.Api` and EF Core migrations.

---

## 1. Quick Start via Automated Script (Recommended)

To start the infrastructure, apply migrations, seed sample data, and launch the application:

```cmd
:: Run from repository root
run.bat
```
Or via PowerShell:
```powershell
.\setup-and-run.ps1
```

---

## 2. Manual Docker Services Startup

1. Copy `.env.example` to `.env` if not already present:
   ```powershell
   Copy-Item deploy/.env.example deploy/.env
   ```
2. Start the local PostgreSQL, Redis, and MinIO services:
   ```powershell
   docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d
   ```
3. Apply Entity Framework Core Migrations:
   ```powershell
   dotnet tool restore
   dotnet ef database update --project backend/BizFlow.Infrastructure --startup-project backend/BizFlow.Infrastructure
   ```
4. Optional: Seed initial test accounts (ADMIN001, MGR001, EMP001, SUPERADMIN):
   ```powershell
   Get-Content deploy/seed_sample_data.sql | docker exec -i bizflow-local-postgres-1 psql -U bizflow -d bizflow
   ```
5. Run the Backend API:
   ```powershell
   dotnet run --project backend/BizFlow.Api
   ```
6. Run the WPF Desktop Application:
   ```powershell
   dotnet run --project backend/BizFlow.Desktop
   ```
