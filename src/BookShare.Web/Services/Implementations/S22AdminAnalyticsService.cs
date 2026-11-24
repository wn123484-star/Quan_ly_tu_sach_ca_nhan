using System.Data;
using System.Text.RegularExpressions;
using BookShare.Web.Data;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S22AdminAnalyticsService : IS22AdminAnalyticsService
{
    private readonly BookShareDbContext _db;
    private readonly IDatabaseBootstrapService _bootstrap;

    public S22AdminAnalyticsService(
        BookShareDbContext db,
        IDatabaseBootstrapService bootstrap)
    {
        _db = db;
        _bootstrap = bootstrap;
    }

    public async Task<S22AdminAnalyticsPageViewModel> GetAdminAnalyticsPageAsync(
        string currentUserId,
        string currentUserRole)
    {
        if (!string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return new S22AdminAnalyticsPageViewModel
            {
                CurrentUserId = currentUserId,
                CurrentUserRole = currentUserRole
            };
        }

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
                Category = "TRANH CHẤP",
                BadgeClass = "bs-badge-danger",
                ActorName = d.RaisedByUser.FullName,
                Summary = $"Hồ sơ khiếu nại #{d.Id:D4} (Hợp đồng #{d.LoanId}) — Trạng thái: {d.Status}",
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
                Category = n.EmailSentViaResend ? "EMAIL" : "THÔNG BÁO",
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
                JobName = "Nhắc hạn trả sách tự động",
                CronSchedule = "07:00 hằng ngày",
                Description = "Quét hợp đồng sắp hết hạn hoặc quá hạn để gửi email nhắc nhở và thông báo cho bạn đọc.",
                Status = "Đang hoạt động • Tự động"
            },
            new()
            {
                JobKey = "OverdueFine",
                JobName = "Cập nhật quá hạn & phí trễ",
                CronSchedule = "00:05 hằng ngày",
                Description = "Tự động chuyển trạng thái quá hạn và tính phí trễ hạn lũy kế 5.000 VNĐ/ngày.",
                Status = "Đang hoạt động • Tự động"
            },
            new()
            {
                JobKey = "ExpireCleanup",
                JobName = "Giải phóng yêu cầu & giữ chỗ hết hạn",
                CronSchedule = "Mỗi 30 phút",
                Description = "Tự động hủy yêu cầu mượn chờ duyệt quá 48 giờ và chuyển lượt giữ chỗ cho bạn đọc kế tiếp.",
                Status = "Đang hoạt động • Tự động"
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
            PendingRoleUpgrades = await _db.RoleUpgradeRequests
                .AsNoTracking()
                .Include(r => r.User)
                .Where(r => r.Status == "Pending")
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new CuratorUpgradeRequestItemDto
                {
                    Id = r.Id,
                    UserId = r.UserId,
                    UserFullName = r.User.FullName,
                    UserEmail = r.User.Email ?? string.Empty,
                    RequestedRole = r.RequestedRole,
                    OrganizationName = r.OrganizationName,
                    Reason = r.Reason,
                    Status = r.Status,
                    ReviewNote = r.ReviewNote,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync(),
            AuditLogs = auditLogs,
            BackgroundJobs = jobs
        };
    }

    public async Task<(bool Success, string Message)> ReviewRoleUpgradeAsync(
        string adminUserId,
        ReviewRoleUpgradeRequestDto request)
    {
        var upgradeReq = await _db.RoleUpgradeRequests
            .Include(r => r.User)
                .ThenInclude(u => u.Roles)
            .FirstOrDefaultAsync(r => r.Id == request.RequestId);

        if (upgradeReq == null)
        {
            return (false, "Không tìm thấy hồ sơ đề nghị nâng quyền cần xử lý.");
        }

        if (upgradeReq.Status != "Pending")
        {
            return (false, $"Hồ sơ đề nghị #{upgradeReq.Id} đã được xử lý trước đó ({upgradeReq.Status}).");
        }

        var now = DateTime.UtcNow;
        upgradeReq.ReviewedByUserId = adminUserId;
        upgradeReq.ReviewedAt = now;

        if (request.Approve)
        {
            var curatorRole = await _db.AspNetRoles.FirstOrDefaultAsync(r => r.Name == "Curator");
            if (curatorRole == null)
            {
                return (false, "Không tìm thấy vai trò Curator trong hệ thống.");
            }

            upgradeReq.Status = "Approved";
            upgradeReq.ReviewNote = string.IsNullOrWhiteSpace(request.ReviewNote)
                ? "Quản trị viên đã phê duyệt trở thành Chủ nhiệm CLB & Điểm trạm giao nhận."
                : request.ReviewNote.Trim();

            upgradeReq.User.Roles.Clear();
            upgradeReq.User.Roles.Add(curatorRole);

            _db.Notifications.Add(new Models.Entities.Notification
            {
                UserId = upgradeReq.UserId,
                Title = "Chúc mừng! Bạn đã được nâng quyền Chủ nhiệm CLB (Curator)",
                Content = $"Đề nghị phụ trách điểm trạm \"{upgradeReq.OrganizationName}\" đã được phê duyệt. Vui lòng đăng nhập lại để cập nhật đầy đủ quyền điều hành trạm CLB.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            return (true, $"Đã phê duyệt đề nghị #{upgradeReq.Id} và cấp vai trò Curator cho {upgradeReq.User.FullName}!");
        }

        upgradeReq.Status = "Rejected";
        upgradeReq.ReviewNote = string.IsNullOrWhiteSpace(request.ReviewNote)
            ? "Đề nghị hiện chưa đáp ứng đủ điều kiện vận hành điểm trạm sách."
            : request.ReviewNote.Trim();

        _db.Notifications.Add(new Models.Entities.Notification
        {
            UserId = upgradeReq.UserId,
            Title = "Kết quả xét duyệt đề nghị Chủ nhiệm CLB",
            Content = $"Đề nghị cho đơn vị \"{upgradeReq.OrganizationName}\" chưa được chấp thuận. Lý do: {upgradeReq.ReviewNote}",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return (true, $"Đã từ chối đề nghị nâng quyền #{upgradeReq.Id} của {upgradeReq.User.FullName}.");
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
            return (false, $"Không tìm thấy vai trò {targetRoleName} trong hệ thống.");
        }

        user.Roles.Clear();
        user.Roles.Add(targetRole);
        await _db.SaveChangesAsync();

        return (true,
            $"Quản trị viên đã cập nhật phân quyền của tài khoản {user.FullName} ({user.Email}) sang [{targetRoleName}] thành công!");
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
            return (false, "Không được phép khóa tài khoản Quản trị viên gốc.");
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

    public async Task<(bool Success, string Message)> DeleteUserAsync(
        string adminUserId,
        string targetUserId)
    {
        string cleanTargetId = (targetUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanTargetId))
        {
            return (false, "Thiếu mã định danh tài khoản cần xóa.");
        }

        if (string.Equals(cleanTargetId, adminUserId, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Không thể tự xóa tài khoản Quản trị viên đang đăng nhập.");
        }

        if (string.Equals(cleanTargetId, "u-admin-01", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Không được phép xóa tài khoản Quản trị viên gốc của hệ thống.");
        }

        var targetUser = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == cleanTargetId);

        if (targetUser == null)
        {
            return (false, "Không tìm thấy tài khoản cần xóa trên hệ thống.");
        }

        bool targetIsAdmin = targetUser.Roles.Any(r => r.NormalizedName == "ADMIN");
        if (targetIsAdmin)
        {
            int otherAdmins = await _db.AspNetUsers
                .CountAsync(u => u.Id != cleanTargetId && u.Roles.Any(r => r.NormalizedName == "ADMIN"));
            if (otherAdmins == 0)
            {
                return (false, "Không thể xóa Quản trị viên duy nhất còn lại của hệ thống.");
            }
        }

        bool hasActiveBorrowedLoans = await _db.Loans.AnyAsync(l =>
            l.LoanRequest.BorrowerId == cleanTargetId &&
            (l.Status == "Active" || l.Status == "Overdue" || l.Status == "Disputed"));

        if (hasActiveBorrowedLoans)
        {
            return (false, $"Không thể xóa tài khoản {targetUser.FullName}: Độc giả này vẫn đang giữ sách chưa hoàn trả hoặc đang có hợp đồng tranh chấp. Vui lòng thu hồi sách hoặc dùng chức năng Khóa tài khoản.");
        }

        bool hasLentOutCopies = await _db.BookCopies.AnyAsync(bc =>
            bc.OwnerUserId == cleanTargetId &&
            (bc.Status == "Borrowed" || bc.Status == "Reserved"));

        if (hasLentOutCopies)
        {
            return (false, $"Không thể xóa tài khoản {targetUser.FullName}: Tài khoản này đang có sách P2P cho người khác mượn hoặc đang giữ chỗ. Vui lòng hoàn tất chu trình trả sách trước.");
        }

        bool hasOpenDisputes = await _db.Disputes.AnyAsync(d =>
            (d.RaisedByUserId == cleanTargetId || d.Loan.LoanRequest.BorrowerId == cleanTargetId) &&
            (d.Status == "Open" || d.Status == "UnderReview"));

        if (hasOpenDisputes)
        {
            return (false, $"Không thể xóa tài khoản {targetUser.FullName}: Vẫn còn hồ sơ tranh chấp đang mở chưa phân xử xong.");
        }

        string displayName = targetUser.FullName;
        string displayEmail = targetUser.Email ?? cleanTargetId;

        await using var dbTx = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE [dbo].[Libraries] SET [ManagerUserId] = N'u-admin-01' WHERE [ManagerUserId] = {cleanTargetId};
                UPDATE [dbo].[RoleUpgradeRequests] SET [ReviewedByUserId] = NULL WHERE [ReviewedByUserId] = {cleanTargetId};
                UPDATE [dbo].[Disputes] SET [ResolvedByAdminId] = NULL WHERE [ResolvedByAdminId] = {cleanTargetId};
                UPDATE [dbo].[LoanRequests] SET [ApproverId] = NULL WHERE [ApproverId] = {cleanTargetId};
                IF OBJECT_ID(N'[dbo].[SystemIconSettings]', N'U') IS NOT NULL
                    UPDATE [dbo].[SystemIconSettings] SET [UpdatedByUserId] = NULL WHERE [UpdatedByUserId] = {cleanTargetId};

                DELETE FROM [dbo].[QrLoginSessions] WHERE [InitiatorUserId] = {cleanTargetId} OR [ApprovedUserId] = {cleanTargetId};
                DELETE FROM [dbo].[BookHandoverQrs]
                WHERE [CreatedByUserId] = {cleanTargetId}
                   OR [ConsumedByUserId] = {cleanTargetId}
                   OR [LoanRequestId] IN (SELECT [Id] FROM [dbo].[LoanRequests] WHERE [BorrowerId] = {cleanTargetId} OR [BookCopyId] IN (SELECT [Id] FROM [dbo].[BookCopies] WHERE [OwnerUserId] = {cleanTargetId}))
                   OR [LoanId] IN (SELECT l.[Id] FROM [dbo].[Loans] l INNER JOIN [dbo].[LoanRequests] lr ON l.[LoanRequestId] = lr.[Id] WHERE lr.[BorrowerId] = {cleanTargetId} OR lr.[BookCopyId] IN (SELECT [Id] FROM [dbo].[BookCopies] WHERE [OwnerUserId] = {cleanTargetId}));

                DELETE FROM [dbo].[Disputes]
                WHERE [RaisedByUserId] = {cleanTargetId}
                   OR [LoanId] IN (SELECT l.[Id] FROM [dbo].[Loans] l INNER JOIN [dbo].[LoanRequests] lr ON l.[LoanRequestId] = lr.[Id] WHERE lr.[BorrowerId] = {cleanTargetId} OR lr.[BookCopyId] IN (SELECT [Id] FROM [dbo].[BookCopies] WHERE [OwnerUserId] = {cleanTargetId}));

                DELETE FROM [dbo].[ConditionReports]
                WHERE [ReportedByUserId] = {cleanTargetId}
                   OR [LoanId] IN (SELECT l.[Id] FROM [dbo].[Loans] l INNER JOIN [dbo].[LoanRequests] lr ON l.[LoanRequestId] = lr.[Id] WHERE lr.[BorrowerId] = {cleanTargetId} OR lr.[BookCopyId] IN (SELECT [Id] FROM [dbo].[BookCopies] WHERE [OwnerUserId] = {cleanTargetId}));

                DELETE FROM [dbo].[Reviews]
                WHERE [ReviewerId] = {cleanTargetId}
                   OR ([TargetType] = N'User' AND [TargetId] = {cleanTargetId});

                DELETE FROM [dbo].[Transactions] WHERE [UserId] = {cleanTargetId};

                DELETE FROM [dbo].[Loans]
                WHERE [LoanRequestId] IN (SELECT [Id] FROM [dbo].[LoanRequests] WHERE [BorrowerId] = {cleanTargetId} OR [BookCopyId] IN (SELECT [Id] FROM [dbo].[BookCopies] WHERE [OwnerUserId] = {cleanTargetId}));

                DELETE FROM [dbo].[LoanRequests]
                WHERE [BorrowerId] = {cleanTargetId}
                   OR [BookCopyId] IN (SELECT [Id] FROM [dbo].[BookCopies] WHERE [OwnerUserId] = {cleanTargetId});

                DELETE FROM [dbo].[ShelfItems]
                WHERE [BookCopyId] IN (SELECT [Id] FROM [dbo].[BookCopies] WHERE [OwnerUserId] = {cleanTargetId})
                   OR [ShelfId] IN (SELECT [Id] FROM [dbo].[Shelves] WHERE [UserId] = {cleanTargetId});

                DELETE FROM [dbo].[Shelves] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[BookCopies] WHERE [OwnerUserId] = {cleanTargetId};
                DELETE FROM [dbo].[AiChatMessages]
                WHERE [UserId] = {cleanTargetId}
                   OR [SessionId] IN (SELECT [Id] FROM [dbo].[AiChatSessions] WHERE [UserId] = {cleanTargetId});
                DELETE FROM [dbo].[AiChatSessions] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[BookNotes]
                WHERE [ReadingProgressId] IN (SELECT [Id] FROM [dbo].[ReadingProgresses] WHERE [UserId] = {cleanTargetId});
                DELETE FROM [dbo].[ReadingProgresses] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[Reservations] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[Notifications] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[RoleUpgradeRequests] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[Memberships] WHERE [UserId] = {cleanTargetId};
                IF OBJECT_ID(N'[dbo].[UserWallets]', N'U') IS NOT NULL
                    DELETE FROM [dbo].[UserWallets] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[OtpVerifications] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[AspNetUserRoles] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[AspNetUserLogins] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[AspNetUserClaims] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[AspNetUserTokens] WHERE [UserId] = {cleanTargetId};
                DELETE FROM [dbo].[AspNetUsers] WHERE [Id] = {cleanTargetId};
            ");

            await dbTx.CommitAsync();
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync();
            return (false, $"Không thể xóa tài khoản {displayName} do ràng buộc dữ liệu: {ex.Message}");
        }

        return (true, $"Đã xóa vĩnh viễn tài khoản {displayName} ({displayEmail}) và toàn bộ dữ liệu cá nhân liên quan!");
    }

    public async Task<(bool Success, string Message)> ManageSampleDataAsync(
        string adminUserId,
        string actionMode)
    {
        string cleanMode = string.IsNullOrWhiteSpace(actionMode) ? "SeedSampleData" : actionMode.Trim();

        const string clearTransactionsSql = @"
            IF OBJECT_ID('dbo.SupportChatMessages', 'U') IS NOT NULL DELETE FROM [dbo].[SupportChatMessages];
            IF OBJECT_ID('dbo.QrLoginSessions', 'U') IS NOT NULL DELETE FROM [dbo].[QrLoginSessions];
            IF OBJECT_ID('dbo.BookHandoverQrs', 'U') IS NOT NULL DELETE FROM [dbo].[BookHandoverQrs];
            IF OBJECT_ID('dbo.RoleUpgradeRequests', 'U') IS NOT NULL DELETE FROM [dbo].[RoleUpgradeRequests];
            IF OBJECT_ID('dbo.ReadingSessions', 'U') IS NOT NULL DELETE FROM [dbo].[ReadingSessions];
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
            await ExecuteRawBatchAsync(clearTransactionsSql);
            return (true, "Đã làm sạch toàn bộ dữ liệu giao dịch và đặt lại 100% bản sao sách về trạng thái Sẵn sàng!");
        }

        if (cleanMode == "FactoryClean")
        {
            await ExecuteRawBatchAsync(clearTransactionsSql);

            const string clearCatalogSql = @"
                DELETE FROM [dbo].[ShelfItems];
                DELETE FROM [dbo].[Shelves];
                DELETE FROM [dbo].[BookCopies];
                DELETE FROM [dbo].[Books];
                DELETE FROM [dbo].[Memberships];
                IF OBJECT_ID('dbo.UserWallets', 'U') IS NOT NULL DELETE FROM [dbo].[UserWallets] WHERE [UserId] <> N'u-admin-01';
                DELETE FROM [dbo].[AspNetUserLogins];
                DELETE FROM [dbo].[AspNetUserClaims];
                DELETE FROM [dbo].[AspNetUserTokens];
                IF EXISTS (SELECT 1 FROM [dbo].[AspNetUsers] WHERE [Id] = N'u-admin-01')
                BEGIN
                    UPDATE [dbo].[Libraries] SET [ManagerUserId] = N'u-admin-01';
                END
                DELETE FROM [dbo].[AspNetUserRoles] WHERE [UserId] <> N'u-admin-01';
                DELETE FROM [dbo].[AspNetUsers] WHERE [Id] <> N'u-admin-01';
            ";

            await ExecuteRawBatchAsync(clearCatalogSql);
            await _bootstrap.EnsureInitializedAsync();
            return (true, "Đã đưa hệ thống về trạng thái trống ban đầu (chỉ giữ lại Quản trị viên gốc, danh mục thể loại và 3 điểm trạm).");
        }

        // Ưu tiên tệp dữ liệu mẫu đóng gói kèm hệ thống
        var candidatePaths = new[]
        {
            "/sql/02_BookShare_SeedData.sql",
            Path.Combine(Directory.GetCurrentDirectory(), "database", "02_BookShare_SeedData.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "database", "02_BookShare_SeedData.sql"),
            Path.Combine(AppContext.BaseDirectory, "database", "02_BookShare_SeedData.sql"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "database", "02_BookShare_SeedData.sql")
        };

        string? resolvedSqlPath = candidatePaths.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(resolvedSqlPath))
        {
            return (false, "Không tìm thấy tệp kịch bản dữ liệu mẫu trên máy chủ.");
        }

        await ExecuteRawBatchAsync(clearTransactionsSql);
        const string prepareForReseedSql = @"
            DELETE FROM [dbo].[ShelfItems];
            DELETE FROM [dbo].[Shelves];
            DELETE FROM [dbo].[BookCopies];
            DELETE FROM [dbo].[Books];
            DELETE FROM [dbo].[Libraries];
            DELETE FROM [dbo].[Memberships];
            IF OBJECT_ID('dbo.UserWallets', 'U') IS NOT NULL DELETE FROM [dbo].[UserWallets] WHERE [UserId] <> N'u-admin-01';
            DELETE FROM [dbo].[AspNetUserLogins];
            DELETE FROM [dbo].[AspNetUserClaims];
            DELETE FROM [dbo].[AspNetUserTokens];
            DELETE FROM [dbo].[AspNetUserRoles] WHERE [UserId] <> N'u-admin-01';
            DELETE FROM [dbo].[AspNetUsers] WHERE [Id] <> N'u-admin-01';
        ";
        await ExecuteRawBatchAsync(prepareForReseedSql);

        string sqlContent = await File.ReadAllTextAsync(resolvedSqlPath);
        var batches = Regex.Split(sqlContent, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        foreach (var rawBatch in batches)
        {
            var batch = Regex.Replace(
                rawBatch,
                @"^\s*(USE\s+\[?BookShareDb\]?|PRINT\s+.*)\s*;?\s*$",
                string.Empty,
                RegexOptions.Multiline | RegexOptions.IgnoreCase).Trim();

            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await ExecuteRawBatchAsync(batch);
        }

        await _bootstrap.EnsureInitializedAsync();

        int booksCount = await _db.Books.CountAsync();
        int copiesCount = await _db.BookCopies.CountAsync();
        int usersCount = await _db.AspNetUsers.CountAsync();

        return (true, $"Đã khôi phục thành công bộ dữ liệu cộng đồng tiêu chuẩn ({booksCount} đầu sách, {copiesCount} bản sao vật lý, {usersCount} tài khoản thành viên)!");
    }

    private async Task ExecuteRawBatchAsync(string sqlBatch)
    {
        var conn = _db.Database.GetDbConnection();
        bool shouldClose = conn.State != ConnectionState.Open;
        if (shouldClose)
        {
            await _db.Database.OpenConnectionAsync();
        }
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sqlBatch;
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose)
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
    }
}
