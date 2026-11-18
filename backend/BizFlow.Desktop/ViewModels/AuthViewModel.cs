using System.Collections.ObjectModel;
using System.IO;
using BizFlow.Desktop.Core;
using BizFlow.Desktop.Models;
using BizFlow.Desktop.Services;
using Microsoft.Win32;

namespace BizFlow.Desktop.ViewModels;

public sealed class AuthViewModel : ObservableObject
{
    private readonly BizFlowApiClient _api;
    private readonly SessionManager _session;
    private readonly ToastNotificationService _toast;

    // Modes: "Login", "EmailLink", "ForgotPassword", "ResetPassword", "RegisterCompany"
    private string _activeScreen = "Login";
    private bool _isBusy;
    private string _errorMessage = "";
    private string _successMessage = "";

    // Password Login fields
    private string _identifier = "ADMIN001";
    private string _password = "Password123!";
    private string _tenantKey = "bizflow-corp";
    private bool _isSuperAdminIdentifier;

    // Email Link fields
    private string _emailLinkAddress = "";
    private string _emailLinkToken = "";
    private string? _workspaceTicket;
    private WorkspaceCandidateModel? _selectedWorkspaceCandidate;

    // Forgot / Reset Password fields
    private string _resetIdentifier = "";
    private string _resetTenantKey = "bizflow-corp";
    private string _resetToken = "";
    private string _newPassword = "";

    // Register Company fields
    private string _regTaxId = "";
    private string _regCompanyName = "";
    private string _regWorkspaceCode = "";
    private string _regContactPerson = "";
    private string _regContactPhone = "";
    private string _regContactEmail = "";
    private string? _regEvidenceKey;
    private string? _regEvidenceFileName;
    private string _codeCheckFeedback = "";
    private bool? _isCodeAvailable;
    private SubmitCompanyRegistrationResponseDto? _registrationResult;

    public AuthViewModel(BizFlowApiClient api, SessionManager session, ToastNotificationService toast)
    {
        _api = api;
        _session = session;
        _toast = toast;

        WorkspaceCandidates = [];

        LoginCommand = new AsyncRelayCommand(ExecuteLoginAsync, () => !IsBusy);
        QuickLoginCommand = new AsyncRelayCommand(p => ExecuteQuickLoginAsync(p?.ToString() ?? "ADMIN"), _ => !IsBusy);
        SwitchScreenCommand = new RelayCommand(p => SwitchScreen(p?.ToString() ?? "Login"));

        RequestEmailLinkCommand = new AsyncRelayCommand(ExecuteRequestEmailLinkAsync, () => !IsBusy);
        VerifyEmailLinkCommand = new AsyncRelayCommand(ExecuteVerifyEmailLinkAsync, () => !IsBusy);
        ConfirmWorkspaceSelectionCommand = new AsyncRelayCommand(ExecuteSelectWorkspaceAsync, () => !IsBusy);

        ForgotPasswordCommand = new AsyncRelayCommand(ExecuteForgotPasswordAsync, () => !IsBusy);
        ResetPasswordCommand = new AsyncRelayCommand(ExecuteResetPasswordAsync, () => !IsBusy);

        GenerateCodeCommand = new AsyncRelayCommand(ExecuteGenerateCodeAsync, () => !IsBusy);
        CheckCodeCommand = new AsyncRelayCommand(ExecuteCheckCodeAsync, () => !IsBusy);
        UploadEvidenceDocumentCommand = new AsyncRelayCommand(ExecuteUploadEvidenceAsync, () => !IsBusy);
        SubmitCompanyRegistrationCommand = new AsyncRelayCommand(ExecuteSubmitRegistrationAsync, () => !IsBusy);
    }

    public ObservableCollection<WorkspaceCandidateModel> WorkspaceCandidates { get; }

    public string ActiveScreen
    {
        get => _activeScreen;
        set
        {
            if (SetProperty(ref _activeScreen, value))
            {
                ErrorMessage = "";
                SuccessMessage = "";
            }
        }
    }

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public string SuccessMessage { get => _successMessage; set => SetProperty(ref _successMessage, value); }

