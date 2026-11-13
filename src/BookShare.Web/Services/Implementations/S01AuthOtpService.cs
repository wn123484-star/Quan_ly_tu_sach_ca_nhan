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

        if (!string.IsNullOrEmpty(currentUserId))
        {
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
                vm.CurrentUserRole = user.Roles.FirstOrDefault()?.Name ?? "User";
                vm.CurrentTrustScore = user.TrustScore;
                vm.TwoFactorEnabled = user.TwoFactorEnabled;
            }
        }

        return vm;
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> AuthenticateAsync(
        string email,
        string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Vui lòng nhập đầy đủ Email và Mật khẩu.", null);
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
                catch
                {
                    passwordValid = password == "BookShare@123";
                }
            }
        }

        if (!passwordValid)
        {
            return (false, "Mật khẩu không chính xác. (Gợi ý tài khoản mẫu: BookShare@123).", null);
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

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> RegisterUserAsync(
        RegisterInputModel input)
    {
        if (string.IsNullOrWhiteSpace(input.FullName) || string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrWhiteSpace(input.Password))
        {
            return (false, "Vui lòng điền đầy đủ Họ tên, Email và Mật khẩu.", null);
        }

        var normalizedEmail = input.Email.Trim().ToUpperInvariant();
        bool exists = await _db.AspNetUsers.AnyAsync(u => u.NormalizedEmail == normalizedEmail);
        if (exists)
        {
            return (false, "Địa chỉ Email này đã được đăng ký trên hệ thống.", null);
        }

        var allowedRoles = new[] { "User", "Curator", "Librarian", "Admin" };
        var roleName = allowedRoles.Contains(input.SelectedRole) ? input.SelectedRole : "User";
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
