using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Data;

/// <summary>
/// [BS-RULE-SCHEMA-ENSURER] Tự động đồng bộ cột và 4 bảng mở rộng (Thuê sách Escrow, Ví Pay, QR Cross-Login,
/// Reading Studio, Chat Thủ thư) bằng kiểm tra idempotent IF OBJECT_ID / COL_LENGTH IS NULL ngay khi khởi động,
/// đảm bảo môi trường VPS Docker &amp; LocalDB nâng cấp mượt mà không bao giờ văng lỗi thiếu cột/bảng.
/// </summary>
public static class DatabaseSchemaEnsurer
{
    public static async Task EnsureExtendedSchemaAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookShareDbContext>();
        var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("DatabaseSchemaEnsurer");

        var batches = new[]
        {
            // 1. Mở rộng BookCopies thêm DailyRentalFee & DepositAmount
            @"
            IF OBJECT_ID(N'[dbo].[BookCopies]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.BookCopies', N'DailyRentalFee') IS NULL
                    ALTER TABLE [dbo].[BookCopies] ADD [DailyRentalFee] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_BookCopies_DailyRentalFee] DEFAULT (0);
                IF COL_LENGTH(N'dbo.BookCopies', N'DepositAmount') IS NULL
                    ALTER TABLE [dbo].[BookCopies] ADD [DepositAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_BookCopies_DepositAmount] DEFAULT (0);
            END",

