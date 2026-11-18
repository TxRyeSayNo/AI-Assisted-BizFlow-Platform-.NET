using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using BizFlow.Desktop.ViewModels;

namespace BizFlow.Desktop.Views;

public partial class AuthView : UserControl
{
    private AuthViewModel? _boundVm;
    private bool _syncingPassword;

    public AuthView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => SyncPasswordFromViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _boundVm = DataContext as AuthViewModel;
        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged += OnViewModelPropertyChanged;
            SyncPasswordFromViewModel();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuthViewModel.Password))
        {
            SyncPasswordFromViewModel();
        }
    }

    private void SyncPasswordFromViewModel()
    {
        if (_boundVm is null || _syncingPassword) return;
        var vmPwd = _boundVm.Password ?? "";
        if (LoginPasswordBox.Password != vmPwd)
        {
            _syncingPassword = true;
            LoginPasswordBox.Password = vmPwd;
            _syncingPassword = false;
        }
    }

    private void OnLoginPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_boundVm is null || _syncingPassword) return;
        _syncingPassword = true;
        _boundVm.Password = LoginPasswordBox.Password;
        _syncingPassword = false;
    }
}
