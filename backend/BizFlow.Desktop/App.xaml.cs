using System.IO;
using System.Windows;
using System.Windows.Threading;
using BizFlow.Desktop.Services;
using BizFlow.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BizFlow.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var services = new ServiceCollection();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<ToastNotificationService>();
        services.AddSingleton<BackendHostSupervisor>();
        services.AddSingleton<BizFlowApiClient>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "desktop-error.log"),
                $"[{DateTimeOffset.Now:O}] {e.Exception}\n");
        }
        catch
        {
            // Ignore logging failures
        }

        var toast = _serviceProvider?.GetService<ToastNotificationService>();
        if (toast is not null)
        {
            toast.Error(e.Exception.Message, "Lỗi giao diện không mong muốn");
            e.Handled = true;
        }
    }
}
