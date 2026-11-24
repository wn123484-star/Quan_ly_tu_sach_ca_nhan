using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BookShare.Web.Services.Implementations;

public class DatabaseBootstrapService : IDatabaseBootstrapService
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private readonly BookShareDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DatabaseBootstrapService> _logger;
    private readonly PasswordHasher<AspNetUser> _passwordHasher = new();

    public DatabaseBootstrapService(
        BookShareDbContext db,
        IMemoryCache cache,
        ILogger<DatabaseBootstrapService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        await InitLock.WaitAsync(ct);
        try
        {
            await EnsureExtensionTablesAsync(ct);
            await EnsureCoreRolesAndAdminAsync(ct);
            await EnsureDefaultIconsAsync(ct);
        }
        finally
        {
            InitLock.Release();
        }
    }

    private async Task EnsureExtensionTablesAsync(CancellationToken ct)
    {
        const string ensureTablesSql = @"
            IF OBJECT_ID(N'[dbo].[QrLoginSessions]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[dbo].[QrLoginSessions]', N'TokenHash') IS NULL
            BEGIN
                DROP TABLE [dbo].[QrLoginSessions];
            END
            IF OBJECT_ID(N'[dbo].[QrLoginSessions]', N'U') IS NULL
               AND OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
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
               AND OBJECT_ID(N'[dbo].[LoanRequests]', N'U') IS NOT NULL
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
               AND OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
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
            END

            IF OBJECT_ID(N'[dbo].[SystemIconSettings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SystemIconSettings] (
                    [IconKey]          NVARCHAR(64)  NOT NULL,
                    [DisplayName]      NVARCHAR(120) NOT NULL,
                    [CategoryGroup]    NVARCHAR(60)  NOT NULL,
                    [DefaultIconClass] NVARCHAR(100) NOT NULL,
                    [CustomImageUrl]   NVARCHAR(300) NULL,
                    [UseCustomImage]   BIT           NOT NULL CONSTRAINT [DF_SystemIconSettings_UseCustom] DEFAULT (0),
                    [UpdatedAt]        DATETIME2(7)  NOT NULL CONSTRAINT [DF_SystemIconSettings_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                    [UpdatedByUserId]  NVARCHAR(450) NULL,
                    CONSTRAINT [PK_SystemIconSettings] PRIMARY KEY CLUSTERED ([IconKey] ASC)
                );
            END";

        try
        {
            await _db.Database.ExecuteSqlRawAsync(ensureTablesSql, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bootstrap extension table check skipped or deferred.");
        }
    }

    private async Task EnsureCoreRolesAndAdminAsync(CancellationToken ct)
    {
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
            bool exists = await _db.AspNetRoles.AnyAsync(r => r.Id == id || r.NormalizedName == normalizedName, ct);
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
            await _db.SaveChangesAsync(ct);
        }

        var adminRole = await _db.AspNetRoles.FirstOrDefaultAsync(r => r.NormalizedName == "ADMIN", ct);
        var adminUser = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == "u-admin-01" || u.NormalizedEmail == "ADMIN@BOOKSHARE.VN", ct);

        if (adminUser == null)
        {
            adminUser = new AspNetUser
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
                AvatarUrl = "/images/avatar-default.svg",
                TrustScore = 150,
                LendQuotaBonus = 5,
                CreatedAt = DateTime.UtcNow
            };
            adminUser.PasswordHash = _passwordHasher.HashPassword(adminUser, "Admin@123");
            if (adminRole != null)
            {
                adminUser.Roles.Add(adminRole);
            }
            _db.AspNetUsers.Add(adminUser);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            bool dirty = false;
            if (!adminUser.EmailConfirmed)
            {
                adminUser.EmailConfirmed = true;
                dirty = true;
            }
            if (adminUser.LockoutEnd.HasValue)
            {
                adminUser.LockoutEnd = null;
                adminUser.AccessFailedCount = 0;
                dirty = true;
            }
            if (string.IsNullOrWhiteSpace(adminUser.PasswordHash)
                || adminUser.PasswordHash == "AQAAAAIAAYagAAAAEBookShareDemoHashAdmin123!")
            {
                adminUser.PasswordHash = _passwordHasher.HashPassword(adminUser, "Admin@123");
                dirty = true;
            }
            if (string.IsNullOrWhiteSpace(adminUser.AvatarUrl)
                || adminUser.AvatarUrl.Equals("/images/avatars/admin.png", StringComparison.OrdinalIgnoreCase))
            {
                adminUser.AvatarUrl = "/images/avatar-default.svg";
                dirty = true;
            }
            if (adminRole != null && !adminUser.Roles.Any(r => r.NormalizedName == "ADMIN"))
            {
                adminUser.Roles.Add(adminRole);
                dirty = true;
            }
            if (dirty)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        // Đảm bảo Admin luôn có tối thiểu 1 kệ sách & 1 hạng thành viên hợp lệ trên CSDL trắng
        bool hasShelf = await _db.Shelves.AnyAsync(s => s.UserId == adminUser.Id, ct);
        if (!hasShelf)
        {
            _db.Shelves.Add(new Shelf
            {
                UserId = adminUser.Id,
                Name = "Kệ Sách Quản Trị",
                Description = "Kệ sách cá nhân mặc định của Quản trị viên hệ thống",
                IsPublic = true,
                DisplayOrder = 1
            });
            await _db.SaveChangesAsync(ct);
        }

        bool hasMembership = await _db.Memberships.AnyAsync(m => m.UserId == adminUser.Id && m.IsActive, ct);
        if (!hasMembership)
        {
            _db.Memberships.Add(new Membership
            {
                UserId = adminUser.Id,
                Tier = "Premium",
                MaxConcurrentLoans = 5,
                StartDate = DateTime.UtcNow,
                IsActive = true
            });
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task EnsureDefaultIconsAsync(CancellationToken ct)
    {
        var existingKeys = await _db.SystemIconSettings
            .AsNoTracking()
            .Select(x => x.IconKey)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);

        bool added = false;
        var now = DateTime.UtcNow;
        foreach (var def in SystemIconService.DefaultCatalog)
        {
            if (existingSet.Contains(def.IconKey))
            {
                continue;
            }

            _db.SystemIconSettings.Add(new SystemIconSetting
            {
                IconKey = def.IconKey,
                DisplayName = def.DisplayName,
                CategoryGroup = def.CategoryGroup,
                DefaultIconClass = def.DefaultIconClass,
                CustomImageUrl = null,
                UseCustomImage = false,
                UpdatedAt = now
            });
            added = true;
        }

        if (added)
        {
            await _db.SaveChangesAsync(ct);
            _cache.Remove(SystemIconService.CacheKey);
        }
    }
}
