using System.Text.RegularExpressions;
using BookShare.Web.Data;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S22AdminAnalyticsService : IS22AdminAnalyticsService
{
    private readonly BookShareDbContext _db;

    public S22AdminAnalyticsService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S22AdminAnalyticsPageViewModel> GetAdminAnalyticsPageAsync(
        string currentUserId,
        string currentUserRole)
    {
        var nowOffset = DateTimeOffset.UtcNow;

        var totalUsers = await _db.AspNetUsers.CountAsync();
        var totalBooks = await _db.Books.CountAsync();
        var totalCopies = await _db.BookCopies.CountAsync();
        var totalLoans = await _db.Loans.CountAsync();

        var libraryLoansCount = await _db.LoanRequests.CountAsync(lr => lr.LoanType == "Library");
        var p2pLoansCount = await _db.LoanRequests.CountAsync(lr => lr.LoanType == "P2P");

        var txs = await _db.Transactions.AsNoTracking().ToListAsync();
        var membershipRev = txs
            .Where(t => t.TransactionType == "Membership" && t.PaymentStatus == "Completed")
            .Sum(t => t.Amount);
        var fineRev = txs
            .Where(t => t.TransactionType == "Fine" && t.PaymentStatus == "Completed")
            .Sum(t => t.Amount);
        var depositTotal = txs
            .Where(t => t.TransactionType == "SimulatedDeposit")
            .Sum(t => t.Amount);

        var openDisputes = await _db.Disputes.CountAsync(d => d.Status == "Open" || d.Status == "UnderReview");
        var totalAiMsgs = await _db.AiChatMessages.CountAsync();

        var copies = await _db.BookCopies.AsNoTracking().ToListAsync();
        int availCopies = copies.Count(c => c.Status == "Available");
        int borrowedCopies = copies.Count(c => c.Status == "Borrowed" || c.Status == "InTransit");
        int reservedCopies = copies.Count(c => c.Status == "Reserved");
        int damagedLostCopies = copies.Count(c => c.Status == "Damaged" || c.Status == "Lost");

        var usersRaw = await _db.AspNetUsers
            .AsNoTracking()
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .OrderByDescending(u => u.TrustScore)
            .ToListAsync();

        var userItems = usersRaw.Select(u =>
        {
            var roleName = u.Roles.FirstOrDefault()?.Name ?? "User";
            var activeMem = u.Memberships
                .Where(m => m.IsActive)
                .OrderByDescending(m => m.StartDate)
                .FirstOrDefault();

            return new S22AdminUserItemDto
            {
                UserId = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? string.Empty,
                RoleName = roleName,
                TrustScore = u.TrustScore,
                LendQuotaBonus = u.LendQuotaBonus,
                MembershipTier = activeMem?.Tier ?? "Free",
                IsLockedOut = u.LockoutEnd.HasValue && u.LockoutEnd.Value > nowOffset,
                CreatedAt = u.CreatedAt
            };
        }).ToList();

        // Tổng hợp Nhật ký thao tác hệ thống (System Audit Logs)
        var auditLogs = new List<S22SystemAuditLogDto>();

        var recentTxs = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.User)
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .ToListAsync();
        foreach (var t in recentTxs)
        {
            auditLogs.Add(new S22SystemAuditLogDto
            {
                Category = "TRANSACTION",
                BadgeClass = "bs-badge-success",
                ActorName = t.User.FullName,
                Summary = $"Giao dịch {t.TransactionType} ({t.Amount:N0}đ) qua {t.PaymentGateway} [{t.PaymentStatus}]",
                Timestamp = t.CreatedAt
            });
        }

        var recentDisputes = await _db.Disputes
            .AsNoTracking()
            .Include(d => d.RaisedByUser)
            .OrderByDescending(d => d.CreatedAt)
            .Take(4)
            .ToListAsync();
        foreach (var d in recentDisputes)
        {
            auditLogs.Add(new S22SystemAuditLogDto
            {
                Category = "DISPUTE",
                BadgeClass = "bs-badge-danger",
                ActorName = d.RaisedByUser.FullName,
                Summary = $"Hồ sơ tranh chấp #DSP-{d.Id:D4} (Loan #{d.LoanId}) — Trạng thái: {d.Status}",
                Timestamp = d.CreatedAt
            });
        }

        var recentNotifs = await _db.Notifications
            .AsNoTracking()
            .Include(n => n.User)
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .ToListAsync();
        foreach (var n in recentNotifs)
        {
            auditLogs.Add(new S22SystemAuditLogDto
            {
                Category = n.EmailSentViaResend ? "RESEND-MAIL" : "NOTIFICATION",
                BadgeClass = "bs-badge-info",
                ActorName = n.User.FullName,
                Summary = $"{n.Title} ({n.Type})",
                Timestamp = n.CreatedAt
            });
        }

        auditLogs = auditLogs
            .OrderByDescending(a => a.Timestamp)
            .Take(12)
            .ToList();

        var jobs = new List<S22BackgroundJobInfoDto>
        {
            new()
            {
                JobKey = "DueReminder",
                JobName = "SendDueDateRemindersJobAsync",
                CronSchedule = "0 7 * * * (07:00 hằng ngày)",
                Description = "Quét hợp đồng sắp hết hạn/quá hạn, tự động gửi Email nhắc hạn qua Resend API & lưu Notifications.",
                Status = "Active • Hangfire Recurring"
            },
            new()
            {
                JobKey = "OverdueFine",
                JobName = "CalculateOverdueFinesJobAsync",
                CronSchedule = "5 0 * * * (00:05 hằng ngày)",
                Description = "Tự động chuyển trạng thái Overdue và tính phí phạt trễ hạn luỹ kế 5.000 VNĐ/ngày.",
                Status = "Active • Hangfire Recurring"
            },
            new()
            {
                JobKey = "ExpireCleanup",
                JobName = "CancelExpiredRequestsAndReservationsJobAsync",
                CronSchedule = "*/30 * * * * (Mỗi 30 phút)",
                Description = "Tự động hủy yêu cầu mượn (LoanRequests) treo quá 48h không duyệt và lượt giữ chỗ hàng chờ hết hạn.",
                Status = "Active • Hangfire Recurring"
            }
        };

        return new S22AdminAnalyticsPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserRole = currentUserRole,
            TotalUsers = totalUsers,
            TotalBooks = totalBooks,
            TotalCopies = totalCopies,
            TotalLoans = totalLoans,
            LibraryLoansCount = libraryLoansCount,
            P2PLoansCount = p2pLoansCount,
            TotalMembershipRevenue = membershipRev,
            TotalFineRevenue = fineRev,
            TotalSimulatedDeposits = depositTotal,
            TotalOpenDisputes = openDisputes,
            TotalAiMessages = totalAiMsgs,
            AvailableCopiesCount = availCopies,
            BorrowedCopiesCount = borrowedCopies,
            ReservedCopiesCount = reservedCopies,
            DamagedOrLostCopiesCount = damagedLostCopies,
            Users = userItems,
            AuditLogs = auditLogs,
            BackgroundJobs = jobs
        };
    }

    public async Task<(bool Success, string Message)> ChangeUserRoleAsync(
        string adminUserId,
        S22ChangeUserRoleDto request)
    {
        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == request.UserId);

        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản người dùng cần phân quyền.");
        }

        var allowedRoles = new[] { "User", "Curator", "Librarian", "Admin" };
        string targetRoleName = allowedRoles.Contains(request.NewRole) ? request.NewRole : "User";

        var targetRole = await _db.AspNetRoles
            .FirstOrDefaultAsync(r => r.Name == targetRoleName);

        if (targetRole == null)
        {
            return (false, $"Không tìm thấy vai trò {targetRoleName} trong bảng AspNetRoles.");
        }

        user.Roles.Clear();
        user.Roles.Add(targetRole);
        await _db.SaveChangesAsync();

        return (true,
            $"Admin đã cập nhật phân quyền Role của tài khoản {user.FullName} ({user.Email}) sang [{targetRoleName}] thành công!");
    }

    public async Task<(bool Success, string Message)> ToggleUserLockoutAsync(
        string adminUserId,
        S22ToggleUserLockDto request)
    {
        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản cần khóa/mở khóa.");
        }

        if (user.Id == "u-admin-01" && request.LockAccount)
        {
            return (false, "Không được phép khóa tài khoản Quản trị viên gốc (u-admin-01).");
        }

        if (request.LockAccount)
        {
            user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(5);
            await _db.SaveChangesAsync();
            return (true, $"Đã KHÓA tạm thời tài khoản {user.FullName} ({user.Email})!");
        }

                user.LockoutEnd = null;
                user.AccessFailedCount = 0;
                await _db.SaveChangesAsync();
                return (true, $"Đã MỞ KHÓA tài khoản {user.FullName} ({user.Email}) thành công!");
            }

            public async Task<(bool Success, string Message)> ManageSampleDataAsync(
                string adminUserId,
                string actionMode)
            {
                string cleanMode = string.IsNullOrWhiteSpace(actionMode) ? "SeedSampleData" : actionMode.Trim();

                const string clearTransactionsSql = @"
                    DELETE FROM [dbo].[AiChatMessages];
                    DELETE FROM [dbo].[AiChatSessions];
                    DELETE FROM [dbo].[Notifications];
                    DELETE FROM [dbo].[Reviews];
                    DELETE FROM [dbo].[Disputes];
                    DELETE FROM [dbo].[Transactions];
                    DELETE FROM [dbo].[ConditionReports];
                    DELETE FROM [dbo].[Reservations];
                    DELETE FROM [dbo].[Loans];
                    DELETE FROM [dbo].[LoanRequests];
                    DELETE FROM [dbo].[BookNotes];
                    DELETE FROM [dbo].[ReadingProgresses];
                    DELETE FROM [dbo].[OtpVerifications];
                    UPDATE [dbo].[BookCopies] SET [Status] = N'Available';
                ";

                if (cleanMode == "ResetTransactions")
                {
                    await _db.Database.ExecuteSqlRawAsync(clearTransactionsSql);
                    return (true, "Đã làm sạch toàn bộ dữ liệu giao dịch (Đơn mượn, Hợp đồng, Biên bản ảnh, Tranh chấp, Đánh giá, Thông báo, Tiến độ đọc) và đặt lại 100% bản sao sách về trạng thái Available!");
                }

                if (cleanMode == "FactoryClean")
                {
                    await _db.Database.ExecuteSqlRawAsync(clearTransactionsSql);

                    const string clearCatalogSql = @"
                        DELETE FROM [dbo].[ShelfItems];
                        DELETE FROM [dbo].[Shelves];
                        DELETE FROM [dbo].[BookCopies];
                        DELETE FROM [dbo].[Books];
                        DELETE FROM [dbo].[Memberships];
                        DELETE FROM [dbo].[AspNetUserLogins];
                        DELETE FROM [dbo].[AspNetUserClaims];
                        DELETE FROM [dbo].[AspNetUserTokens];
                        IF EXISTS (SELECT 1 FROM [dbo].[AspNetUsers] WHERE [Id] = N'u-admin-01')
                        BEGIN
                            UPDATE [dbo].[Libraries] SET [ManagerUserId] = N'u-admin-01';
                        END
                        DELETE FROM [dbo].[AspNetUserRoles] WHERE [UserId] IN (N'u-librarian-01', N'u-curator-01', N'u-user-01', N'u-user-02', N'u-user-03');
                        DELETE FROM [dbo].[AspNetUsers] WHERE [Id] IN (N'u-librarian-01', N'u-curator-01', N'u-user-01', N'u-user-02', N'u-user-03');
                    ";

                    await _db.Database.ExecuteSqlRawAsync(clearCatalogSql);
                    return (true, "Đã làm sạch toàn bộ dữ liệu mẫu! Hệ thống hiện ở trạng thái CSDL Trắng (chỉ giữ lại 4 Roles gốc, 8 Thể loại, 3 Điểm trạm và tài khoản Quản trị viên).");
                }

                var candidatePaths = new[]
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "database", "02_BookShare_SeedData.sql"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "database", "02_BookShare_SeedData.sql"),
                    Path.Combine(AppContext.BaseDirectory, "database", "02_BookShare_SeedData.sql"),
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "database", "02_BookShare_SeedData.sql"),
                    "/sql/02_BookShare_SeedData.sql"
                };

                string? resolvedSqlPath = candidatePaths.FirstOrDefault(File.Exists);
                if (string.IsNullOrWhiteSpace(resolvedSqlPath))
                {
                    return (false, "Không tìm thấy tệp kịch bản database/02_BookShare_SeedData.sql trên máy chủ.");
                }

                await _db.Database.ExecuteSqlRawAsync(clearTransactionsSql);
                const string prepareForReseedSql = @"
                    DELETE FROM [dbo].[ShelfItems];
                    DELETE FROM [dbo].[Shelves];
                    DELETE FROM [dbo].[BookCopies];
                    DELETE FROM [dbo].[Books];
                    DELETE FROM [dbo].[Libraries];
                    DELETE FROM [dbo].[Memberships];
                    DELETE FROM [dbo].[AspNetUserRoles] WHERE [UserId] IN (N'u-librarian-01', N'u-curator-01', N'u-user-01', N'u-user-02', N'u-user-03');
                    DELETE FROM [dbo].[AspNetUsers] WHERE [Id] IN (N'u-librarian-01', N'u-curator-01', N'u-user-01', N'u-user-02', N'u-user-03');
                ";
                await _db.Database.ExecuteSqlRawAsync(prepareForReseedSql);

                string sqlContent = await File.ReadAllTextAsync(resolvedSqlPath);
                var batches = Regex.Split(sqlContent, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

                foreach (var rawBatch in batches)
                {
                    var batch = rawBatch.Trim();
                    if (string.IsNullOrWhiteSpace(batch))
                    {
                        continue;
                    }
                    if (batch.StartsWith("USE [BookShareDb]", StringComparison.OrdinalIgnoreCase) ||
                        batch.StartsWith("PRINT ", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    await _db.Database.ExecuteSqlRawAsync(batch);
                }

                int booksCount = await _db.Books.CountAsync();
                int copiesCount = await _db.BookCopies.CountAsync();
                int usersCount = await _db.AspNetUsers.CountAsync();

                return (true, $"Đã nạp thành công bộ dữ liệu mẫu chuẩn từ tệp 02_BookShare_SeedData.sql ({booksCount} đầu sách, {copiesCount} bản sao vật lý, {usersCount} tài khoản 4 cấp phân quyền)!");
            }
        }
