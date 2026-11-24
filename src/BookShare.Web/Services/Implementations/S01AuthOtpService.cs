using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S01AuthOtpService : IS01AuthOtpService
{
    private static int _sprint3SchemaEnsured;
    private readonly BookShareDbContext _db;
    private readonly IConfiguration _config;
    private readonly PasswordHasher<AspNetUser> _passwordHasher = new();

    public S01AuthOtpService(BookShareDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private async Task EnsureCoreAdminBootstrapAsync()
    {
        if (Interlocked.CompareExchange(ref _sprint3SchemaEnsured, 1, 0) == 0)
        {
            const string ensureSprint3TablesSql = @"
                IF OBJECT_ID(N'[dbo].[QrLoginSessions]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'[dbo].[QrLoginSessions]', N'TokenHash') IS NULL
                BEGIN
                    DROP TABLE [dbo].[QrLoginSessions];
                END
                IF OBJECT_ID(N'[dbo].[QrLoginSessions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[QrLoginSessions] (
                        [Id]              INT IDENTITY(1,1) NOT NULL,
                        [TokenHash]       NVARCHAR(128)     NOT NULL,
                        [FlowType]        NVARCHAR(30)      NOT NULL,
                        [Status]          NVARCHAR(20)      NOT NULL CONSTRAINT [DF_QrLoginSessions_Status] DEFAULT (N'Pending'),
                        [InitiatorUserId] NVARCHAR(450)     NULL,
                        [ApprovedUserId]  NVARCHAR(450)     NULL,
                        [DeviceInfo]      NVARCHAR(300)     NULL,
                        [IpAddress]       NVARCHAR(64)      NULL,
                        [CreatedAt]       DATETIME2(7)      NOT NULL CONSTRAINT [DF_QrLoginSessions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        [ExpiresAt]       DATETIME2(7)      NOT NULL,
                        [ConsumedAt]      DATETIME2(7)      NULL,
                        CONSTRAINT [PK_QrLoginSessions] PRIMARY KEY CLUSTERED ([Id] ASC),
                        CONSTRAINT [FK_QrLoginSessions_InitiatorUser] FOREIGN KEY ([InitiatorUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_QrLoginSessions_ApprovedUser] FOREIGN KEY ([ApprovedUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX [UX_QrLoginSessions_TokenHash] ON [dbo].[QrLoginSessions]([TokenHash] ASC);
                END
                IF OBJECT_ID(N'[dbo].[BookHandoverQrs]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[BookHandoverQrs] (
                        [Id]               INT IDENTITY(1,1) NOT NULL,
                        [LoanRequestId]    INT               NOT NULL,
                        [LoanId]           INT               NULL,
                        [HandoverType]     NVARCHAR(20)      NOT NULL,
                        [TokenHash]        NVARCHAR(128)     NOT NULL,
                        [ShortCodeHash]    NVARCHAR(128)     NOT NULL,
                        [CreatedByUserId]  NVARCHAR(450)     NOT NULL,
                        [ConsumedByUserId] NVARCHAR(450)     NULL,
                        [Status]           NVARCHAR(20)      NOT NULL CONSTRAINT [DF_BookHandoverQrs_Status] DEFAULT (N'Pending'),
                        [CreatedAt]        DATETIME2(7)      NOT NULL CONSTRAINT [DF_BookHandoverQrs_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        [ExpiresAt]        DATETIME2(7)      NOT NULL,
                        [ConsumedAt]       DATETIME2(7)      NULL,
                        CONSTRAINT [PK_BookHandoverQrs] PRIMARY KEY CLUSTERED ([Id] ASC),
                        CONSTRAINT [FK_BookHandoverQrs_LoanRequests] FOREIGN KEY ([LoanRequestId]) REFERENCES [dbo].[LoanRequests] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_BookHandoverQrs_Loans] FOREIGN KEY ([LoanId]) REFERENCES [dbo].[Loans] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BookHandoverQrs_CreatedByUser] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BookHandoverQrs_ConsumedByUser] FOREIGN KEY ([ConsumedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX [UX_BookHandoverQrs_TokenHash] ON [dbo].[BookHandoverQrs]([TokenHash] ASC);
                END
                IF OBJECT_ID(N'[dbo].[RoleUpgradeRequests]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[RoleUpgradeRequests] (
                        [Id]               INT IDENTITY(1,1) NOT NULL,
                        [UserId]           NVARCHAR(450)     NOT NULL,
                        [RequestedRole]    NVARCHAR(30)      NOT NULL CONSTRAINT [DF_RoleUpgradeRequests_Role] DEFAULT (N'Curator'),
                        [OrganizationName] NVARCHAR(200)     NOT NULL,
                        [Reason]           NVARCHAR(1000)    NOT NULL,
                        [Status]           NVARCHAR(20)      NOT NULL CONSTRAINT [DF_RoleUpgradeRequests_Status] DEFAULT (N'Pending'),
                        [ReviewedByUserId] NVARCHAR(450)     NULL,
                        [ReviewNote]       NVARCHAR(500)     NULL,
                        [CreatedAt]        DATETIME2(7)      NOT NULL CONSTRAINT [DF_RoleUpgradeRequests_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                        [ReviewedAt]       DATETIME2(7)      NULL,
                        CONSTRAINT [PK_RoleUpgradeRequests] PRIMARY KEY CLUSTERED ([Id] ASC),
                        CONSTRAINT [FK_RoleUpgradeRequests_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_RoleUpgradeRequests_Reviewer] FOREIGN KEY ([ReviewedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
                    );
                END";
            try
            {
                await _db.Database.ExecuteSqlRawAsync(ensureSprint3TablesSql);
            }
            catch
            {
                // Bỏ qua nếu schema đã đồng bộ từ SQL script chính
            }
        }
        var roleDefs = new (string Id, string Name, string NormalizedName)[]
        {
            ("role-admin", "Admin", "ADMIN"),
            ("role-librarian", "Librarian", "LIBRARIAN"),
            ("role-curator", "Curator", "CURATOR"),
            ("role-user", "User", "USER")
        };

        bool anyRoleAdded = false;
        foreach (var (id, name, normalizedName) in roleDefs)
        {
            var exists = await _db.AspNetRoles.AnyAsync(r => r.Id == id || r.NormalizedName == normalizedName);
            if (!exists)
            {
                _db.AspNetRoles.Add(new AspNetRole
                {
                    Id = id,
                    Name = name,
                    NormalizedName = normalizedName,
                    ConcurrencyStamp = Guid.NewGuid().ToString("N")
                });
                anyRoleAdded = true;
            }
        }

        if (anyRoleAdded)
        {
            await _db.SaveChangesAsync();
        }

        var adminExists = await _db.AspNetUsers.AnyAsync(u => u.Id == "u-admin-01");
        if (!adminExists)
        {
            var adminUser = new AspNetUser
            {
                Id = "u-admin-01",
                UserName = "admin@bookshare.vn",
                NormalizedUserName = "ADMIN@BOOKSHARE.VN",
                Email = "admin@bookshare.vn",
                NormalizedEmail = "ADMIN@BOOKSHARE.VN",
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                PhoneNumber = "0901000001",
                PhoneNumberConfirmed = true,
                TwoFactorEnabled = false,
                LockoutEnabled = true,
                AccessFailedCount = 0,
                FullName = "Quản Trị Viên Hệ Thống",
                Address = "Tòa nhà CMC, Duy Tân, Cầu Giấy, Hà Nội",
                AvatarUrl = "/images/avatars/admin.png",
                TrustScore = 150,
                LendQuotaBonus = 5,
                CreatedAt = DateTime.UtcNow
            };
            adminUser.PasswordHash = _passwordHasher.HashPassword(adminUser, "Admin@123");

            var adminRole = await _db.AspNetRoles.FirstOrDefaultAsync(r => r.NormalizedName == "ADMIN");
            if (adminRole != null)
            {
                adminUser.Roles.Add(adminRole);
            }

            _db.AspNetUsers.Add(adminUser);
            await _db.SaveChangesAsync();
        }
    }

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
                    ? "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?w=200"
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