            // 2. Mở rộng Loans thêm RentalFeeTotal, DepositHeld, EscrowStatus
            @"
            IF OBJECT_ID(N'[dbo].[Loans]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.Loans', N'RentalFeeTotal') IS NULL
                    ALTER TABLE [dbo].[Loans] ADD [RentalFeeTotal] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_Loans_RentalFeeTotal] DEFAULT (0);
                IF COL_LENGTH(N'dbo.Loans', N'DepositHeld') IS NULL
                    ALTER TABLE [dbo].[Loans] ADD [DepositHeld] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_Loans_DepositHeld] DEFAULT (0);
                IF COL_LENGTH(N'dbo.Loans', N'EscrowStatus') IS NULL
                    ALTER TABLE [dbo].[Loans] ADD [EscrowStatus] NVARCHAR(30) NOT NULL CONSTRAINT [DF_Loans_EscrowStatus] DEFAULT (N'None');
            END",

            // 3. Mở rộng CHECK constraint trên bảng Transactions để hỗ trợ BookSharePay & Escrow
            @"
            IF OBJECT_ID(N'[dbo].[Transactions]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Transactions_Type')
                    ALTER TABLE [dbo].[Transactions] DROP CONSTRAINT [CK_Transactions_Type];
                ALTER TABLE [dbo].[Transactions] WITH NOCHECK ADD CONSTRAINT [CK_Transactions_Type]
                    CHECK ([TransactionType] IN (N'Membership', N'Fine', N'SimulatedDeposit', N'WalletTopUp', N'RentalFee', N'DepositHold', N'DepositRefund', N'LenderPayout', N'QrPay', N'RenewalFee'));

                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Transactions_Gateway')
                    ALTER TABLE [dbo].[Transactions] DROP CONSTRAINT [CK_Transactions_Gateway];
                ALTER TABLE [dbo].[Transactions] WITH NOCHECK ADD CONSTRAINT [CK_Transactions_Gateway]
                    CHECK ([PaymentGateway] IN (N'VNPaySandbox', N'MoMoSandbox', N'BookSharePay'));
            END",

            // 3b. [BS-SEC-OTP] Mở rộng CHECK constraint trên OtpVerifications để hỗ trợ VerifyNotificationEmail
            @"
            IF OBJECT_ID(N'[dbo].[OtpVerifications]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OtpVerifications_Purpose')
                    ALTER TABLE [dbo].[OtpVerifications] DROP CONSTRAINT [CK_OtpVerifications_Purpose];
                ALTER TABLE [dbo].[OtpVerifications] WITH NOCHECK ADD CONSTRAINT [CK_OtpVerifications_Purpose]
                    CHECK ([Purpose] IN (N'Register', N'ResetPassword', N'VerifyLoan', N'VerifyNotificationEmail'));
            END",

            // 4. Tạo bảng QrLoginSessions
            @"
            IF OBJECT_ID(N'[dbo].[QrLoginSessions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[QrLoginSessions] (
                    [Id]               INT IDENTITY(1,1) NOT NULL,
                    [Token]            NVARCHAR(100)     NOT NULL,
                    [Status]           NVARCHAR(20)      NOT NULL CONSTRAINT [DF_QrLoginSessions_Status] DEFAULT (N'Pending'),
                    [ApprovedByUserId] NVARCHAR(450)     NULL,
                    [ExpiresAt]        DATETIME2(7)      NOT NULL,
                    [CreatedAt]        DATETIME2(7)      NOT NULL CONSTRAINT [DF_QrLoginSessions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT [PK_QrLoginSessions] PRIMARY KEY CLUSTERED ([Id] ASC),
                    CONSTRAINT [UQ_QrLoginSessions_Token] UNIQUE ([Token]),
                    CONSTRAINT [FK_QrLoginSessions_AspNetUsers] FOREIGN KEY ([ApprovedByUserId])
                        REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE SET NULL,
                    CONSTRAINT [CK_QrLoginSessions_Status] CHECK ([Status] IN (N'Pending', N'Approved', N'Claimed', N'Expired'))
                );
                CREATE NONCLUSTERED INDEX [IX_QrLoginSessions_Token_Status] ON [dbo].[QrLoginSessions]([Token] ASC, [Status] ASC);
            END",

            // 5. Tạo bảng UserWallets
            @"
            IF OBJECT_ID(N'[dbo].[UserWallets]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[UserWallets] (
                    [Id]        INT IDENTITY(1,1) NOT NULL,
                    [UserId]    NVARCHAR(450)     NOT NULL,
                    [Balance]   DECIMAL(18,2)     NOT NULL CONSTRAINT [DF_UserWallets_Balance] DEFAULT (0),
                    [PinHash]   NVARCHAR(128)     NOT NULL CONSTRAINT [DF_UserWallets_PinHash] DEFAULT (N'8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92'),
                    [UpdatedAt] DATETIME2(7)      NOT NULL CONSTRAINT [DF_UserWallets_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT [PK_UserWallets] PRIMARY KEY CLUSTERED ([Id] ASC),
                    CONSTRAINT [UQ_UserWallets_UserId] UNIQUE ([UserId]),
                    CONSTRAINT [FK_UserWallets_AspNetUsers] FOREIGN KEY ([UserId])
                        REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [CK_UserWallets_Balance] CHECK ([Balance] >= 0)
                );
            END",

            // 6. Tạo bảng ReadingSessions
            @"
            IF OBJECT_ID(N'[dbo].[ReadingSessions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ReadingSessions] (
                    [Id]              INT IDENTITY(1,1) NOT NULL,
                    [UserId]          NVARCHAR(450)     NOT NULL,
                    [BookId]          INT               NOT NULL,
                    [DurationMinutes] INT               NOT NULL CONSTRAINT [DF_ReadingSessions_Duration] DEFAULT (25),
                    [PagesRead]       INT               NOT NULL CONSTRAINT [DF_ReadingSessions_Pages] DEFAULT (0),
                    [SessionDate]     DATE              NOT NULL CONSTRAINT [DF_ReadingSessions_Date] DEFAULT (CAST(SYSUTCDATETIME() AS DATE)),
                    [CreatedAt]       DATETIME2(7)      NOT NULL CONSTRAINT [DF_ReadingSessions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT [PK_ReadingSessions] PRIMARY KEY CLUSTERED ([Id] ASC),
                    CONSTRAINT [FK_ReadingSessions_AspNetUsers] FOREIGN KEY ([UserId])
                        REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_ReadingSessions_Books] FOREIGN KEY ([BookId])
                        REFERENCES [dbo].[Books] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [CK_ReadingSessions_Positive] CHECK ([DurationMinutes] > 0 AND [PagesRead] >= 0)
                );
                CREATE NONCLUSTERED INDEX [IX_ReadingSessions_UserId_Date] ON [dbo].[ReadingSessions]([UserId] ASC, [SessionDate] DESC);
            END",

            // 7. Tạo bảng SupportChatMessages
            @"
            IF OBJECT_ID(N'[dbo].[SupportChatMessages]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SupportChatMessages] (
                    [Id]             INT IDENTITY(1,1) NOT NULL,
                    [SenderUserId]   NVARCHAR(450)     NOT NULL,
                    [ReceiverUserId] NVARCHAR(450)     NULL,
                    [Channel]        NVARCHAR(30)      NOT NULL CONSTRAINT [DF_SupportChatMessages_Channel] DEFAULT (N'Librarian'),
                    [MessageText]    NVARCHAR(2000)    NOT NULL,
                    [IsRead]         BIT               NOT NULL CONSTRAINT [DF_SupportChatMessages_IsRead] DEFAULT (0),
                    [CreatedAt]      DATETIME2(7)      NOT NULL CONSTRAINT [DF_SupportChatMessages_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT [PK_SupportChatMessages] PRIMARY KEY CLUSTERED ([Id] ASC),
                    CONSTRAINT [FK_SupportChatMessages_Sender] FOREIGN KEY ([SenderUserId])
                        REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_SupportChatMessages_Receiver] FOREIGN KEY ([ReceiverUserId])
                        REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
                );
                CREATE NONCLUSTERED INDEX [IX_SupportChatMessages_Channel_CreatedAt] ON [dbo].[SupportChatMessages]([Channel] ASC, [CreatedAt] DESC);
            END",

            // 8. Đồng bộ dữ liệu phí thuê & Ví mẫu nếu chưa thiết lập (chạy an toàn, không ghi đè dữ liệu đã có)
            @"
            IF OBJECT_ID(N'[dbo].[BookCopies]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [dbo].[BookCopies] WHERE [DailyRentalFee] > 0 OR [DepositAmount] > 0)
            BEGIN
                UPDATE [dbo].[BookCopies]
                SET [DailyRentalFee] = 2000, [DepositAmount] = 50000
                WHERE [OwnerType] = N'System' AND [Id] IN (3, 6, 7, 15, 24, 51, 52, 53, 54, 55);

                UPDATE [dbo].[BookCopies]
                SET [DailyRentalFee] = CASE WHEN [Id] % 2 = 0 THEN 3000 ELSE 2000 END,
                    [DepositAmount]  = 50000
                WHERE [OwnerType] = N'User';
            END",

            @"
            IF OBJECT_ID(N'[dbo].[UserWallets]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
            BEGIN
                INSERT INTO [dbo].[UserWallets] ([UserId], [Balance], [PinHash])
                SELECT u.[Id], 500000, N'8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92'
                FROM [dbo].[AspNetUsers] u
                WHERE NOT EXISTS (SELECT 1 FROM [dbo].[UserWallets] w WHERE w.[UserId] = u.[Id]);
            END"
        };

        try
        {
            foreach (var sql in batches)
            {
                await db.Database.ExecuteSqlRawAsync(sql);
            }
            logger?.LogInformation("[BS-RULE-SCHEMA-ENSURER] Đã xác minh & đồng bộ hoàn tất lược đồ BookShareDb mở rộng.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[BS-RULE-SCHEMA-ENSURER] Bỏ qua đồng bộ schema tự động lúc khởi động: {Message}", ex.Message);
        }
    }
}