    public string Identifier
    {
        get => _identifier;
        set
        {
            if (SetProperty(ref _identifier, value))
            {
                var trimmed = (value ?? "").Trim();
                IsSuperAdminIdentifier =
                    trimmed.Equals("SUPERADMIN", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("platform@bizflow.local", StringComparison.OrdinalIgnoreCase);
                if (IsSuperAdminIdentifier)
                {
                    TenantKey = "";
                }
            }
        }
    }

    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string TenantKey { get => _tenantKey; set => SetProperty(ref _tenantKey, value); }
    public bool IsSuperAdminIdentifier { get => _isSuperAdminIdentifier; private set => SetProperty(ref _isSuperAdminIdentifier, value); }

    public string EmailLinkAddress { get => _emailLinkAddress; set => SetProperty(ref _emailLinkAddress, value); }
    public string EmailLinkToken { get => _emailLinkToken; set => SetProperty(ref _emailLinkToken, value); }
    public WorkspaceCandidateModel? SelectedWorkspaceCandidate { get => _selectedWorkspaceCandidate; set => SetProperty(ref _selectedWorkspaceCandidate, value); }

    public string ResetIdentifier { get => _resetIdentifier; set => SetProperty(ref _resetIdentifier, value); }
    public string ResetTenantKey { get => _resetTenantKey; set => SetProperty(ref _resetTenantKey, value); }
    public string ResetToken { get => _resetToken; set => SetProperty(ref _resetToken, value); }
    public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }

    public string RegTaxId { get => _regTaxId; set => SetProperty(ref _regTaxId, value); }
    public string RegCompanyName { get => _regCompanyName; set => SetProperty(ref _regCompanyName, value); }
    public string RegWorkspaceCode { get => _regWorkspaceCode; set => SetProperty(ref _regWorkspaceCode, value); }
    public string RegContactPerson { get => _regContactPerson; set => SetProperty(ref _regContactPerson, value); }
    public string RegContactPhone { get => _regContactPhone; set => SetProperty(ref _regContactPhone, value); }
    public string RegContactEmail { get => _regContactEmail; set => SetProperty(ref _regContactEmail, value); }
    public string? RegEvidenceKey { get => _regEvidenceKey; private set => SetProperty(ref _regEvidenceKey, value); }
    public string? RegEvidenceFileName { get => _regEvidenceFileName; private set => SetProperty(ref _regEvidenceFileName, value); }
    public string CodeCheckFeedback { get => _codeCheckFeedback; private set => SetProperty(ref _codeCheckFeedback, value); }
    public bool? IsCodeAvailable { get => _isCodeAvailable; private set => SetProperty(ref _isCodeAvailable, value); }
    public SubmitCompanyRegistrationResponseDto? RegistrationResult { get => _registrationResult; private set => SetProperty(ref _registrationResult, value); }

    public AsyncRelayCommand LoginCommand { get; }
    public AsyncRelayCommand QuickLoginCommand { get; }
    public RelayCommand SwitchScreenCommand { get; }
    public AsyncRelayCommand RequestEmailLinkCommand { get; }
    public AsyncRelayCommand VerifyEmailLinkCommand { get; }
    public AsyncRelayCommand ConfirmWorkspaceSelectionCommand { get; }
    public AsyncRelayCommand ForgotPasswordCommand { get; }
    public AsyncRelayCommand ResetPasswordCommand { get; }
    public AsyncRelayCommand GenerateCodeCommand { get; }
    public AsyncRelayCommand CheckCodeCommand { get; }
    public AsyncRelayCommand UploadEvidenceDocumentCommand { get; }
    public AsyncRelayCommand SubmitCompanyRegistrationCommand { get; }

    public void SwitchScreen(string screen)
    {
        ActiveScreen = screen;
        if (screen == "RegisterCompany")
        {
            RegistrationResult = null;
        }
    }

