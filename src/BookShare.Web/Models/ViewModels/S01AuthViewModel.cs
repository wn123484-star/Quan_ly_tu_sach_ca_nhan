using System.ComponentModel.DataAnnotations;

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
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(100)]
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = true;
    public string? ReturnUrl { get; set; }
}

public class RegisterInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên phải có từ 2 đến 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
    public string Password { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Địa chỉ không được vượt quá 250 ký tự.")]
    public string? Address { get; set; }

}

public class SendOtpRequestDto
{
    [Required(ErrorMessage = "Vui lòng nhập email nhận OTP.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Login|Register|ResetPassword|VerifyLoan)$", ErrorMessage = "Mục đích OTP không hợp lệ.")]
    public string Purpose { get; set; } = "VerifyLoan";
}

public class VerifyOtpRequestDto
{
    [Required(ErrorMessage = "Vui lòng nhập email nhận OTP.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mã OTP.")]
    [RegularExpression("^\\d{6}$", ErrorMessage = "Mã OTP phải gồm đúng 6 chữ số.")]
    public string OtpCode { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Login|Register|ResetPassword|VerifyLoan)$", ErrorMessage = "Mục đích OTP không hợp lệ.")]
    public string Purpose { get; set; } = "VerifyLoan";
}

public class UpdateProfileRequestDto
{
    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên phải có từ 2 đến 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")]
    [RegularExpression("^\\+?[0-9\\s().-]{8,20}$", ErrorMessage = "Số điện thoại không đúng định dạng.")]
    public string? PhoneNumber { get; set; }

    [StringLength(250, ErrorMessage = "Địa chỉ không được vượt quá 250 ký tự.")]
    public string? Address { get; set; }

    [StringLength(500, ErrorMessage = "Đường dẫn ảnh không được vượt quá 500 ký tự.")]
    [Url(ErrorMessage = "Đường dẫn ảnh phải là URL hợp lệ.")]
    public string? AvatarUrl { get; set; }
}

public class ChangePasswordRequestDto
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại.")]
    [StringLength(100)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu mới phải có từ 6 đến 100 ký tự.")]
    public string NewPassword { get; set; } = string.Empty;
}
