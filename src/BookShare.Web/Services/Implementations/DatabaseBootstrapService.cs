using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [CORE-DB] Dịch vụ tự động đồng bộ các bảng/cột mở rộng, đảm bảo 4 vai trò chuẩn (Admin, Librarian, Curator, User), tài khoản Quản trị viên gốc và 18 biểu tượng mặc định ngay lúc startup.
/// </summary>
/// <remarks>Chạy an toàn idempotent nhiều lần không gây trùng dữ liệu.</remarks>
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
            await EnsureClubGovernanceAsync(ct);
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
                    [IconKey]                NVARCHAR(64)   NOT NULL,
                    [DisplayName]            NVARCHAR(120)  NOT NULL,
                    [CategoryGroup]          NVARCHAR(60)   NOT NULL,
                    [DefaultIconClass]       NVARCHAR(100)  NOT NULL,
                    [CustomImageUrl]         NVARCHAR(300)  NULL,
                    [CustomImageData]        VARBINARY(MAX) NULL,
                    [CustomImageContentType] NVARCHAR(100)  NULL,
                    [UseCustomImage]         BIT            NOT NULL CONSTRAINT [DF_SystemIconSettings_UseCustom] DEFAULT (0),
                    [UpdatedAt]              DATETIME2(7)   NOT NULL CONSTRAINT [DF_SystemIconSettings_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                    [UpdatedByUserId]        NVARCHAR(450)  NULL,
                    CONSTRAINT [PK_SystemIconSettings] PRIMARY KEY CLUSTERED ([IconKey] ASC)
                );
            END
            ELSE
            BEGIN
                IF COL_LENGTH(N'[dbo].[SystemIconSettings]', N'CustomImageData') IS NULL
                    ALTER TABLE [dbo].[SystemIconSettings] ADD [CustomImageData] VARBINARY(MAX) NULL;
                IF COL_LENGTH(N'[dbo].[SystemIconSettings]', N'CustomImageContentType') IS NULL
                    ALTER TABLE [dbo].[SystemIconSettings] ADD [CustomImageContentType] NVARCHAR(100) NULL;
            END

            IF OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'UiPreferencesJson') IS NULL
                    ALTER TABLE [dbo].[AspNetUsers] ADD [UiPreferencesJson] NVARCHAR(1000) NULL;
            END

            IF OBJECT_ID(N'[dbo].[UserWallets]', N'U') IS NULL
               AND OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
            BEGIN
                CREATE TABLE [dbo].[UserWallets] (
                    [UserId]        NVARCHAR(450) NOT NULL,
                    [Balance]       DECIMAL(18,2) NOT NULL CONSTRAINT [DF_UserWallets_Balance] DEFAULT (0),
                    [TotalToppedUp] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_UserWallets_ToppedUp] DEFAULT (0),
                    [TotalSpent]    DECIMAL(18,2) NOT NULL CONSTRAINT [DF_UserWallets_Spent] DEFAULT (0),
                    [UpdatedAt]     DATETIME2(7)  NOT NULL CONSTRAINT [DF_UserWallets_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT [PK_UserWallets] PRIMARY KEY CLUSTERED ([UserId] ASC),
                    CONSTRAINT [FK_UserWallets_AspNetUsers] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [CK_UserWallets_Balance] CHECK ([Balance] >= 0)
                );
            END

            IF OBJECT_ID(N'[dbo].[Transactions]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'[dbo].[Transactions]', N'TargetTier') IS NULL
                    ALTER TABLE [dbo].[Transactions] ADD [TargetTier] NVARCHAR(20) NULL;
                IF COL_LENGTH(N'[dbo].[Transactions]', N'DurationMonths') IS NULL
                    ALTER TABLE [dbo].[Transactions] ADD [DurationMonths] INT NULL;
                IF COL_LENGTH(N'[dbo].[Transactions]', N'Description') IS NULL
                    ALTER TABLE [dbo].[Transactions] ADD [Description] NVARCHAR(300) NULL;
                IF COL_LENGTH(N'[dbo].[Transactions]', N'IdempotencyKey') IS NULL
                    ALTER TABLE [dbo].[Transactions] ADD [IdempotencyKey] NVARCHAR(100) NULL;
                IF COL_LENGTH(N'[dbo].[Transactions]', N'ActionType') IS NULL
                    ALTER TABLE [dbo].[Transactions] ADD [ActionType] NVARCHAR(30) NULL;
                IF COL_LENGTH(N'[dbo].[Transactions]', N'SwitchFee') IS NULL
                    ALTER TABLE [dbo].[Transactions] ADD [SwitchFee] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_Transactions_SwitchFee] DEFAULT (0);
                IF COL_LENGTH(N'[dbo].[Transactions]', N'ProrationCredit') IS NULL
                    ALTER TABLE [dbo].[Transactions] ADD [ProrationCredit] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_Transactions_ProrationCredit] DEFAULT (0);
                IF COL_LENGTH(N'[dbo].[Transactions]', N'BalanceBefore') IS NULL
                    ALTER TABLE [dbo].[Transactions] ADD [BalanceBefore] DECIMAL(18,2) NULL;
                IF COL_LENGTH(N'[dbo].[Transactions]', N'BalanceAfter') IS NULL
                    ALTER TABLE [dbo].[Transactions] ADD [BalanceAfter] DECIMAL(18,2) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Transactions_IdempotencyKey' AND object_id = OBJECT_ID(N'[dbo].[Transactions]'))
                    CREATE UNIQUE NONCLUSTERED INDEX [UX_Transactions_IdempotencyKey] ON [dbo].[Transactions]([IdempotencyKey] ASC) WHERE ([IdempotencyKey] IS NOT NULL);
                IF OBJECT_ID(N'[dbo].[CK_Transactions_Type]', N'C') IS NOT NULL
                    ALTER TABLE [dbo].[Transactions] DROP CONSTRAINT [CK_Transactions_Type];
                ALTER TABLE [dbo].[Transactions] ADD CONSTRAINT [CK_Transactions_Type]
                    CHECK ([TransactionType] IN (N'Membership', N'MembershipSwitch', N'MembershipCancel', N'Fine', N'SimulatedDeposit', N'WalletTopUp'));
                IF OBJECT_ID(N'[dbo].[CK_Transactions_Amount]', N'C') IS NOT NULL
                    ALTER TABLE [dbo].[Transactions] DROP CONSTRAINT [CK_Transactions_Amount];
                ALTER TABLE [dbo].[Transactions] ADD CONSTRAINT [CK_Transactions_Amount]
                    CHECK ([Amount] >= 0);
                IF OBJECT_ID(N'[dbo].[CK_Transactions_Gateway]', N'C') IS NOT NULL
                    ALTER TABLE [dbo].[Transactions] DROP CONSTRAINT [CK_Transactions_Gateway];
                ALTER TABLE [dbo].[Transactions] ADD CONSTRAINT [CK_Transactions_Gateway]
                    CHECK ([PaymentGateway] IN (N'VirtualWallet', N'QrPay', N'VNPaySandbox', N'MoMoSandbox'));
            END

            IF OBJECT_ID(N'[dbo].[Memberships]', N'U') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'[dbo].[CK_Memberships_Tier]', N'C') IS NOT NULL
                    ALTER TABLE [dbo].[Memberships] DROP CONSTRAINT [CK_Memberships_Tier];
                ALTER TABLE [dbo].[Memberships] ADD CONSTRAINT [CK_Memberships_Tier]
                    CHECK ([Tier] IN (N'Free', N'Standard', N'Premium', N'Scholar'));
            END

            IF OBJECT_ID(N'[dbo].[Libraries]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'[dbo].[Libraries]', N'Description') IS NULL
                    ALTER TABLE [dbo].[Libraries] ADD [Description] NVARCHAR(600) NULL;
                IF COL_LENGTH(N'[dbo].[Libraries]', N'ClubRules') IS NULL
                    ALTER TABLE [dbo].[Libraries] ADD [ClubRules] NVARCHAR(1000) NULL;
                IF COL_LENGTH(N'[dbo].[Libraries]', N'IsPublicClub') IS NULL
                    ALTER TABLE [dbo].[Libraries] ADD [IsPublicClub] BIT NOT NULL CONSTRAINT [DF_Libraries_IsPublicClub] DEFAULT (1);
            END

            IF OBJECT_ID(N'[dbo].[ClubMembers]', N'U') IS NULL
               AND OBJECT_ID(N'[dbo].[Libraries]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
            BEGIN
                CREATE TABLE [dbo].[ClubMembers] (
                    [Id]            INT IDENTITY(1,1) NOT NULL,
                    [HubId]         INT               NOT NULL,
                    [UserId]        NVARCHAR(450)     NOT NULL,
                    [MemberRole]    NVARCHAR(20)      NOT NULL CONSTRAINT [DF_ClubMembers_MemberRole] DEFAULT (N'Member'),
                    [Status]        NVARCHAR(20)      NOT NULL CONSTRAINT [DF_ClubMembers_Status] DEFAULT (N'Approved'),
                    [JoinNote]      NVARCHAR(300)     NULL,
                    [AddedByUserId] NVARCHAR(450)     NULL,
                    [JoinedAt]      DATETIME2(7)      NOT NULL CONSTRAINT [DF_ClubMembers_JoinedAt] DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT [PK_ClubMembers] PRIMARY KEY CLUSTERED ([Id] ASC),
                    CONSTRAINT [FK_ClubMembers_Libraries] FOREIGN KEY ([HubId]) REFERENCES [dbo].[Libraries] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_ClubMembers_AspNetUsers] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_ClubMembers_AddedByUser] FOREIGN KEY ([AddedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
                );
                CREATE UNIQUE NONCLUSTERED INDEX [UX_ClubMembers_Hub_User] ON [dbo].[ClubMembers]([HubId] ASC, [UserId] ASC);
                CREATE NONCLUSTERED INDEX [IX_ClubMembers_Hub_Status] ON [dbo].[ClubMembers]([HubId] ASC, [Status] ASC);
            END

            IF OBJECT_ID(N'[dbo].[ClubPosts]', N'U') IS NULL
               AND OBJECT_ID(N'[dbo].[Libraries]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
            BEGIN
                CREATE TABLE [dbo].[ClubPosts] (
                    [Id]           INT IDENTITY(1,1) NOT NULL,
                    [HubId]        INT               NOT NULL,
                    [AuthorUserId] NVARCHAR(450)     NOT NULL,
                    [Title]        NVARCHAR(200)     NOT NULL,
                    [Content]      NVARCHAR(1500)    NOT NULL,
                    [IsPinned]     BIT               NOT NULL CONSTRAINT [DF_ClubPosts_IsPinned] DEFAULT (0),
                    [CreatedAt]    DATETIME2(7)      NOT NULL CONSTRAINT [DF_ClubPosts_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT [PK_ClubPosts] PRIMARY KEY CLUSTERED ([Id] ASC),
                    CONSTRAINT [FK_ClubPosts_Libraries] FOREIGN KEY ([HubId]) REFERENCES [dbo].[Libraries] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_ClubPosts_AuthorUser] FOREIGN KEY ([AuthorUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
                );
                CREATE NONCLUSTERED INDEX [IX_ClubPosts_Hub_CreatedAt] ON [dbo].[ClubPosts]([HubId] ASC, [CreatedAt] DESC);
            END

            IF OBJECT_ID(N'[dbo].[Friendships]', N'U') IS NULL
               AND OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
            BEGIN
                CREATE TABLE [dbo].[Friendships] (
                    [Id]          INT IDENTITY(1,1) NOT NULL,
                    [RequesterId] NVARCHAR(450)     NOT NULL,
                    [AddresseeId] NVARCHAR(450)     NOT NULL,
                    [Status]      NVARCHAR(20)      NOT NULL CONSTRAINT [DF_Friendships_Status] DEFAULT (N'Pending'),
                    [Message]     NVARCHAR(300)     NULL,
                    [CreatedAt]   DATETIME2(7)      NOT NULL CONSTRAINT [DF_Friendships_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                    [RespondedAt] DATETIME2(7)      NULL,
                    CONSTRAINT [PK_Friendships] PRIMARY KEY CLUSTERED ([Id] ASC),
                    CONSTRAINT [FK_Friendships_Requester] FOREIGN KEY ([RequesterId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_Friendships_Addressee] FOREIGN KEY ([AddresseeId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
                    CONSTRAINT [CK_Friendships_Status] CHECK ([Status] IN (N'Pending', N'Accepted', N'Rejected')),
                    CONSTRAINT [CK_Friendships_NotSelf] CHECK ([RequesterId] <> [AddresseeId])
                );
                CREATE UNIQUE NONCLUSTERED INDEX [UX_Friendships_Pair] ON [dbo].[Friendships]([RequesterId] ASC, [AddresseeId] ASC);
                CREATE NONCLUSTERED INDEX [IX_Friendships_Addressee_Status] ON [dbo].[Friendships]([AddresseeId] ASC, [Status] ASC);
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

        bool hasWallet = await _db.UserWallets.AnyAsync(w => w.UserId == adminUser.Id, ct);
        if (!hasWallet)
        {
            _db.UserWallets.Add(new UserWallet
            {
                UserId = adminUser.Id,
                Balance = 500000m,
                TotalToppedUp = 500000m,
                TotalSpent = 0m,
                UpdatedAt = DateTime.UtcNow
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

    private async Task EnsureClubGovernanceAsync(CancellationToken ct)
    {
        try
        {
            var hubs = await _db.Libraries.ToListAsync(ct);
            if (!hubs.Any()) return;

            var existingMembers = await _db.ClubMembers.ToListAsync(ct);
            bool changed = false;
            var now = DateTime.UtcNow;

            foreach (var hub in hubs)
            {
                if (string.IsNullOrWhiteSpace(hub.Description))
                {
                    hub.Description = hub.HubType == "Library"
                        ? "Kho sách học thuật chuẩn do Thủ thư điều phối mượn trả miễn phí."
                        : "Không gian Câu lạc bộ đọc sách cộng đồng, ký gửi sách P2P và giao lưu tri thức.";
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(hub.ClubRules))
                {
                    hub.ClubRules = "1. Chia sẻ & mượn trả sách giấy 100% phi thương mại (0đ). 2. Giữ gìn sách sạch đẹp, trả đúng hẹn. 3. Tôn trọng điều phối của Trưởng nhóm CLB.";
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(hub.ManagerUserId))
                {
                    var leaderRow = existingMembers.FirstOrDefault(m => m.HubId == hub.Id && m.UserId == hub.ManagerUserId);
                    if (leaderRow == null)
                    {
                        var newLeader = new ClubMember
                        {
                            HubId = hub.Id,
                            UserId = hub.ManagerUserId,
                            MemberRole = "Leader",
                            Status = "Approved",
                            JoinNote = "Trưởng nhóm / Chủ nhiệm sáng lập trạm CLB",
                            AddedByUserId = hub.ManagerUserId,
                            JoinedAt = now
                        };
                        _db.ClubMembers.Add(newLeader);
                        existingMembers.Add(newLeader);
                        changed = true;
                    }
                    else if (leaderRow.MemberRole != "Leader" || leaderRow.Status != "Approved")
                    {
                        leaderRow.MemberRole = "Leader";
                        leaderRow.Status = "Approved";
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Club governance bootstrap skipped or deferred.");
        }
    }
}
