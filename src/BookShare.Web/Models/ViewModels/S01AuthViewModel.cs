namespace BookShare.Web.Models.ViewModels;

public class S01AuthPageViewModel
{
    public bool IsAuthenticated { get; set; }
    public bool IsGoogleOAuthConfigured { get; set; }
    public string? CurrentUserId { get; set; }
    public string? CurrentUserName { get; set; }
    public string? CurrentUserFullName { get; set; }
    public string? CurrentUserEmail { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool HasValidEmail { get; set; }
    public string? UiPreferencesJson { get; set; }
    public string? CurrentUserPhoneNumber { get; set; }
    public string? CurrentUserAddress { get; set; }
    public string? CurrentUserAvatarUrl { get; set; }
    public string? CurrentUserRole { get; set; }
    public int CurrentTrustScore { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? StatusMessage { get; set; }
    public int StatusCode { get; set; } = 200;
    public string? ReturnUrl { get; set; }
    public string? PendingCuratorStatus { get; set; }
    public string? LatestCuratorNote { get; set; }
}

public class LoginInputModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = true;
    public string? ReturnUrl { get; set; }
    // [BOT-GUARD] Token Google reCAPTCHA v3
    public string? RecaptchaToken { get; set; }
}

public class RegisterInputModel
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string SelectedRole { get; set; } = "User";
    // [BOT-GUARD] Token Google reCAPTCHA v3
    public string? RecaptchaToken { get; set; }
}

public class SendOtpRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Purpose { get; set; } = "VerifyLoan";
    // [BOT-GUARD] Token Google reCAPTCHA v3
    public string? RecaptchaToken { get; set; }
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
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? AvatarUrl { get; set; }
}

public class UiPreferencesDto
{
    public string Theme { get; set; } = "light";       // light / dark
    public string Style { get; set; } = "standard";    // standard / sketchy / flatly / morph / cyborg
    public string Palette { get; set; } = "blue";      // blue / ocean / emerald / gold / rose / lavender / sepia / coral / royal / midnight
    public string Font { get; set; } = "roboto";       // roboto / bevietnam / lora / patrick / jetbrains
    public string FontSize { get; set; } = "md";       // sm / md / lg / xl
}

public class ChangePasswordRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
