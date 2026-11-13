namespace BookShare.Web.Models.ViewModels;

public class S01AuthPageViewModel
{
    public bool IsAuthenticated { get; set; }
    public string? CurrentUserId { get; set; }
    public string? CurrentUserFullName { get; set; }
    public string? CurrentUserEmail { get; set; }
    public string? CurrentUserPhoneNumber { get; set; }
    public string? CurrentUserAddress { get; set; }
    public string? CurrentUserAvatarUrl { get; set; }
    public string? CurrentUserRole { get; set; }
    public int CurrentTrustScore { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? StatusMessage { get; set; }
    public int StatusCode { get; set; } = 200;
    public string? ReturnUrl { get; set; }
    public List<DemoRoleAccountDto> DemoAccounts { get; set; } = new();
}

public class DemoRoleAccountDto
{
    public string Role { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int TrustScore { get; set; }
    public string BadgeClass { get; set; } = "bg-primary";
}

public class LoginInputModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = true;
    public string? ReturnUrl { get; set; }
}

public class RegisterInputModel
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string SelectedRole { get; set; } = "User";
}

public class SendOtpRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Purpose { get; set; } = "VerifyLoan";
}

public class VerifyOtpRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public string Purpose { get; set; } = "VerifyLoan";
}

public class UpdateProfileRequestDto
{
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? AvatarUrl { get; set; }
}

public class ChangePasswordRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
