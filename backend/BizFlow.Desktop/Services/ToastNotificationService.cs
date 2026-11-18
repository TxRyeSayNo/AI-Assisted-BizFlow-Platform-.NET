using System.Windows.Threading;
using BizFlow.Desktop.Core;

namespace BizFlow.Desktop.Services;

public sealed class ToastNotificationService : ObservableObject
{
    private bool _isVisible;
    private string _title = "";
    private string _message = "";
    private string _toastKind = "INFO"; // SUCCESS, ERROR, WARNING, INFO
    private DispatcherTimer? _timer;

    public bool IsVisible { get => _isVisible; private set => SetProperty(ref _isVisible, value); }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string ToastKind { get => _toastKind; private set => SetProperty(ref _toastKind, value); }

    public void Success(string message, string title = "Thành công") => Show(title, message, "APPROVED");
    public void Error(string message, string title = "Thông báo lỗi") => Show(title, message, "ERROR");
    public void Warning(string message, string title = "Cảnh báo") => Show(title, message, "WARNING");
    public void Info(string message, string title = "Thông tin") => Show(title, message, "IN_PROGRESS");

    public void Dismiss()
    {
        _timer?.Stop();
        IsVisible = false;
    }

    private void Show(string title, string message, string kind)
    {
        Title = title;
        Message = message;
        ToastKind = kind;
        IsVisible = true;

        try
        {
            if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
            {
                dispatcher.InvokeAsync(() =>
                {
                    _timer?.Stop();
                    _timer = new DispatcherTimer(TimeSpan.FromSeconds(4.5), DispatcherPriority.Background, (_, _) =>
                    {
                        _timer?.Stop();
                        IsVisible = false;
                    }, dispatcher);
                    _timer.Start();
                });
            }
        }
        catch
        {
            // Ignore timer errors in headless unit test environments
        }
    }
}
