using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using BizFlow.Desktop.Core;

namespace BizFlow.Desktop.Services;

public sealed class BackendHostSupervisor : ObservableObject, IDisposable
{
    private readonly HttpClient _probeClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private Process? _spawnedApiProcess;
    private bool _isOnline;
    private bool _isReady;
    private string _statusMessage = "Đang kiểm tra kết nối máy chủ BizFlow API...";

    public string BaseUrl { get; } = Environment.GetEnvironmentVariable("BIZFLOW_API_URL") ?? "http://localhost:5154";

    public bool IsOnline
    {
        get => _isOnline;
        private set => SetProperty(ref _isOnline, value);
    }

    public bool IsReady
    {
        get => _isReady;
        private set => SetProperty(ref _isReady, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task<bool> EnsureBackendRunningAsync(CancellationToken cancellationToken = default)
    {
        // 1. Check if BizFlow.Api is already listening (e.g., started by Visual Studio Multiple Startup Projects)
        if (await ProbeLiveAsync(cancellationToken))
        {
            await ProbeReadyAsync(cancellationToken);
            return true;
        }

        StatusMessage = "Đang khởi động dịch vụ dữ liệu và BizFlow Backend API...";

        // 2. Ensure PostgreSQL / Redis / MinIO Docker containers are started if port 5433 is not listening
        await EnsureDatabaseContainerStartedAsync(cancellationToken);

        // 3. Wait briefly in case Visual Studio Multiple Startup Projects is currently booting BizFlow.Api
        for (var i = 0; i < 4; i++)
        {
            if (await ProbeLiveAsync(cancellationToken))
            {
                if (await ProbeReadyAsync(cancellationToken))
                {
                    return true;
                }
            }
            if (Process.GetProcessesByName("BizFlow.Api").Length > 0)
            {
                await Task.Delay(750, cancellationToken);
            }
            else
            {
                break;
            }
        }

        // 4. If BizFlow.Api is still not running, launch it automatically in the background
        if (_spawnedApiProcess is null || _spawnedApiProcess.HasExited)
        {
            TrySpawnBackendApiProcess();
        }

        // 5. Poll until /health/live and /health/ready respond 200 OK
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (await ProbeLiveAsync(cancellationToken))
            {
                if (await ProbeReadyAsync(cancellationToken))
                {
                    return true;
                }
            }
            await Task.Delay(500, cancellationToken);
        }

        if (await ProbeReadyAsync(cancellationToken))
        {
            return true;
        }

        StatusMessage = "Không thể kết nối hoặc khởi chạy BizFlow.Api trên cổng 5154.";
        return false;
    }

    public async Task<bool> ProbeLiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _probeClient.GetAsync($"{BaseUrl}/health/live", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                IsOnline = true;
                StatusMessage = $"Máy chủ BizFlow API trực tuyến ({BaseUrl})";
                return true;
            }
        }
        catch
        {
            // Ignore probe exceptions
        }

        IsOnline = false;
        return false;
    }

    public async Task<bool> ProbeReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _probeClient.GetAsync($"{BaseUrl}/health/ready", cancellationToken);
            IsReady = response.IsSuccessStatusCode;
            if (IsReady)
            {
                IsOnline = true;
                StatusMessage = $"Hệ thống sẵn sàng · Đã kết nối PostgreSQL & EF Core ({BaseUrl})";
            }
            return IsReady;
        }
        catch
        {
            IsReady = false;
            return false;
        }
    }

    private static async Task EnsureDatabaseContainerStartedAsync(CancellationToken cancellationToken)
    {
        if (IsPortListening("127.0.0.1", 5433))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "start bizflow-local-postgres-1 bizflow-local-redis-1 bizflow-local-minio-1",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                await proc.WaitForExitAsync(cancellationToken);
            }
        }
        catch
        {
            // Ignore if docker CLI is not on PATH
        }
    }

    private static bool IsPortListening(string host, int port)
    {
        try
        {
            using var tcp = new TcpClient();
            var task = tcp.ConnectAsync(host, port);
            return task.Wait(TimeSpan.FromMilliseconds(400)) && tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    private void TrySpawnBackendApiProcess()
    {
        try
        {
            var repoRoot = FindRepositoryRoot();
            if (repoRoot is null) return;

            var apiProjectDir = Path.Combine(repoRoot, "backend", "BizFlow.Api");
            if (!Directory.Exists(apiProjectDir)) return;

            var debugExe = Path.Combine(apiProjectDir, "bin", "Debug", "net10.0", "BizFlow.Api.exe");
            var releaseExe = Path.Combine(apiProjectDir, "bin", "Release", "net10.0", "BizFlow.Api.exe");

            ProcessStartInfo psi;
            if (File.Exists(debugExe))
            {
                psi = new ProcessStartInfo
                {
                    FileName = debugExe,
                    WorkingDirectory = apiProjectDir,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
            }
            else if (File.Exists(releaseExe))
            {
                psi = new ProcessStartInfo
                {
                    FileName = releaseExe,
                    WorkingDirectory = apiProjectDir,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{Path.Combine(apiProjectDir, "BizFlow.Api.csproj")}\" --no-launch-profile",
                    WorkingDirectory = apiProjectDir,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
            }

            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            psi.Environment["ASPNETCORE_URLS"] = BaseUrl;
            psi.Environment["ConnectionStrings__BizFlow"] =
                Environment.GetEnvironmentVariable("ConnectionStrings__BizFlow")
                ?? "Host=127.0.0.1;Port=5433;Database=bizflow;Username=bizflow;Password=bizflow_secret_123";
            psi.Environment["Jwt__Issuer"] = "BizFlow";
            psi.Environment["Jwt__Audience"] = "BizFlow.Web";
            psi.Environment["Jwt__SigningKeyBase64"] = "Loizdg9h99sl7lZJSV1LJwNCNTIdi7PjAtR3yib+brk=";

            var prnKey = Environment.GetEnvironmentVariable("PRN_KEY");
            if (!string.IsNullOrWhiteSpace(prnKey))
            {
                psi.Environment["PRN_KEY"] = prnKey;
                psi.Environment["OpenAI__ApiKey"] = prnKey;
            }

            _spawnedApiProcess = Process.Start(psi);
        }
        catch
        {
            // Ignore background spawn failure
        }
    }

    private static string? FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BizFlow.slnx")) ||
                File.Exists(Path.Combine(dir.FullName, "BizFlow.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "backend", "BizFlow.Api")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    public void Dispose()
    {
        try
        {
            if (_spawnedApiProcess is { HasExited: false })
            {
                _spawnedApiProcess.Kill(entireProcessTree: true);
                _spawnedApiProcess.Dispose();
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        _probeClient.Dispose();
    }
}