    public async Task ExecuteLoginAsync()
    {
        ErrorMessage = "";
        SuccessMessage = "";

        if (string.IsNullOrWhiteSpace(Identifier))
        {
            ErrorMessage = "Vui lòng nhập Mã nhân viên hoặc Email công ty.";
            _toast.Error(ErrorMessage, "Thiếu thông tin");
            return;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Vui lòng nhập mật khẩu.";
            _toast.Error(ErrorMessage, "Thiếu mật khẩu");
            return;
        }
        if (!IsSuperAdminIdentifier && string.IsNullOrWhiteSpace(TenantKey))
        {
            ErrorMessage = "Vui lòng nhập Mã không gian làm việc (ví dụ: bizflow-corp).";
            _toast.Error(ErrorMessage, "Thiếu mã không gian làm việc");
            return;
        }

        IsBusy = true;
        try
        {
            var tenant = IsSuperAdminIdentifier ? null : TenantKey.Trim();
            await _api.LoginAsync(Identifier.Trim(), Password, tenant);
            _toast.Success($"Chào mừng {_session.WelcomeName} đã đăng nhập vào hệ thống BizFlow!", "Đăng nhập thành công");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage, "Đăng nhập thất bại");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteQuickLoginAsync(string roleType)
    {
        switch (roleType.ToUpperInvariant())
        {
            case "SUPERADMIN":
                Identifier = "SUPERADMIN";
                Password = "Password123!";
                TenantKey = "";
                break;
            case "ADMIN":
                Identifier = "ADMIN001";
                Password = "Password123!";
                TenantKey = "bizflow-corp";
                break;
            case "MANAGER":
                Identifier = "MGR001";
                Password = "Password123!";
                TenantKey = "bizflow-corp";
                break;
            default:
                Identifier = "EMP001";
                Password = "Password123!";
                TenantKey = "bizflow-corp";
                break;
        }

        await ExecuteLoginAsync();
    }

    private async Task ExecuteRequestEmailLinkAsync()
    {
        ErrorMessage = "";
        SuccessMessage = "";
        if (string.IsNullOrWhiteSpace(EmailLinkAddress))
        {
            ErrorMessage = "Vui lòng nhập địa chỉ email doanh nghiệp.";
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _api.RequestEmailLinkAsync(EmailLinkAddress);
            SuccessMessage = $"Đã gửi liên kết đăng nhập tới {EmailLinkAddress} (hết hạn sau {res.ExpiresInMinutes} phút).";
            _toast.Success(SuccessMessage);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteVerifyEmailLinkAsync()
    {
        ErrorMessage = "";
        if (string.IsNullOrWhiteSpace(EmailLinkToken))
        {
            ErrorMessage = "Vui lòng nhập mã token xác thực hoặc sử dụng xác thực Google/Email.";
            return;
        }

        IsBusy = true;
        try
        {
            var res = EmailLinkToken.StartsWith("test-firebase-token:", StringComparison.OrdinalIgnoreCase)
                ? await _api.VerifyFirebaseTokenAsync(EmailLinkToken)
                : await _api.VerifyEmailLinkAsync(EmailLinkToken);

            if (res.Status == "RequiresWorkspaceSelection" && res.Workspaces is not null)
            {
                _workspaceTicket = res.Ticket;
                WorkspaceCandidates.Clear();
                foreach (var w in res.Workspaces) WorkspaceCandidates.Add(w);
                SelectedWorkspaceCandidate = WorkspaceCandidates.FirstOrDefault();
                SuccessMessage = "Vui lòng chọn không gian làm việc của bạn để hoàn tất đăng nhập.";
            }
            else if (res.Status == "NoWorkspace")
            {
                ErrorMessage = res.Message ?? "Không tìm thấy không gian làm việc nào cho email này.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteSelectWorkspaceAsync()
    {
        if (string.IsNullOrWhiteSpace(_workspaceTicket) || SelectedWorkspaceCandidate is null)
            return;

        IsBusy = true;
        try
        {
            await _api.SelectWorkspaceAsync(_workspaceTicket, SelectedWorkspaceCandidate.TenantKey);
            _toast.Success($"Đã đăng nhập vào {SelectedWorkspaceCandidate.TenantName}!");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteForgotPasswordAsync()
    {
        ErrorMessage = "";
        SuccessMessage = "";
        if (string.IsNullOrWhiteSpace(ResetIdentifier))
        {
            ErrorMessage = "Vui lòng nhập Mã nhân viên hoặc Email cần khôi phục mật khẩu.";
            return;
        }

        IsBusy = true;
        try
        {
            var msg = await _api.ForgotPasswordAsync(ResetIdentifier, ResetTenantKey);
            SuccessMessage = msg;
            _toast.Success(msg);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteResetPasswordAsync()
    {
        ErrorMessage = "";
        SuccessMessage = "";
        if (string.IsNullOrWhiteSpace(ResetIdentifier) || string.IsNullOrWhiteSpace(ResetToken) || string.IsNullOrWhiteSpace(NewPassword))
        {
            ErrorMessage = "Vui lòng điền đầy đủ Mã định danh, Token khôi phục và Mật khẩu mới.";
            return;
        }

        IsBusy = true;
        try
        {
            var msg = await _api.ResetPasswordAsync(ResetIdentifier, ResetTenantKey, ResetToken, NewPassword);
            SuccessMessage = msg;
            _toast.Success(msg);
            ActiveScreen = "Login";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteGenerateCodeAsync()
    {
        IsBusy = true;
        try
        {
            var res = await _api.GenerateWorkspaceCodeAsync(RegCompanyName);
            RegWorkspaceCode = res.Code;
            IsCodeAvailable = true;
            CodeCheckFeedback = $"✓ Mã '{res.Code}' khả dụng!";
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteCheckCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(RegWorkspaceCode))
        {
            CodeCheckFeedback = "Vui lòng nhập mã không gian làm việc cần kiểm tra.";
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _api.CheckWorkspaceCodeAsync(RegWorkspaceCode);
            IsCodeAvailable = res.Available;
            CodeCheckFeedback = res.Available
                ? $"✓ Mã '{res.Code}' hoàn toàn khả dụng!"
                : $"✕ {res.Message ?? "Mã không gian làm việc đã tồn tại."}";
        }
        catch (Exception ex)
        {
            CodeCheckFeedback = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteUploadEvidenceAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn Giấy phép kinh doanh / Hồ sơ pháp lý (PDF, PNG, JPG)",
            Filter = "Tài liệu minh chứng (*.pdf;*.png;*.jpg;*.jpeg)|*.pdf;*.png;*.jpg;*.jpeg"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var res = await _api.UploadCompanyDocumentAsync(dlg.FileName);
            RegEvidenceKey = res.DocumentKey;
            RegEvidenceFileName = res.FileName;
            _toast.Success($"Đã tải lên tệp minh chứng: {res.FileName}");
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message, "Lỗi tải tệp minh chứng");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteSubmitRegistrationAsync()
    {
        ErrorMessage = "";
        if (string.IsNullOrWhiteSpace(RegTaxId) ||
            string.IsNullOrWhiteSpace(RegCompanyName) ||
            string.IsNullOrWhiteSpace(RegWorkspaceCode) ||
            string.IsNullOrWhiteSpace(RegContactPerson) ||
            string.IsNullOrWhiteSpace(RegContactPhone) ||
            string.IsNullOrWhiteSpace(RegContactEmail))
        {
            ErrorMessage = "Vui lòng điền đầy đủ cả 6 trường thông tin doanh nghiệp bắt buộc.";
            _toast.Warning(ErrorMessage);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _api.SubmitCompanyRegistrationAsync(
                RegTaxId,
                RegCompanyName,
                RegWorkspaceCode,
                RegContactPerson,
                RegContactEmail,
                RegContactPhone,
                RegEvidenceKey,
                RegEvidenceFileName);
            RegistrationResult = result;
            _toast.Success($"Đã gửi hồ sơ đăng ký doanh nghiệp '{result.Name}' thành công!");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _toast.Error(ErrorMessage, "Đăng ký doanh nghiệp thất bại");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
