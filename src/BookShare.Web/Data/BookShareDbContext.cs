using System;
using System.Collections.Generic;
using BookShare.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Data;

public partial class BookShareDbContext : DbContext
{
    public BookShareDbContext(DbContextOptions<BookShareDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AiChatMessage> AiChatMessages { get; set; }

    public virtual DbSet<AiChatSession> AiChatSessions { get; set; }

    public virtual DbSet<AspNetRole> AspNetRoles { get; set; }

    public virtual DbSet<AspNetRoleClaim> AspNetRoleClaims { get; set; }

    public virtual DbSet<AspNetUser> AspNetUsers { get; set; }

    public virtual DbSet<AspNetUserClaim> AspNetUserClaims { get; set; }

    public virtual DbSet<AspNetUserLogin> AspNetUserLogins { get; set; }

    public virtual DbSet<AspNetUserToken> AspNetUserTokens { get; set; }

    public virtual DbSet<Book> Books { get; set; }

    public virtual DbSet<BookCopy> BookCopies { get; set; }

    public virtual DbSet<BookNote> BookNotes { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<ConditionReport> ConditionReports { get; set; }

    public virtual DbSet<Dispute> Disputes { get; set; }

    public virtual DbSet<Library> Libraries { get; set; }

    public virtual DbSet<Loan> Loans { get; set; }

    public virtual DbSet<LoanRequest> LoanRequests { get; set; }

    public virtual DbSet<Membership> Memberships { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<OtpVerification> OtpVerifications { get; set; }

    public virtual DbSet<ReadingProgress> ReadingProgresses { get; set; }

    public virtual DbSet<Reservation> Reservations { get; set; }

    public virtual DbSet<Review> Reviews { get; set; }

    public virtual DbSet<Shelf> Shelves { get; set; }

    public virtual DbSet<ShelfItem> ShelfItems { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    // [BS-RULE-SCHEMA-V2] 4 DbSet mới cho QR Cross-Login, Ví Bảo Lãnh, Reading Studio & Chat Thủ Thư
    public virtual DbSet<QrLoginSession> QrLoginSessions { get; set; }

    public virtual DbSet<UserWallet> UserWallets { get; set; }

    public virtual DbSet<ReadingSession> ReadingSessions { get; set; }

    public virtual DbSet<SupportChatMessage> SupportChatMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiChatMessage>(entity =>
        {
            entity.HasIndex(e => new { e.SessionId, e.CreatedAt }, "IX_AiChatMessages_SessionId");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_AiChatMessages_CreatedAt");
            entity.Property(e => e.ReferencedBookIdsJson).HasMaxLength(500);
            entity.Property(e => e.Sender).HasMaxLength(20);
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.Session).WithMany(p => p.AiChatMessages)
                .HasForeignKey(d => d.SessionId)
                .HasConstraintName("FK_AiChatMessages_AiChatSessions");

            entity.HasOne(d => d.User).WithMany(p => p.AiChatMessages)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AiChatMessages_AspNetUsers");
        });

        modelBuilder.Entity<AiChatSession>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.UpdatedAt }, "IX_AiChatSessions_UserId").IsDescending(false, true);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_AiChatSessions_CreatedAt");
            entity.Property(e => e.ModelName)
                .HasMaxLength(50)
                .HasDefaultValue("gemini-2.5-flash", "DF_AiChatSessions_Model");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasDefaultValue("Tư vấn chọn sách & mượn trả", "DF_AiChatSessions_Title");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_AiChatSessions_UpdatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.AiChatSessions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_AiChatSessions_AspNetUsers");
        });

        modelBuilder.Entity<AspNetRole>(entity =>
        {
            entity.HasIndex(e => e.NormalizedName, "RoleNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedName] IS NOT NULL)");

            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.NormalizedName).HasMaxLength(256);
        });

        modelBuilder.Entity<AspNetRoleClaim>(entity =>
        {
            entity.Property(e => e.RoleId).HasMaxLength(450);

            entity.HasOne(d => d.Role).WithMany(p => p.AspNetRoleClaims).HasForeignKey(d => d.RoleId);
        });

        modelBuilder.Entity<AspNetUser>(entity =>
        {
            entity.HasIndex(e => e.NormalizedEmail, "EmailIndex");

            entity.HasIndex(e => e.NormalizedUserName, "UserNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedUserName] IS NOT NULL)");

            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_AspNetUsers_CreatedAt");
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.LockoutEnabled).HasDefaultValue(true, "DF_AspNetUsers_LockoutEnabled");
            entity.Property(e => e.NormalizedEmail).HasMaxLength(256);
            entity.Property(e => e.NormalizedUserName).HasMaxLength(256);
            entity.Property(e => e.PhoneNumber).HasMaxLength(30);
            entity.Property(e => e.TrustScore).HasDefaultValue(100, "DF_AspNetUsers_TrustScore");
            entity.Property(e => e.UserName).HasMaxLength(256);

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "AspNetUserRole",
                    r => r.HasOne<AspNetRole>().WithMany().HasForeignKey("RoleId"),
                    l => l.HasOne<AspNetUser>().WithMany().HasForeignKey("UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.ToTable("AspNetUserRoles");
                        j.HasIndex(new[] { "RoleId" }, "IX_AspNetUserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<AspNetUserClaim>(entity =>
        {
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserClaims).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserLogin>(entity =>
        {
            entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });

            entity.HasIndex(e => e.UserId, "IX_AspNetUserLogins_UserId");

            entity.Property(e => e.LoginProvider).HasMaxLength(128);
            entity.Property(e => e.ProviderKey).HasMaxLength(128);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserLogins).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserToken>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.LoginProvider, e.Name });

            entity.Property(e => e.LoginProvider).HasMaxLength(128);
            entity.Property(e => e.Name).HasMaxLength(128);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserTokens).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<Book>(entity =>
        {
            entity.HasIndex(e => e.CategoryId, "IX_Books_CategoryId");

            entity.HasIndex(e => new { e.Title, e.Author }, "IX_Books_Title_Author");

            entity.HasIndex(e => e.Isbn, "UQ_Books_ISBN").IsUnique();

            entity.Property(e => e.Author).HasMaxLength(200);
            entity.Property(e => e.CoverImageUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Books_CreatedAt");
            entity.Property(e => e.Isbn)
                .HasMaxLength(20)
                .HasColumnName("ISBN");
            entity.Property(e => e.PageCount).HasDefaultValue(200, "DF_Books_PageCount");
            entity.Property(e => e.Publisher).HasMaxLength(150);
            entity.Property(e => e.Title).HasMaxLength(250);

            entity.HasOne(d => d.Category).WithMany(p => p.Books)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Books_Categories");
        });

        modelBuilder.Entity<BookCopy>(entity =>
        {
            entity.HasIndex(e => new { e.BookId, e.Status, e.IsLendable }, "IX_BookCopies_BookId_Status");

            entity.HasIndex(e => e.LibraryId, "IX_BookCopies_LibraryId");

            entity.HasIndex(e => e.OwnerUserId, "IX_BookCopies_OwnerUserId");

            entity.HasIndex(e => e.Barcode, "UQ_BookCopies_Barcode").IsUnique();

            entity.Property(e => e.Barcode).HasMaxLength(50);
            entity.Property(e => e.Condition)
                .HasMaxLength(20)
                .HasDefaultValue("Good", "DF_BookCopies_Condition");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_BookCopies_CreatedAt");
            entity.Property(e => e.IsLendable).HasDefaultValue(true, "DF_BookCopies_IsLendable");
            entity.Property(e => e.DailyRentalFee).HasColumnType("decimal(18, 2)").HasDefaultValue(0m, "DF_BookCopies_DailyRentalFee");
            entity.Property(e => e.DepositAmount).HasColumnType("decimal(18, 2)").HasDefaultValue(0m, "DF_BookCopies_DepositAmount");
            entity.Property(e => e.OwnerType).HasMaxLength(20);
            entity.Property(e => e.ShelfLocation).HasMaxLength(80);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Available", "DF_BookCopies_Status");

            entity.HasOne(d => d.Book).WithMany(p => p.BookCopies)
                .HasForeignKey(d => d.BookId)
                .HasConstraintName("FK_BookCopies_Books");

            entity.HasOne(d => d.Library).WithMany(p => p.BookCopies)
                .HasForeignKey(d => d.LibraryId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_BookCopies_Libraries");

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.BookCopies)
                .HasForeignKey(d => d.OwnerUserId)
                .HasConstraintName("FK_BookCopies_AspNetUsers_Owner");
        });

        modelBuilder.Entity<BookNote>(entity =>
        {
            entity.HasIndex(e => new { e.ReadingProgressId, e.PageNumber }, "IX_BookNotes_ReadingProgressId");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_BookNotes_CreatedAt");
            entity.Property(e => e.NoteContent).HasMaxLength(2000);
            entity.Property(e => e.PageNumber).HasDefaultValue(1, "DF_BookNotes_PageNumber");
            entity.Property(e => e.QuoteText).HasMaxLength(1000);

            entity.HasOne(d => d.ReadingProgress).WithMany(p => p.BookNotes)
                .HasForeignKey(d => d.ReadingProgressId)
                .HasConstraintName("FK_BookNotes_ReadingProgresses");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(e => e.Slug, "UQ_Categories_Slug").IsUnique();

            entity.Property(e => e.IconClass)
                .HasMaxLength(80)
                .HasDefaultValue("bi-book", "DF_Categories_IconClass");
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Slug).HasMaxLength(120);
        });

        modelBuilder.Entity<ConditionReport>(entity =>
        {
            entity.HasIndex(e => new { e.LoanId, e.ReportType }, "IX_ConditionReports_LoanId");

            entity.Property(e => e.ConditionGrade).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ConditionReports_CreatedAt");
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.Note).HasMaxLength(1000);
            entity.Property(e => e.ReportType).HasMaxLength(20);
            entity.Property(e => e.ReportedByUserId).HasMaxLength(450);

            entity.HasOne(d => d.Loan).WithMany(p => p.ConditionReports)
                .HasForeignKey(d => d.LoanId)
                .HasConstraintName("FK_ConditionReports_Loans");

            entity.HasOne(d => d.ReportedByUser).WithMany(p => p.ConditionReports)
                .HasForeignKey(d => d.ReportedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ConditionReports_AspNetUsers");
        });

        modelBuilder.Entity<Dispute>(entity =>
        {
            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_Disputes_Status").IsDescending(false, true);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Disputes_CreatedAt");
            entity.Property(e => e.RaisedByUserId).HasMaxLength(450);
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.Property(e => e.ResolutionNote).HasMaxLength(1500);
            entity.Property(e => e.ResolvedByAdminId).HasMaxLength(450);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Open", "DF_Disputes_Status");

            entity.HasOne(d => d.Loan).WithMany(p => p.Disputes)
                .HasForeignKey(d => d.LoanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Disputes_Loans");

            entity.HasOne(d => d.RaisedByUser).WithMany(p => p.DisputeRaisedByUsers)
                .HasForeignKey(d => d.RaisedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Disputes_AspNetUsers_RaisedBy");

            entity.HasOne(d => d.ResolvedByAdmin).WithMany(p => p.DisputeResolvedByAdmins)
                .HasForeignKey(d => d.ResolvedByAdminId)
                .HasConstraintName("FK_Disputes_AspNetUsers_ResolvedBy");
        });

        modelBuilder.Entity<Library>(entity =>
        {
            entity.Property(e => e.Address).HasMaxLength(350);
            entity.Property(e => e.HubType)
                .HasMaxLength(30)
                .HasDefaultValue("Library", "DF_Libraries_HubType");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Libraries_IsActive");
            entity.Property(e => e.ManagerUserId).HasMaxLength(450);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.OpenHours)
                .HasMaxLength(120)
                .HasDefaultValue("08:00 - 17:30 (T2 - T7)", "DF_Libraries_OpenHours");
            entity.Property(e => e.Phone).HasMaxLength(30);

            entity.HasOne(d => d.ManagerUser).WithMany(p => p.Libraries)
                .HasForeignKey(d => d.ManagerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Libraries_AspNetUsers_Manager");
        });

        modelBuilder.Entity<Loan>(entity =>
        {
            entity.HasIndex(e => new { e.Status, e.DueDate }, "IX_Loans_Status_DueDate");

            entity.HasIndex(e => e.LoanRequestId, "UQ_Loans_LoanRequestId").IsUnique();

            entity.Property(e => e.FineAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.RentalFeeTotal).HasColumnType("decimal(18, 2)").HasDefaultValue(0m, "DF_Loans_RentalFeeTotal");
            entity.Property(e => e.DepositHeld).HasColumnType("decimal(18, 2)").HasDefaultValue(0m, "DF_Loans_DepositHeld");
            entity.Property(e => e.EscrowStatus).HasMaxLength(30).HasDefaultValue("None", "DF_Loans_EscrowStatus");
            entity.Property(e => e.StartDate).HasDefaultValueSql("(sysutcdatetime())", "DF_Loans_StartDate");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active", "DF_Loans_Status");

            entity.HasOne(d => d.LoanRequest).WithOne(p => p.Loan)
                .HasForeignKey<Loan>(d => d.LoanRequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Loans_LoanRequests");
        });

        modelBuilder.Entity<LoanRequest>(entity =>
        {
            entity.HasIndex(e => new { e.BookCopyId, e.Status }, "IX_LoanRequests_BookCopyId_Status");

            entity.HasIndex(e => new { e.BorrowerId, e.Status }, "IX_LoanRequests_BorrowerId_Status");

            entity.Property(e => e.ApproverId).HasMaxLength(450);
            entity.Property(e => e.DesiredDays).HasDefaultValue(14, "DF_LoanRequests_DesiredDays");
            entity.Property(e => e.LoanType).HasMaxLength(20);
            entity.Property(e => e.RequestNote).HasMaxLength(500);
            entity.Property(e => e.RequestedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_LoanRequests_RequestedAt");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending", "DF_LoanRequests_Status");

            entity.HasOne(d => d.Approver).WithMany(p => p.LoanRequestApprovers)
                .HasForeignKey(d => d.ApproverId)
                .HasConstraintName("FK_LoanRequests_AspNetUsers_Approver");

            entity.HasOne(d => d.BookCopy).WithMany(p => p.LoanRequests)
                .HasForeignKey(d => d.BookCopyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LoanRequests_BookCopies");

            entity.HasOne(d => d.Borrower).WithMany(p => p.LoanRequestBorrowers)
                .HasForeignKey(d => d.BorrowerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LoanRequests_AspNetUsers_Borrower");

            entity.HasOne(d => d.PickupHub).WithMany(p => p.LoanRequests)
                .HasForeignKey(d => d.PickupHubId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_LoanRequests_Libraries_PickupHub");
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.IsActive }, "IX_Memberships_UserId_Active");

            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Memberships_IsActive");
            entity.Property(e => e.MaxConcurrentLoans).HasDefaultValue(2, "DF_Memberships_MaxLoans");
            entity.Property(e => e.StartDate).HasDefaultValueSql("(sysutcdatetime())", "DF_Memberships_StartDate");
            entity.Property(e => e.Tier)
                .HasMaxLength(20)
                .HasDefaultValue("Free", "DF_Memberships_Tier");

            entity.HasOne(d => d.User).WithMany(p => p.Memberships)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Memberships_AspNetUsers");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "IX_Notifications_UserId_IsRead").IsDescending(false, false, true);

            entity.Property(e => e.Content).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Notifications_CreatedAt");
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Type)
                .HasMaxLength(30)
                .HasDefaultValue("System", "DF_Notifications_Type");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Notifications_AspNetUsers");
        });

        modelBuilder.Entity<OtpVerification>(entity =>
        {
            entity.HasIndex(e => new { e.Email, e.Purpose, e.IsUsed }, "IX_OtpVerifications_Email_Purpose");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_OtpVerifications_CreatedAt");
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.OtpCodeHash).HasMaxLength(128);
            entity.Property(e => e.Purpose).HasMaxLength(30);
            entity.Property(e => e.ResendMessageId).HasMaxLength(100);
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.User).WithMany(p => p.OtpVerifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_OtpVerifications_AspNetUsers");
        });

        modelBuilder.Entity<ReadingProgress>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.Status }, "IX_ReadingProgresses_UserId_Status");

            entity.HasIndex(e => new { e.UserId, e.BookId }, "UQ_ReadingProgresses_User_Book").IsUnique();

            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .HasDefaultValue("Medium", "DF_ReadingProgresses_Priority");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("WantToRead", "DF_ReadingProgresses_Status");
            entity.Property(e => e.TotalPages).HasDefaultValue(200, "DF_ReadingProgresses_TotalPages");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ReadingProgresses_UpdatedAt");

            entity.HasOne(d => d.Book).WithMany(p => p.ReadingProgresses)
                .HasForeignKey(d => d.BookId)
                .HasConstraintName("FK_ReadingProgresses_Books");

            entity.HasOne(d => d.User).WithMany(p => p.ReadingProgresses)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_ReadingProgresses_AspNetUsers");
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasIndex(e => new { e.BookId, e.Status, e.QueuePosition }, "IX_Reservations_BookId_Queue");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Reservations_CreatedAt");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Waiting", "DF_Reservations_Status");
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.Book).WithMany(p => p.Reservations)
                .HasForeignKey(d => d.BookId)
                .HasConstraintName("FK_Reservations_Books");

            entity.HasOne(d => d.User).WithMany(p => p.Reservations)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Reservations_AspNetUsers");
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "IX_Reviews_Target");

            entity.Property(e => e.Comment).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Reviews_CreatedAt");
            entity.Property(e => e.ReviewerId).HasMaxLength(450);
            entity.Property(e => e.TargetType).HasMaxLength(20);

            entity.HasOne(d => d.Loan).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.LoanId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Reviews_Loans");

            entity.HasOne(d => d.Reviewer).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.ReviewerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Reviews_AspNetUsers_Reviewer");
        });

        modelBuilder.Entity<Shelf>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.DisplayOrder }, "IX_Shelves_UserId");

            entity.Property(e => e.Description).HasMaxLength(300);
            entity.Property(e => e.DisplayOrder).HasDefaultValue(1, "DF_Shelves_DisplayOrder");
            entity.Property(e => e.IsPublic).HasDefaultValue(true, "DF_Shelves_IsPublic");
            entity.Property(e => e.Name).HasMaxLength(100);

            entity.HasOne(d => d.User).WithMany(p => p.Shelves)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Shelves_AspNetUsers");
        });

        modelBuilder.Entity<ShelfItem>(entity =>
        {
            entity.HasIndex(e => new { e.ShelfId, e.BookCopyId }, "UQ_ShelfItems_Shelf_BookCopy").IsUnique();

            entity.Property(e => e.AddedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ShelfItems_AddedAt");

            entity.HasOne(d => d.BookCopy).WithMany(p => p.ShelfItems)
                .HasForeignKey(d => d.BookCopyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ShelfItems_BookCopies");

            entity.HasOne(d => d.Shelf).WithMany(p => p.ShelfItems)
                .HasForeignKey(d => d.ShelfId)
                .HasConstraintName("FK_ShelfItems_Shelves");
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_Transactions_UserId_CreatedAt").IsDescending(false, true);

            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Transactions_CreatedAt");
            entity.Property(e => e.HmacSignature).HasMaxLength(256);
            entity.Property(e => e.PaymentGateway)
                .HasMaxLength(30)
                .HasDefaultValue("VNPaySandbox", "DF_Transactions_Gateway");
            entity.Property(e => e.PaymentStatus)
                .HasMaxLength(20)
                .HasDefaultValue("Pending", "DF_Transactions_Status");
            entity.Property(e => e.TransactionType).HasMaxLength(30);

            entity.HasOne(d => d.Loan).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.LoanId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Transactions_Loans");

            entity.HasOne(d => d.User).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Transactions_AspNetUsers");
        });

        // [BS-RULE-SCHEMA-V2] Cấu hình EF Core cho 4 bảng nghiệp vụ mới
        modelBuilder.Entity<QrLoginSession>(entity =>
        {
            entity.HasIndex(e => e.Token, "UQ_QrLoginSessions_Token").IsUnique();
            entity.HasIndex(e => new { e.Token, e.Status }, "IX_QrLoginSessions_Token_Status");

            entity.Property(e => e.Token).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Pending", "DF_QrLoginSessions_Status");
            entity.Property(e => e.ApprovedByUserId).HasMaxLength(450);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_QrLoginSessions_CreatedAt");

            entity.HasOne(d => d.ApprovedByUser).WithMany(p => p.ApprovedQrLoginSessions)
                .HasForeignKey(d => d.ApprovedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_QrLoginSessions_AspNetUsers");
        });

        modelBuilder.Entity<UserWallet>(entity =>
        {
            entity.HasIndex(e => e.UserId, "UQ_UserWallets_UserId").IsUnique();

            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.Balance).HasColumnType("decimal(18, 2)").HasDefaultValue(0m, "DF_UserWallets_Balance");
            entity.Property(e => e.PinHash).HasMaxLength(128).HasDefaultValue("8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92", "DF_UserWallets_PinHash");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_UserWallets_UpdatedAt");

            entity.HasOne(d => d.User).WithOne(p => p.UserWallet)
                .HasForeignKey<UserWallet>(d => d.UserId)
                .HasConstraintName("FK_UserWallets_AspNetUsers");
        });

        modelBuilder.Entity<ReadingSession>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.SessionDate }, "IX_ReadingSessions_UserId_Date").IsDescending(false, true);

            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.DurationMinutes).HasDefaultValue(25, "DF_ReadingSessions_Duration");
            entity.Property(e => e.PagesRead).HasDefaultValue(0, "DF_ReadingSessions_Pages");
            entity.Property(e => e.SessionDate).HasDefaultValueSql("(cast(sysutcdatetime() as date))", "DF_ReadingSessions_Date");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ReadingSessions_CreatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.ReadingSessions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_ReadingSessions_AspNetUsers");

            entity.HasOne(d => d.Book).WithMany(p => p.ReadingSessions)
                .HasForeignKey(d => d.BookId)
                .HasConstraintName("FK_ReadingSessions_Books");
        });

        modelBuilder.Entity<SupportChatMessage>(entity =>
        {
            entity.HasIndex(e => new { e.Channel, e.CreatedAt }, "IX_SupportChatMessages_Channel_CreatedAt").IsDescending(false, true);

            entity.Property(e => e.SenderUserId).HasMaxLength(450);
            entity.Property(e => e.ReceiverUserId).HasMaxLength(450);
            entity.Property(e => e.Channel).HasMaxLength(30).HasDefaultValue("Librarian", "DF_SupportChatMessages_Channel");
            entity.Property(e => e.MessageText).HasMaxLength(2000);
            entity.Property(e => e.IsRead).HasDefaultValue(false, "DF_SupportChatMessages_IsRead");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_SupportChatMessages_CreatedAt");

            entity.HasOne(d => d.SenderUser).WithMany(p => p.SentSupportMessages)
                .HasForeignKey(d => d.SenderUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupportChatMessages_Sender");

            entity.HasOne(d => d.ReceiverUser).WithMany(p => p.ReceivedSupportMessages)
                .HasForeignKey(d => d.ReceiverUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupportChatMessages_Receiver");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
