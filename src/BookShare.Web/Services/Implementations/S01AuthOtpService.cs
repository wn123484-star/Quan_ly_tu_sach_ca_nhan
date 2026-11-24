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
    private readonly IConfiguration _config;
    private readonly IDatabaseBootstrapService _bootstrap;
    private readonly PasswordHasher<AspNetUser> _passwordHasher = new();

    public S01AuthOtpService(
        BookShareDbContext db,
        IConfiguration config,
        IDatabaseBootstrapService bootstrap)
    {
        _db = db;
        _config = config;
        _bootstrap = bootstrap;
    }

    private Task EnsureCoreAdminBootstrapAsync() => _bootstrap.EnsureInitializedAsync();

    public async Task<S01AuthPageViewModel> GetAuthPageViewModelAsync(string? currentUserId, string? returnUrl = null)
    {
        await EnsureCoreAdminBootstrapAsync();

        var vm = new S01AuthPageViewModel
        {
            ReturnUrl = returnUrl,
            IsGoogleOAuthConfigured = AuthConfigHelper.IsGoogleOAuthConfigured(_config)
        };

        if (!string.IsNullOrWhiteSpace(currentUserId))
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
                vm.CurrentUserPhoneNumber = user.PhoneNumber ?? string.Empty;
                vm.CurrentUserAddress = user.Address ?? string.Empty;
                vm.CurrentUserAvatarUrl = string.IsNullOrWhiteSpace(user.AvatarUrl)
                    ? "/images/avatar-default.svg"
                    : user.AvatarUrl;
                vm.CurrentUserRole = user.Roles.FirstOrDefault()?.Name ?? "User";
                vm.CurrentTrustScore = user.TrustScore;
                vm.TwoFactorEnabled = user.TwoFactorEnabled;

                var latestCuratorReq = await _db.RoleUpgradeRequests
                    .AsNoTracking()
                    .Where(r => r.UserId == user.Id && r.RequestedRole == "Curator")
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync();
                vm.PendingCuratorStatus = latestCuratorReq?.Status;
                vm.LatestCuratorNote = latestCuratorReq?.ReviewNote;
            }
        }

        return vm;
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UpdatedClaims)> UpdateUserProfileAsync(
        string userId,
        UpdateProfileRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return (false, "Họ và tên không được để trống.", null);
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return (false, "Không tìm thấy thông tin hồ sơ người dùng.", null);
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.Address = request.Address?.Trim();
        if (!string.IsNullOrWhiteSpace(request.AvatarUrl))
        {
            user.AvatarUrl = request.AvatarUrl.Trim();
        }

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

        return (true, $"Đã cập nhật hồ sơ cá nhân của {user.FullName} thành công.", claims);
    }

    public async Task<(bool Success, string Message)> ChangeUserPasswordAsync(
        string userId,
        ChangePasswordRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return (false, "Vui lòng nhập đầy đủ mật khẩu hiện tại và mật khẩu mới.");
        }

        if (request.NewPassword.Length < 6)
        {
            return (false, "Mật khẩu mới phải có tối thiểu 6 ký tự.");
        }

        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản người dùng.");
        }

        var currentValid = VerifyUserPassword(user, request.CurrentPassword, out _);
        if (!currentValid)
        {
            return (false, "Mật khẩu hiện tại không chính xác.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();

        return (true, $"Đã đổi mật khẩu thành công cho tài khoản {user.Email}.");
    }

    public async Task<(bool Success, string Message)> SubmitCuratorRequestAsync(
        string userId,
        SubmitCuratorRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationName) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return (false, "Vui lòng nhập đầy đủ Tên Câu lạc bộ / Điểm trạm và Lý do đăng ký điều hành.");
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return (false, "Không tìm thấy hồ sơ thành viên.");
        }

        var currentRole = user.Roles.FirstOrDefault()?.Name ?? "User";
        if (currentRole != "User")
        {
            return (false, $"Tài khoản của bạn hiện đang giữ vai trò {currentRole}, không cần đăng ký nâng quyền thêm.");
        }

        bool hasPending = await _db.RoleUpgradeRequests.AnyAsync(r =>
            r.UserId == userId && r.RequestedRole == "Curator" && r.Status == "Pending");
        if (hasPending)
        {
            return (false, "Bạn đã có một đề nghị trở thành Chủ nhiệm CLB đang chờ Quản trị viên xét duyệt.");
        }

        var upgradeReq = new RoleUpgradeRequest
        {
            UserId = userId,
            RequestedRole = "Curator",
            OrganizationName = request.OrganizationName.Trim(),
            Reason = request.Reason.Trim(),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        _db.RoleUpgradeRequests.Add(upgradeReq);

        var adminUserIds = await _db.AspNetUsers
            .Where(u => u.Roles.Any(r => r.Name == "Admin"))
            .Select(u => u.Id)
            .Take(5)
            .ToListAsync();

        foreach (var adminId in adminUserIds)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = adminId,
                Title = $"Đề nghị nâng quyền Chủ nhiệm CLB mới từ {user.FullName}",
                Content = $"Thành viên {user.FullName} ({user.Email}) đề nghị phụ trách điểm trạm \"{upgradeReq.OrganizationName}\". Mời Quản trị viên xem xét phê duyệt tại trang Quản trị.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã gửi đề nghị trở thành Chủ nhiệm CLB cho đơn vị \"{upgradeReq.OrganizationName}\". Quản trị viên sẽ xem xét và phản hồi qua thông báo.");
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> AuthenticateAsync(
        string email,
        string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Vui lòng nhập đầy đủ Email và Mật khẩu.", null);
        }

        await EnsureCoreAdminBootstrapAsync();

        var normalizedEmail = email.Trim().ToUpperInvariant();
        if (normalizedEmail == "ADMIN")
        {
            normalizedEmail = "ADMIN@BOOKSHARE.VN";
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail || u.NormalizedUserName == normalizedEmail);

        if (user == null)
        {
            return (false, "Email hoặc mật khẩu không chính xác.", null);
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản của bạn hiện đang bị tạm khóa. Vui lòng liên hệ Quản trị viên để được hỗ trợ.", null);
        }

        bool passwordValid = VerifyUserPassword(user, password, out bool needsRehash);
        if (!passwordValid)
        {
            return (false, "Email hoặc mật khẩu không chính xác.", null);
        }

        if (needsRehash)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            await _db.SaveChangesAsync();
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

        return (true, $"Đăng nhập thành công! Chào mừng {user.FullName}.", claimsDto);
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> RegisterUserAsync(
        RegisterInputModel input)
    {
        if (string.IsNullOrWhiteSpace(input.FullName) || string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrWhiteSpace(input.Password))
        {
            return (false, "Vui lòng điền đầy đủ Họ tên, Email và Mật khẩu.", null);
        }

        if (input.Password.Length < 6)
        {
            return (false, "Mật khẩu đăng ký phải có tối thiểu 6 ký tự.", null);
        }

        await EnsureCoreAdminBootstrapAsync();

        var normalizedEmail = input.Email.Trim().ToUpperInvariant();
        bool exists = await _db.AspNetUsers.AnyAsync(u => u.NormalizedEmail == normalizedEmail || u.NormalizedUserName == normalizedEmail);
        if (exists)
        {
            return (false, "Địa chỉ Email này đã được đăng ký trên hệ thống.", null);
        }

        // Đăng ký công khai luôn luôn gán vai trò thành viên tiêu chuẩn (User)
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
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            FullName = input.FullName.Trim(),
            Address = string.IsNullOrWhiteSpace(input.Address) ? "Hà Nội" : input.Address.Trim(),
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

        _db.Shelves.Add(new Shelf
        {
            UserId = newUser.Id,
            Name = "Kệ sách cá nhân của tôi",
            Description = "Kệ sách mặc định dành cho thành viên mới",
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

        return (true, "Đăng ký tài khoản mới thành công với 100 điểm uy tín khởi đầu!", claims);
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> UpsertGoogleLoginAsync(
        string providerKey,
        string email,
        string fullName)
    {
        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
        {
            return (false, "Thiếu thông tin định danh từ Google.", null);
        }

        await EnsureCoreAdminBootstrapAsync();

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
                    LockoutEnabled = true,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    FullName = string.IsNullOrWhiteSpace(fullName) ? email.Split('@')[0] : fullName.Trim(),
                    Address = "Hà Nội",
                    TrustScore = 100,
                    LendQuotaBonus = 0,
                    CreatedAt = DateTime.UtcNow
                };
                if (userRole != null)
                {
                    user.Roles.Add(userRole);
                }
                _db.AspNetUsers.Add(user);

                _db.Shelves.Add(new Shelf
                {
                    UserId = user.Id,
                    Name = "Kệ sách cá nhân của tôi",
                    Description = "Kệ sách mặc định dành cho thành viên mới",
                    IsPublic = true,
                    DisplayOrder = 1
                });

                _db.Memberships.Add(new Membership
                {
                    UserId = user.Id,
                    Tier = "Free",
                    MaxConcurrentLoans = 2,
                    StartDate = DateTime.UtcNow,
                    IsActive = true
                });
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
            {
                return (false, "Tài khoản của bạn hiện đang bị tạm khóa. Không thể đăng nhập bằng Google.", null);
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
        else if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản của bạn hiện đang bị tạm khóa. Không thể đăng nhập bằng Google.", null);
        }

        var roleName = user.Roles.FirstOrDefault()?.Name ?? "User";
        return (true, "Đã liên kết và đăng nhập bằng Google thành công!", new AuthUserClaimsDto
        {
            UserId = user.Id,
            Email = user.Email ?? email,
            FullName = user.FullName,
            PrimaryRole = roleName,
            TrustScore = user.TrustScore
        });
    }

    private bool VerifyUserPassword(AspNetUser user, string password, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrWhiteSpace(user.PasswordHash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        // Hỗ trợ nâng cấp mật khẩu khởi tạo từ dữ liệu SQL mẫu cũ sang băm chuẩn Identity trong lần đăng nhập đầu tiên
        var legacySeedSecret = user.PasswordHash switch
        {
            "AQAAAAIAAYagAAAAEBookShareDemoHashAdmin123!" => "Admin@123",
            "AQAAAAIAAYagAAAAEBookShareDemoHashLib123!" => "Librarian@123",
            "AQAAAAIAAYagAAAAEBookShareDemoHashCurator123!" => "Curator@123",
            "AQAAAAIAAYagAAAAEBookShareDemoHashUser123!" => "User@123",
            _ => null
        };

        if (legacySeedSecret != null)
        {
            if (string.Equals(password, legacySeedSecret, StringComparison.Ordinal))
            {
                needsRehash = true;
                return true;
            }
            return false;
        }

        try
        {
            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                needsRehash = true;
                return true;
            }
            return verifyResult == PasswordVerificationResult.Success;
        }
        catch
        {
            return false;
        }
    }
}
