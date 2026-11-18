using BizFlow.Desktop.Core;
using BizFlow.Desktop.Models;

namespace BizFlow.Desktop.Services;

public sealed class SessionManager : ObservableObject
{
    private LoginResponseDto? _currentSession;

    public event EventHandler? SessionChanged;

    public LoginResponseDto? CurrentSession => _currentSession;

    public SessionIdentityDto? Identity => _currentSession?.Session;

    public bool IsSignedIn => _currentSession?.Session is not null;

    public string? AccessToken => _currentSession?.AccessToken;

    public string? RefreshToken => _currentSession?.RefreshToken;

    public bool IsPlatformAdmin => IsSignedIn && Identity?.TenantId is null;

    public bool IsCompanyAdmin => HasPermission("roles.configure");

    public bool IsManager => !IsCompanyAdmin && HasPermission("tasks.assign");

    public bool IsEmployee => IsSignedIn && !IsPlatformAdmin && !IsCompanyAdmin && !IsManager;

    public string UserRoleCode
    {
        get
        {
            if (!IsSignedIn) return "GUEST";
            if (Identity?.TenantId is null) return "SUPERADMIN";
            if (HasPermission("roles.configure")) return "COMPANY_ADMIN";
            if (HasPermission("tasks.assign")) return "MANAGER";
            return "EMPLOYEE";
        }
    }

    public string CurrentRoleLabel => UserRoleCode switch
    {
        "SUPERADMIN" => "Quản trị viên Hệ thống (Platform Admin)",
        "COMPANY_ADMIN" => "Quản trị viên Doanh nghiệp (Company Admin)",
        "MANAGER" => "Quản lý Vận hành / Trưởng phòng",
        "EMPLOYEE" => "Nhân viên Doanh nghiệp",
        _ => "Khách"
    };

    public string WelcomeName
    {
        get
        {
            var raw = Identity?.FullName?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return "Quản trị viên Hệ thống";
            if (raw.Equals("Platform Administrator", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("superadmin", StringComparison.OrdinalIgnoreCase))
                return "Quản trị viên Hệ thống";
            if (raw.Equals("Company Administrator", StringComparison.OrdinalIgnoreCase))
                return "Quản trị viên Doanh nghiệp";
            if (raw.Equals("Operation Manager", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("Operational Manager", StringComparison.OrdinalIgnoreCase))
                return "Quản lý Vận hành";
            if (raw.Equals("Regular Employee", StringComparison.OrdinalIgnoreCase))
                return "Nhân viên Doanh nghiệp";
            return raw;
        }
    }

    public string TenantDisplayName =>
        !string.IsNullOrWhiteSpace(Identity?.TenantName)
            ? Identity!.TenantName!
            : (IsPlatformAdmin ? "Platform Governance (Toàn Nền Tảng)" : "BizFlow Corporation");

    public string TenantDisplayLabel => TenantDisplayName;

    public string RoleBadgeLabel => CurrentRoleLabel;

    public string EmployeeCode => !string.IsNullOrWhiteSpace(Identity?.EmployeeCode) ? Identity!.EmployeeCode! : UserRoleCode;

    public string UserInitials
    {
        get
        {
            var name = Identity?.FullName?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return "BF";
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
            return name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant();
        }
    }

    public void SetSession(LoginResponseDto? session)
    {
        _currentSession = session;
        RaiseAllProperties();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _currentSession = null;
        RaiseAllProperties();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool HasPermission(string permission)
    {
        var perms = Identity?.Permissions;
        if (perms is null || perms.Length == 0) return false;
        return perms.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasAnyPermission(params string[] permissions)
    {
        foreach (var p in permissions)
        {
            if (HasPermission(p)) return true;
        }
        return false;
    }

    private void RaiseAllProperties()
    {
        OnPropertyChanged(nameof(CurrentSession));
        OnPropertyChanged(nameof(Identity));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(AccessToken));
        OnPropertyChanged(nameof(RefreshToken));
        OnPropertyChanged(nameof(IsPlatformAdmin));
        OnPropertyChanged(nameof(IsCompanyAdmin));
        OnPropertyChanged(nameof(IsManager));
        OnPropertyChanged(nameof(IsEmployee));
        OnPropertyChanged(nameof(UserRoleCode));
        OnPropertyChanged(nameof(CurrentRoleLabel));
        OnPropertyChanged(nameof(RoleBadgeLabel));
        OnPropertyChanged(nameof(WelcomeName));
        OnPropertyChanged(nameof(TenantDisplayName));
        OnPropertyChanged(nameof(TenantDisplayLabel));
        OnPropertyChanged(nameof(EmployeeCode));
        OnPropertyChanged(nameof(UserInitials));
    }
}
