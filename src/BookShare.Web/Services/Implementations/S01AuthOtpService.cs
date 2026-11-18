using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S01AuthOtpService : IS01AuthOtpService
{
    private readonly BookShareDbContext _db;
    private readonly PasswordHasher<AspNetUser> _passwordHasher = new();

    public S01AuthOtpService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S01AuthPageViewModel> GetAuthPageViewModelAsync(string? currentUserId, string? returnUrl = null)
    {
        var vm = new S01AuthPageViewModel
        {
            ReturnUrl = returnUrl,
            DemoAccounts = new List<DemoRoleAccountDto>
            {
                new()
                {
                    Role = "User",
                    RoleTitle = "User (Độc giả & Chủ tủ sách cá nhân)",
                    FullName = "Nguyễn Hùng (BIT250157)",
                    Email = "nguyenhung@cmc.edu.vn",
                    TrustScore = 128,
                    BadgeClass = "bg-primary"
                },
                new()
                {
                    Role = "Curator",
                    RoleTitle = "Curator (Chủ nhiệm CLB / Trạm kiểm định P2P)",
                    FullName = "Đỗ Quang Minh (CLB Sách)",
                    Email = "clbsach@cmc.edu.vn",
                    TrustScore = 135,
                    BadgeClass = "bg-info text-dark"
                },
                new()
                {
                    Role = "Librarian",
                    RoleTitle = "Librarian (Thủ thư Kho chung ĐH CMC)",
                    FullName = "ThS. Phạm Thu Hà",
                    Email = "thuthu@cmc.edu.vn",
                    TrustScore = 140,
                    BadgeClass = "bg-success"
                },
                new()
                {
                    Role = "Admin",
                    RoleTitle = "Admin (Quản trị viên Hệ thống)",
                    FullName = "Quản Trị Viên Hệ Thống",
                    Email = "admin@bookshare.vn",
                    TrustScore = 150,
                    BadgeClass = "bg-danger"
                }
            }
        };

        // Khách chưa đăng nhập không được truy vấn hoặc nhận dữ liệu của tài khoản demo.
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return vm;
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        if (user != null)
        {
            vm.IsAuthenticated = true;
            vm.CurrentUserId = user.Id;
            vm.CurrentUserFullName = user.FullName;
            vm.CurrentUserEmail = user.Email;
            vm.CurrentUserPhoneNumber = user.PhoneNumber;
            vm.CurrentUserAddress = user.Address;
            vm.CurrentUserAvatarUrl = user.AvatarUrl;
            vm.CurrentUserRole = user.Roles.FirstOrDefault()?.Name ?? "User";
            vm.CurrentTrustScore = user.TrustScore;
            vm.TwoFactorEnabled = user.TwoFactorEnabled;
        }

        return vm;
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UpdatedClaims)> UpdateUserProfileAsync(
        string userId,
        UpdateProfileRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (false, "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.", null);
        }
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2 || request.FullName.Trim().Length > 100)
        {
            return (false, "Họ và tên phải có từ 2 đến 100 ký tự.", null);
        }
        if (!string.IsNullOrWhiteSpace(request.AvatarUrl) &&
            (!Uri.TryCreate(request.AvatarUrl.Trim(), UriKind.Absolute, out var avatarUri) ||
             (avatarUri.Scheme != Uri.UriSchemeHttp && avatarUri.Scheme != Uri.UriSchemeHttps)))
        {
            return (false, "Đường dẫn ảnh đại diện phải bắt đầu bằng http:// hoặc https://.", null);
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return (false, "Không tìm thấy hồ sơ người dùng trong bảng AspNetUsers.", null);
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.Address = request.Address?.Trim();
        user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();

        await _db.SaveChangesAsync();

        var roleName = user.Roles.FirstOrDefault()?.Name ?? "User";
        var claims = new AuthUserClaimsDto
        {
            UserId = user.Id,
            Email = user.Email ?? "user@bookshare.vn",
            FullName = user.FullName,
            PrimaryRole = roleName,
            TrustScore = user.TrustScore
        };

        return (true, $"Đã cập nhật hồ sơ cá nhân của {user.FullName} trực tiếp vào bảng AspNetUsers!", claims);
    }

    public async Task<(bool Success, string Message)> ChangeUserPasswordAsync(
        string userId,
        ChangePasswordRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return (false, "Vui lòng nhập đầy đủ mật khẩu hiện tại và mật khẩu mới.");
        }

        if (request.NewPassword.Length < 6 || request.NewPassword.Length > 100)
        {
            return (false, "Mật khẩu mới phải có từ 6 đến 100 ký tự.");
        }
        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
        {
            return (false, "Mật khẩu mới phải khác mật khẩu hiện tại.");
        }
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (false, "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.");
        }

        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return (false, "Không tìm thấy người dùng trong CSDL.");
        }

        bool currentValid = false;
        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            // Các tài khoản SeedData cũ có hash đánh dấu demo, chưa phải hash Identity hợp lệ.
            // Chỉ chấp nhận mật khẩu seed đã công bố trong môi trường local; sau khi đổi sẽ ghi hash Identity thật.
            if (user.PasswordHash.StartsWith("AQAAAAIAAYagAAAAEBookShareDemoHash", StringComparison.Ordinal))
            {
                currentValid = string.Equals(request.CurrentPassword, "BookShare@123", StringComparison.Ordinal);
            }
            else
            {
                try
                {
                    var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
                    currentValid = verify != PasswordVerificationResult.Failed;
                }
                catch (FormatException)
                {
                    currentValid = false;
                }
            }
        }

        if (!currentValid)
        {
            return (false, "Mật khẩu hiện tại không chính xác.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();

        return (true, $"Đã đổi mật khẩu thành công cho tài khoản {user.Email} và cập nhật PasswordHash chuẩn Identity trong bảng AspNetUsers!");
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> AuthenticateAsync(
        string email,
        string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Vui lòng nhập đầy đủ Email và Mật khẩu.", null);
        }
        if (password.Length > 100 || email.Trim().Length > 254 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email.Trim()))
        {
            return (false, "Email hoặc mật khẩu không đúng định dạng.", null);
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản với địa chỉ Email này.", null);
        }

        bool passwordValid = false;
        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            // Hỗ trợ mật khẩu mẫu cho dữ liệu SeedData ("BookShare@123") hoặc kiểm tra băm chuẩn Identity
            if (user.PasswordHash.StartsWith("AQAAAAIAAYagAAAAEBookShareDemoHash") && password == "BookShare@123")
            {
                passwordValid = true;
                user.PasswordHash = _passwordHasher.HashPassword(user, password);
                await _db.SaveChangesAsync();
            }
            else
            {
                try
                {
                    var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
                    passwordValid = verifyResult != PasswordVerificationResult.Failed;
                }
                catch (FormatException)
                {
                    passwordValid = false;
                }
            }
        }

        if (!passwordValid)
        {
            return (false, "Email hoặc mật khẩu không chính xác.", null);
        }

        var primaryRole = user.Roles.FirstOrDefault()?.Name ?? "User";
        var claimsDto = new AuthUserClaimsDto
        {
            UserId = user.Id,
            Email = user.Email ?? email.Trim(),
            FullName = user.FullName,
            PrimaryRole = primaryRole,
            TrustScore = user.TrustScore
        };

        return (true, $"Đăng nhập thành công với vai trò [{primaryRole}]!", claimsDto);
    }

    public async Task<AuthUserClaimsDto?> GetUserClaimsForOtpLoginAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email.Trim()))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        if (user == null) return null;

        return new AuthUserClaimsDto
        {
            UserId = user.Id,
            Email = user.Email ?? email.Trim(),
            FullName = user.FullName,
            PrimaryRole = user.Roles.FirstOrDefault()?.Name ?? "User",
            TrustScore = user.TrustScore
        };
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> RegisterUserAsync(
        RegisterInputModel input)
    {
        if (string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Trim().Length < 2 || input.FullName.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(input.Email) || input.Email.Trim().Length > 254 ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(input.Email.Trim()) ||
            string.IsNullOrWhiteSpace(input.Password) || input.Password.Length < 6 || input.Password.Length > 100)
        {
            return (false, "Vui lòng kiểm tra họ tên, email và mật khẩu (mật khẩu tối thiểu 6 ký tự).", null);
        }

        var normalizedEmail = input.Email.Trim().ToUpperInvariant();
        bool exists = await _db.AspNetUsers.AnyAsync(u => u.NormalizedEmail == normalizedEmail);
        if (exists)
        {
            return (false, "Địa chỉ Email này đã được đăng ký trên hệ thống.", null);
        }

        // Không tin vai trò do client gửi lên. Đăng ký công khai luôn tạo tài khoản User;
        // quyền Curator/Librarian/Admin phải được cấp bởi luồng quản trị đã được bảo vệ.
        const string roleName = "User";
        var roleEntity = await _db.AspNetRoles.FirstOrDefaultAsync(r => r.Name == roleName);

        var newUser = new AspNetUser
        {
            Id = $"u-{Guid.NewGuid():N}"[..16],
            UserName = input.Email.Trim(),
            NormalizedUserName = normalizedEmail,
            Email = input.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            FullName = input.FullName.Trim(),
            Address = input.Address?.Trim() ?? "Hà Nội",
            TrustScore = 100,
            LendQuotaBonus = 0,
            CreatedAt = DateTime.UtcNow
        };
        newUser.PasswordHash = _passwordHasher.HashPassword(newUser, input.Password);

        if (roleEntity != null)
        {
            newUser.Roles.Add(roleEntity);
        }

        _db.AspNetUsers.Add(newUser);

        // Khởi tạo kệ sách mặc định và gói Free Membership cho thành viên mới
        _db.Shelves.Add(new Shelf
        {
            UserId = newUser.Id,
            Name = "Kệ sách cá nhân của tôi",
            Description = "Kệ sách mặc định khi đăng ký tài khoản BookShare",
            IsPublic = true,
            DisplayOrder = 1
        });

        _db.Memberships.Add(new Membership
        {
            UserId = newUser.Id,
            Tier = "Free",
            MaxConcurrentLoans = 2,
            StartDate = DateTime.UtcNow,
            IsActive = true
        });

        await _db.SaveChangesAsync();

        var claims = new AuthUserClaimsDto
        {
            UserId = newUser.Id,
            Email = newUser.Email,
            FullName = newUser.FullName,
            PrimaryRole = roleName,
            TrustScore = newUser.TrustScore
        };

        return (true, $"Tạo tài khoản mới thành công với vai trò [{roleName}] và 100 điểm TrustScore!", claims);
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> UpsertGoogleLoginAsync(
        string providerKey,
        string email,
        string fullName)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var userLogin = await _db.AspNetUserLogins
            .Include(l => l.User)
            .ThenInclude(u => u.Roles)
            .FirstOrDefaultAsync(l => l.LoginProvider == "Google" && l.ProviderKey == providerKey);

        AspNetUser? user = userLogin?.User;

        if (user == null)
        {
            user = await _db.AspNetUsers
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

            if (user == null)
            {
                var userRole = await _db.AspNetRoles.FirstOrDefaultAsync(r => r.Name == "User");
                user = new AspNetUser
                {
                    Id = $"u-gg-{Guid.NewGuid():N}"[..16],
                    UserName = email.Trim(),
                    NormalizedUserName = normalizedEmail,
                    Email = email.Trim(),
                    NormalizedEmail = normalizedEmail,
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    FullName = string.IsNullOrWhiteSpace(fullName) ? email.Split('@')[0] : fullName,
                    TrustScore = 100,
                    CreatedAt = DateTime.UtcNow
                };
                if (userRole != null) user.Roles.Add(userRole);
                _db.AspNetUsers.Add(user);
            }

            _db.AspNetUserLogins.Add(new AspNetUserLogin
            {
                LoginProvider = "Google",
                ProviderKey = providerKey,
                ProviderDisplayName = "Google OAuth 2.0",
                UserId = user.Id
            });

            await _db.SaveChangesAsync();
        }

        var roleName = user.Roles.FirstOrDefault()?.Name ?? "User";
        return (true, "Đã liên kết và đăng nhập bằng Google OAuth thành công!", new AuthUserClaimsDto
        {
            UserId = user.Id,
            Email = user.Email ?? email,
            FullName = user.FullName,
            PrimaryRole = roleName,
            TrustScore = user.TrustScore
        });
    }
}
