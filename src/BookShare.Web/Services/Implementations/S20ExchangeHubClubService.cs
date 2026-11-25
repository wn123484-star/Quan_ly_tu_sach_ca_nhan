using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [S20] Dịch vụ quản lý đa Câu lạc bộ độc lập: thành lập CLB, tuyển/duyệt/phân quyền thành viên (Trưởng nhóm, Phó nhóm, Thành viên), bảng tin CLB, tủ sách CLB và điều phối đơn ký gửi/giao nhận tại trạm.
/// </summary>
/// <remarks>Phân quyền chặt theo từng CLB: chỉ Trưởng/Phó CLB của đúng CLB đó hoặc Admin mới được quản trị.</remarks>
public class S20ExchangeHubClubService : IS20ExchangeHubClubService
{
    private readonly BookShareDbContext _db;
    private readonly ILoanFlowCoordinatorService _loanCoordinator;
    private readonly IAntiSpamRateLimiterService _rateLimiter;

    public S20ExchangeHubClubService(
        BookShareDbContext db,
        ILoanFlowCoordinatorService loanCoordinator,
        IAntiSpamRateLimiterService rateLimiter)
    {
        _db = db;
        _loanCoordinator = loanCoordinator;
        _rateLimiter = rateLimiter;
    }

    public async Task<S20ExchangeHubClubPageViewModel> GetExchangeHubPageAsync(
        string currentUserId,
        string currentUserRole,
        int? selectedHubId = null,
        string? clubKeyword = null,
        string hubTypeFilter = "all",
        string memberStatusFilter = "all",
        string orderStatusFilter = "all",
        int memberPage = 1,
        int orderPage = 1)
    {
        var allHubsRaw = await _db.Libraries
            .AsNoTracking()
            .Include(l => l.ManagerUser)
            .Include(l => l.BookCopies)
            .Include(l => l.LoanRequests)
            .Include(l => l.ClubMembers)
            .OrderByDescending(l => l.HubType == "ClubStation" || l.HubType == "DropoffHub")
            .ThenBy(l => l.Id)
            .ToListAsync();

        var allHubDtos = allHubsRaw.Select(l => new S20HubStationDto
        {
            HubId = l.Id,
            Name = l.Name,
            Address = l.Address,
            ManagerUserId = l.ManagerUserId,
            ManagerName = l.ManagerUser?.FullName ?? "Ban Điều Hành",
            ManagerEmail = string.Empty,
            Phone = l.Phone ?? "Liên hệ qua bảng tin CLB",
            HubType = l.HubType,
            OpenHours = l.OpenHours ?? "08:00 - 18:00",
            Description = l.Description,
            ClubRules = l.ClubRules,
            IsPublicClub = l.IsPublicClub,
            IsActive = l.IsActive,
            CopiesStoredCount = l.BookCopies.Count,
            ActiveLoanRequestsCount = l.LoanRequests.Count(r => r.Status == "Pending" || r.Status == "Approved"),
            ApprovedMembersCount = l.ClubMembers.Count(m => m.Status == "Approved"),
            PendingMembersCount = l.ClubMembers.Count(m => m.Status == "Pending")
        }).ToList();

        IEnumerable<S20HubStationDto> filteredHubs = allHubDtos;
        if (hubTypeFilter is "ClubStation" or "DropoffHub" or "Library")
        {
            filteredHubs = filteredHubs.Where(h => h.HubType == hubTypeFilter);
        }
        if (!string.IsNullOrWhiteSpace(clubKeyword))
        {
            var kw = clubKeyword.Trim();
            filteredHubs = filteredHubs.Where(h =>
                h.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                h.Address.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                h.ManagerName.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        var visibleHubs = filteredHubs.ToList();
        var selectedHub = (selectedHubId.HasValue
                ? allHubDtos.FirstOrDefault(h => h.HubId == selectedHubId.Value)
                : null)
            ?? visibleHubs.FirstOrDefault(h => h.HubType == "ClubStation" || h.HubType == "DropoffHub")
            ?? visibleHubs.FirstOrDefault()
            ?? allHubDtos.FirstOrDefault();

        int resolvedHubId = selectedHub?.HubId ?? 0;

        // Thành viên của CLB đang chọn (có lọc & phân trang)
        var rawMembers = resolvedHubId > 0
            ? await _db.ClubMembers
                .AsNoTracking()
                .Include(m => m.User).ThenInclude(u => u.Roles)
                .Include(m => m.AddedByUser)
                .Where(m => m.HubId == resolvedHubId && m.Status != "Rejected")
                .OrderByDescending(m => m.MemberRole == "Leader")
                .ThenByDescending(m => m.MemberRole == "ViceLeader")
                .ThenByDescending(m => m.Status == "Pending")
                .ThenBy(m => m.JoinedAt)
                .ToListAsync()
            : new List<ClubMember>();

        var myMembership = !string.IsNullOrWhiteSpace(currentUserId)
            ? rawMembers.FirstOrDefault(m => m.UserId == currentUserId)
            : null;

        bool isAdmin = string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        bool isSelectedLeader = selectedHub != null && !string.IsNullOrWhiteSpace(currentUserId) &&
            (string.Equals(selectedHub.ManagerUserId, currentUserId, StringComparison.OrdinalIgnoreCase) ||
             (myMembership != null && myMembership.Status == "Approved" && myMembership.MemberRole == "Leader"));
        bool isSelectedVice = myMembership != null && myMembership.Status == "Approved" && myMembership.MemberRole == "ViceLeader";
        bool canManageSelected = isAdmin || isSelectedLeader || isSelectedVice;

        IEnumerable<ClubMember> filteredMembers = rawMembers;
        if (memberStatusFilter is "Approved" or "Pending")
        {
            filteredMembers = filteredMembers.Where(m => m.Status == memberStatusFilter);
        }
        var filteredMemberList = filteredMembers.ToList();
        int memberPageSize = 10;
        int memberTotalPages = Math.Max(1, (int)Math.Ceiling(filteredMemberList.Count / (double)memberPageSize));
        int safeMemberPage = Math.Clamp(memberPage, 1, memberTotalPages);

        var memberDtos = filteredMemberList
            .Skip((safeMemberPage - 1) * memberPageSize)
            .Take(memberPageSize)
            .Select(m => new S20ClubMemberDto
            {
                MemberId = m.Id,
                HubId = m.HubId,
                UserId = m.UserId,
                FullName = m.User.FullName,
                // [ANTI-PII-LEAK] Tuyệt đối không phơi bày địa chỉ Email cá nhân của thành viên CLB trên giao diện nhóm
                Email = string.Empty,
                SystemRole = m.User.Roles.FirstOrDefault()?.Name ?? "User",
                TrustScore = m.User.TrustScore,
                MemberRole = m.MemberRole,
                Status = m.Status,
                JoinNote = m.JoinNote,
                AddedByName = m.AddedByUser?.FullName,
                JoinedAt = m.JoinedAt
            }).ToList();

        // Thành viên chính thức của CLB đang chọn (dùng cho chức năng chuyển giao Trưởng nhóm — 0 lộ danh sách người lạ/email)
        var eligibleLeaderMembers = rawMembers
            .Where(m => m.Status == "Approved")
            .Select(m => new S20ClubMemberOptionDto
            {
                UserId = m.UserId,
                FullName = m.User.FullName,
                MemberRole = m.MemberRole,
                TrustScore = m.User.TrustScore
            })
            .ToList();

        var approvedMemberUserIds = new HashSet<string>(
            rawMembers.Where(m => m.Status == "Approved").Select(m => m.UserId),
            StringComparer.OrdinalIgnoreCase);

        // Bảng tin CLB đang chọn
        var memberRoleByUser = rawMembers.ToDictionary(m => m.UserId, m => m.MemberRole, StringComparer.OrdinalIgnoreCase);
        var clubPosts = resolvedHubId > 0
            ? await _db.ClubPosts
                .AsNoTracking()
                .Include(p => p.AuthorUser)
                .Where(p => p.HubId == resolvedHubId)
                .OrderByDescending(p => p.IsPinned)
                .ThenByDescending(p => p.CreatedAt)
                .Take(15)
                .Select(p => new S20ClubPostDto
                {
                    PostId = p.Id,
                    HubId = p.HubId,
                    AuthorUserId = p.AuthorUserId,
                    AuthorName = p.AuthorUser.FullName,
                    Title = p.Title,
                    Content = p.Content,
                    IsPinned = p.IsPinned,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync()
            : new List<S20ClubPostDto>();

        foreach (var post in clubPosts)
        {
            post.AuthorClubRole = memberRoleByUser.TryGetValue(post.AuthorUserId, out var r) ? r : "Admin";
        }

        // [ANTI-PII-LEAK] Không trả danh sách toàn bộ người dùng kèm Email ra DOM; thay bằng mạng lưới Bạn bè đã kết bạn
        string currentUserFullName = string.Empty;
        var friends = new List<S20FriendItemDto>();
        var incomingFriendRequests = new List<S20FriendItemDto>();
        var outgoingFriendRequests = new List<S20FriendItemDto>();

        if (!string.IsNullOrWhiteSpace(currentUserId))
        {
            currentUserFullName = await _db.AspNetUsers
                .AsNoTracking()
                .Where(u => u.Id == currentUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync() ?? string.Empty;

            var myFriendships = await _db.Friendships
                .AsNoTracking()
                .Include(f => f.Requester).ThenInclude(u => u.Roles)
                .Include(f => f.Addressee).ThenInclude(u => u.Roles)
                .Where(f => (f.RequesterId == currentUserId || f.AddresseeId == currentUserId) && f.Status != "Rejected")
                .OrderByDescending(f => f.RespondedAt ?? f.CreatedAt)
                .ToListAsync();

            foreach (var f in myFriendships)
            {
                bool iAmRequester = string.Equals(f.RequesterId, currentUserId, StringComparison.OrdinalIgnoreCase);
                var peer = iAmRequester ? f.Addressee : f.Requester;
                var dto = new S20FriendItemDto
                {
                    FriendshipId = f.Id,
                    UserId = peer.Id,
                    FullName = peer.FullName,
                    SystemRole = peer.Roles.FirstOrDefault()?.Name ?? "User",
                    TrustScore = peer.TrustScore,
                    AvatarUrl = string.IsNullOrWhiteSpace(peer.AvatarUrl) ? "/images/avatar-default.svg" : peer.AvatarUrl,
                    Status = f.Status,
                    Message = f.Message,
                    CreatedAt = f.CreatedAt,
                    RespondedAt = f.RespondedAt,
                    IsInSelectedClub = approvedMemberUserIds.Contains(peer.Id)
                };

                if (f.Status == "Accepted")
                {
                    friends.Add(dto);
                }
                else if (f.Status == "Pending")
                {
                    if (iAmRequester) outgoingFriendRequests.Add(dto);
                    else incomingFriendRequests.Add(dto);
                }
            }
        }

        var candidateUsers = new List<S20UserOptionDto>();

        // Bản sao ký gửi tại các trạm / CLB (ưu tiên CLB đang chọn + toàn bộ P2P)
        var p2pCopies = await _db.BookCopies
            .AsNoTracking()
            .Include(bc => bc.Book)
            .Include(bc => bc.OwnerUser)
            .Include(bc => bc.Library)
            .Where(bc => bc.OwnerType == "User" || (resolvedHubId > 0 && bc.LibraryId == resolvedHubId))
            .OrderByDescending(bc => bc.LibraryId == resolvedHubId)
            .ThenByDescending(bc => bc.Id)
            .Select(bc => new S20HubBookCopyDto
            {
                BookCopyId = bc.Id,
                Barcode = bc.Barcode,
                BookTitle = bc.Book.Title,
                OwnerName = bc.OwnerUser != null ? bc.OwnerUser.FullName : "CLB BookShare Hub",
                OwnerType = bc.OwnerType,
                HubId = bc.LibraryId,
                HubName = bc.Library != null ? bc.Library.Name : "Chưa gán trạm Hub",
                ShelfLocation = bc.ShelfLocation ?? "Kệ Ký Gửi CLB",
                Condition = bc.Condition,
                Status = bc.Status,
                IsLendable = bc.IsLendable
            })
            .ToListAsync();

        // Đơn giao nhận trung gian tại CLB (có lọc trạng thái & phân trang)
        var hubOrdersQuery = _db.LoanRequests
            .AsNoTracking()
            .Include(lr => lr.BookCopy).ThenInclude(bc => bc.Book)
            .Include(lr => lr.BookCopy).ThenInclude(bc => bc.OwnerUser)
            .Include(lr => lr.Borrower)
            .Include(lr => lr.PickupHub)
            .Include(lr => lr.Loan)
            .Where(lr => lr.LoanType == "P2P" || (resolvedHubId > 0 && lr.PickupHubId == resolvedHubId));

        if (!string.IsNullOrWhiteSpace(orderStatusFilter) && orderStatusFilter != "all")
        {
            hubOrdersQuery = hubOrdersQuery.Where(lr => lr.Status == orderStatusFilter || lr.BookCopy.Status == orderStatusFilter);
        }

        var hubOrdersAll = await hubOrdersQuery
            .OrderByDescending(lr => lr.PickupHubId == resolvedHubId)
            .ThenByDescending(lr => lr.RequestedAt)
            .Select(lr => new S20HubLoanOrderDto
            {
                LoanRequestId = lr.Id,
                LoanId = lr.Loan != null ? lr.Loan.Id : null,
                PickupHubId = lr.PickupHubId,
                Barcode = lr.BookCopy.Barcode,
                BookTitle = lr.BookCopy.Book.Title,
                OwnerName = lr.BookCopy.OwnerUser != null ? lr.BookCopy.OwnerUser.FullName : "CLB Sách ĐH CMC",
                BorrowerName = lr.Borrower.FullName,
                BorrowerTrustScore = lr.Borrower.TrustScore,
                HubName = lr.PickupHub != null ? lr.PickupHub.Name : "Trạm CLB BookShare Cầu Giấy",
                RequestStatus = lr.Status,
                CopyStatus = lr.BookCopy.Status,
                RequestedAt = lr.RequestedAt
            })
            .ToListAsync();

        int orderPageSize = 10;
        int orderTotalPages = Math.Max(1, (int)Math.Ceiling(hubOrdersAll.Count / (double)orderPageSize));
        int safeOrderPage = Math.Clamp(orderPage, 1, orderTotalPages);
        var pagedOrders = hubOrdersAll.Skip((safeOrderPage - 1) * orderPageSize).Take(orderPageSize).ToList();

        return new S20ExchangeHubClubPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserFullName = currentUserFullName,
            CurrentUserRole = currentUserRole,
            TotalHubsCount = allHubDtos.Count,
            TotalClubsCount = allHubDtos.Count(h => h.HubType is "ClubStation" or "DropoffHub"),
            TotalCopiesAtHubs = p2pCopies.Count,
            PendingP2PHubOrdersCount = hubOrdersAll.Count(o => o.RequestStatus == "Pending" || o.RequestStatus == "Approved"),
            SelectedHubId = resolvedHubId,
            SelectedHub = selectedHub,
            ClubKeyword = clubKeyword,
            HubTypeFilter = hubTypeFilter,
            MemberStatusFilter = memberStatusFilter,
            OrderStatusFilter = orderStatusFilter,
            MemberPage = safeMemberPage,
            MemberPageSize = memberPageSize,
            TotalFilteredMembers = filteredMemberList.Count,
            OrderPage = safeOrderPage,
            OrderPageSize = orderPageSize,
            TotalFilteredOrders = hubOrdersAll.Count,
            CanManageSelectedClub = canManageSelected,
            IsSelectedClubLeader = isAdmin || isSelectedLeader,
            IsCurrentUserClubMember = myMembership != null && myMembership.Status == "Approved",
            CurrentUserClubRole = myMembership?.MemberRole,
            CurrentUserClubStatus = myMembership?.Status,
            Hubs = visibleHubs.Any() ? visibleHubs : allHubDtos,
            SelectedClubMembers = memberDtos,
            EligibleLeaderMembers = eligibleLeaderMembers,
            SelectedClubPosts = clubPosts,
            CandidateUsers = candidateUsers,
            Friends = friends,
            IncomingFriendRequests = incomingFriendRequests,
            OutgoingFriendRequests = outgoingFriendRequests,
            HubCopies = p2pCopies,
            HubP2POrders = pagedOrders
        };
    }

    private async Task<bool> IsHubLeaderOrAdminAsync(Library hub, string userId, string userRole)
    {
        if (string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(hub.ManagerUserId, userId, StringComparison.OrdinalIgnoreCase)) return true;
        return await _db.ClubMembers.AnyAsync(m =>
            m.HubId == hub.Id && m.UserId == userId && m.Status == "Approved" && m.MemberRole == "Leader");
    }

    private async Task<bool> CanManageClubAsync(Library hub, string userId, string userRole)
    {
        if (await IsHubLeaderOrAdminAsync(hub, userId, userRole)) return true;
        return await _db.ClubMembers.AnyAsync(m =>
            m.HubId == hub.Id && m.UserId == userId && m.Status == "Approved" &&
            (m.MemberRole == "Leader" || m.MemberRole == "ViceLeader"));
    }

    public async Task<(bool Success, string Message, int HubId)> CreateClubAsync(
        string creatorUserId,
        string creatorRole,
        S20CreateClubDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Address))
        {
            return (false, "Tên Câu lạc bộ / Điểm trạm và Địa chỉ không được để trống.", 0);
        }

        string cleanName = request.Name.Trim();
        string cleanAddress = request.Address.Trim();
        if (cleanName.Length > 180 || cleanAddress.Length > 300)
        {
            return (false, "Tên Câu lạc bộ tối đa 180 ký tự và Địa chỉ tối đa 300 ký tự.", 0);
        }

        // Mặc định người thành lập CLB là Trưởng nhóm; chỉ Quản trị viên mới được chỉ định Trưởng nhóm khác qua API nếu truyền rõ
        bool isAdmin = string.Equals(creatorRole, "Admin", StringComparison.OrdinalIgnoreCase);
        string leaderId = (isAdmin && !string.IsNullOrWhiteSpace(request.LeaderUserId))
            ? request.LeaderUserId.Trim()
            : creatorUserId;
        var leaderUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == leaderId);
        if (leaderUser == null)
        {
            return (false, "Không tìm thấy thành viên được chỉ định làm Trưởng nhóm CLB.", 0);
        }

        var allowedTypes = new[] { "ClubStation", "DropoffHub", "Library" };
        string hubType = allowedTypes.Contains(request.HubType) ? request.HubType : "ClubStation";

        var hub = new Library
        {
            Name = cleanName,
            Address = cleanAddress,
            ManagerUserId = leaderUser.Id,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? "Liên hệ qua bảng tin CLB" : request.Phone.Trim(),
            HubType = hubType,
            OpenHours = string.IsNullOrWhiteSpace(request.OpenHours) ? "09:00 - 20:00 (Cả tuần)" : request.OpenHours.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? "Câu lạc bộ đọc sách cộng đồng BookShare — giao lưu, mượn trả sách giấy 0đ."
                : request.Description.Trim(),
            ClubRules = string.IsNullOrWhiteSpace(request.ClubRules)
                ? "1. Mượn trả sách giấy 0đ minh bạch. 2. Bảo quản sách cẩn thận và hoàn trả đúng hẹn. 3. Tôn trọng điều phối của Trưởng nhóm CLB."
                : request.ClubRules.Trim(),
            IsPublicClub = request.IsPublicClub,
            IsActive = true
        };

        _db.Libraries.Add(hub);
        await _db.SaveChangesAsync();

        bool isSelfFounded = string.Equals(leaderUser.Id, creatorUserId, StringComparison.OrdinalIgnoreCase);
        _db.ClubMembers.Add(new ClubMember
        {
            HubId = hub.Id,
            UserId = leaderUser.Id,
            MemberRole = "Leader",
            Status = "Approved",
            JoinNote = isSelfFounded ? "Trưởng nhóm sáng lập Câu lạc bộ" : "Trưởng nhóm sáng lập được Quản trị viên chỉ định",
            AddedByUserId = creatorUserId,
            JoinedAt = DateTime.UtcNow
        });

        _db.ClubPosts.Add(new ClubPost
        {
            HubId = hub.Id,
            AuthorUserId = creatorUserId,
            Title = $"Chào mừng đến với {hub.Name}!",
            Content = $"Câu lạc bộ chính thức đi vào hoạt động do Trưởng nhóm {leaderUser.FullName} điều hành. Mời các thành viên kết bạn, tham gia nhóm và chia sẻ tủ sách mỗi tuần!",
            IsPinned = true,
            CreatedAt = DateTime.UtcNow
        });

        if (!isSelfFounded)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = leaderUser.Id,
                Title = $"Bạn được bổ nhiệm làm Trưởng nhóm \"{hub.Name}\"",
                Content = "Quản trị viên vừa thành lập CLB và bổ nhiệm bạn làm Trưởng nhóm điều hành. Bạn có thể kết bạn, thêm thành viên, duyệt đơn gia nhập và đăng thông báo tại Không gian CLB.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã thành lập CLB \"{hub.Name}\" thành công! Trưởng nhóm mặc định: {leaderUser.FullName}.", hub.Id);
    }

    public async Task<(bool Success, string Message)> AssignClubLeaderAsync(
        string currentUserId,
        string currentUserRole,
        S20AssignClubLeaderDto request)
    {
        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        if (hub == null) return (false, "Không tìm thấy Câu lạc bộ cần bổ nhiệm Trưởng nhóm.");

        if (!await IsHubLeaderOrAdminAsync(hub, currentUserId, currentUserRole))
        {
            throw new UnauthorizedAccessException("Chỉ Quản trị viên hoặc Trưởng nhóm hiện tại của CLB này mới được quyền chuyển giao Trưởng nhóm.");
        }

        var newLeader = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == request.LeaderUserId);
        if (newLeader == null) return (false, "Không tìm thấy tài khoản thành viên được bổ nhiệm.");

        var existingMembers = await _db.ClubMembers.Where(m => m.HubId == hub.Id).ToListAsync();
        bool isAdmin = string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        var targetRow = existingMembers.FirstOrDefault(m => m.UserId == newLeader.Id);
        if (!isAdmin && (targetRow == null || targetRow.Status != "Approved"))
        {
            return (false, "Chỉ có thể chuyển giao quyền Trưởng nhóm cho thành viên chính thức hiện có trong CLB này.");
        }

        string oldLeaderId = hub.ManagerUserId;
        hub.ManagerUserId = newLeader.Id;

        foreach (var old in existingMembers.Where(m => m.MemberRole == "Leader" && m.UserId != newLeader.Id))
        {
            old.MemberRole = "ViceLeader";
        }

        if (targetRow == null)
        {
            _db.ClubMembers.Add(new ClubMember
            {
                HubId = hub.Id,
                UserId = newLeader.Id,
                MemberRole = "Leader",
                Status = "Approved",
                JoinNote = "Được bổ nhiệm làm Trưởng nhóm CLB",
                AddedByUserId = currentUserId,
                JoinedAt = DateTime.UtcNow
            });
        }
        else
        {
            targetRow.MemberRole = "Leader";
            targetRow.Status = "Approved";
        }

        if (newLeader.Id != oldLeaderId)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = newLeader.Id,
                Title = $"Bạn đã trở thành Trưởng nhóm CLB \"{hub.Name}\"",
                Content = "Bạn đã được giao quyền Trưởng nhóm quản lý thành viên, bảng tin và điều phối sách tại Câu lạc bộ này.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã bổ nhiệm {newLeader.FullName} làm Trưởng nhóm CLB \"{hub.Name}\" thành công!");
    }

    public async Task<(bool Success, string Message)> AddClubMemberAsync(
        string currentUserId,
        string currentUserRole,
        S20AddClubMemberDto request)
    {
        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        if (hub == null) return (false, "Không tìm thấy Câu lạc bộ cần thêm thành viên.");

        if (!await CanManageClubAsync(hub, currentUserId, currentUserRole))
        {
            throw new UnauthorizedAccessException("Chỉ Quản trị viên, Trưởng nhóm hoặc Phó nhóm của CLB này mới được thêm thành viên vào nhóm.");
        }

        AspNetUser? targetUser = null;
        if (!string.IsNullOrWhiteSpace(request.TargetUserId))
        {
            targetUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == request.TargetUserId.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(request.TargetEmail))
        {
            string normEmail = request.TargetEmail.Trim().ToUpperInvariant();
            targetUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.NormalizedEmail == normEmail || u.NormalizedUserName == normEmail);
        }

        if (targetUser == null)
        {
            return (false, "Không tìm thấy thành viên phù hợp để thêm vào Câu lạc bộ.");
        }

        if (targetUser.LockoutEnd.HasValue && targetUser.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản này đang bị tạm khóa nên không thể thêm vào Câu lạc bộ.");
        }

        var existing = await _db.ClubMembers.FirstOrDefaultAsync(m => m.HubId == hub.Id && m.UserId == targetUser.Id);
        bool isAdmin = string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        bool hasPendingJoin = existing != null && existing.Status == "Pending";
        bool isFriend = await _db.Friendships.AnyAsync(f =>
            f.Status == "Accepted" &&
            ((f.RequesterId == currentUserId && f.AddresseeId == targetUser.Id) ||
             (f.RequesterId == targetUser.Id && f.AddresseeId == currentUserId)));

        if (!isAdmin && !string.Equals(targetUser.Id, currentUserId, StringComparison.OrdinalIgnoreCase) && !isFriend && !hasPendingJoin)
        {
            return (false, $"Bạn cần gửi lời mời kết bạn và được {targetUser.FullName} chấp nhận trước khi thêm trực tiếp vào Câu lạc bộ!");
        }

        bool isLeaderOrAdmin = await IsHubLeaderOrAdminAsync(hub, currentUserId, currentUserRole);
        string assignedRole = (request.MemberRole == "ViceLeader" && isLeaderOrAdmin) ? "ViceLeader" : "Member";
        if (existing != null)
        {
            if (existing.Status == "Approved")
            {
                return (false, $"{targetUser.FullName} đã là thành viên trong CLB \"{hub.Name}\".");
            }
            existing.Status = "Approved";
            existing.MemberRole = existing.MemberRole == "Leader" ? "Leader" : assignedRole;
            existing.AddedByUserId = currentUserId;
            existing.JoinedAt = DateTime.UtcNow;
        }
        else
        {
            _db.ClubMembers.Add(new ClubMember
            {
                HubId = hub.Id,
                UserId = targetUser.Id,
                MemberRole = assignedRole,
                Status = "Approved",
                JoinNote = isFriend ? "Được bạn bè trong Ban điều hành mời vào nhóm CLB" : "Được Ban điều hành thêm trực tiếp vào nhóm CLB",
                AddedByUserId = currentUserId,
                JoinedAt = DateTime.UtcNow
            });
        }

        _db.Notifications.Add(new Notification
        {
            UserId = targetUser.Id,
            Title = $"Bạn đã được thêm vào CLB \"{hub.Name}\"",
            Content = $"Bạn vừa được mời tham gia nhóm CLB \"{hub.Name}\" với vai trò {(assignedRole == "ViceLeader" ? "Phó nhóm" : "Thành viên")}.",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return (true, $"Đã thêm {targetUser.FullName} vào nhóm CLB \"{hub.Name}\" thành công!");
    }

    public async Task<List<S20FriendCandidateDto>> SearchFriendCandidatesAsync(
        string currentUserId,
        string? keyword)
    {
        if (string.IsNullOrWhiteSpace(currentUserId) || string.IsNullOrWhiteSpace(keyword))
        {
            return new List<S20FriendCandidateDto>();
        }

        string kw = keyword.Trim();
        if (kw.Length < 2 || kw.Length > 120)
        {
            return new List<S20FriendCandidateDto>();
        }

        string normExactEmail = kw.ToUpperInvariant();
        bool looksLikeEmail = kw.Contains('@');
        var now = DateTimeOffset.UtcNow;

        var usersQuery = _db.AspNetUsers
            .AsNoTracking()
            .Include(u => u.Roles)
            .Where(u => u.Id != currentUserId && (!u.LockoutEnd.HasValue || u.LockoutEnd.Value <= now));

        // Bảo vệ PII: chỉ khớp Email chính xác 100% (không hỗ trợ tìm chuỗi con Email để tránh dò quét danh bạ)
        usersQuery = looksLikeEmail
            ? usersQuery.Where(u => u.NormalizedEmail == normExactEmail)
            : usersQuery.Where(u => u.FullName.Contains(kw));

        var matches = await usersQuery
            .OrderByDescending(u => u.TrustScore)
            .ThenBy(u => u.FullName)
            .Take(12)
            .ToListAsync();

        if (!matches.Any())
        {
            return new List<S20FriendCandidateDto>();
        }

        var matchIds = matches.Select(u => u.Id).ToList();
        var existingPairs = await _db.Friendships
            .AsNoTracking()
            .Where(f =>
                (f.RequesterId == currentUserId && matchIds.Contains(f.AddresseeId)) ||
                (f.AddresseeId == currentUserId && matchIds.Contains(f.RequesterId)))
            .ToListAsync();

        return matches.Select(u =>
        {
            var rel = existingPairs.FirstOrDefault(f =>
                (f.RequesterId == currentUserId && f.AddresseeId == u.Id) ||
                (f.RequesterId == u.Id && f.AddresseeId == currentUserId));

            string relStatus = "None";
            if (rel != null)
            {
                if (rel.Status == "Accepted") relStatus = "Accepted";
                else if (rel.Status == "Rejected") relStatus = "Rejected";
                else if (rel.Status == "Pending")
                    relStatus = string.Equals(rel.RequesterId, currentUserId, StringComparison.OrdinalIgnoreCase)
                        ? "PendingSent"
                        : "PendingReceived";
            }

            return new S20FriendCandidateDto
            {
                UserId = u.Id,
                FullName = u.FullName,
                SystemRole = u.Roles.FirstOrDefault()?.Name ?? "User",
                TrustScore = u.TrustScore,
                AvatarUrl = string.IsNullOrWhiteSpace(u.AvatarUrl) ? "/images/avatar-default.svg" : u.AvatarUrl,
                RelationStatus = relStatus,
                FriendshipId = rel?.Id
            };
        }).ToList();
    }

    public async Task<(bool Success, string Message, int FriendshipId)> SendFriendRequestAsync(
        string currentUserId,
        S20SendFriendRequestDto request)
    {
        var (allowed, retrySec, rlMsg) = _rateLimiter.TryConsume(
            "s20-friend-request",
            currentUserId,
            maxRequests: 15,
            window: TimeSpan.FromMinutes(10),
            customMessage: "Bạn đã gửi quá nhiều lời mời kết bạn trong 10 phút. Vui lòng đợi giây lát.");
        if (!allowed)
        {
            throw new RateLimitExceededException(rlMsg, retrySec);
        }

        var requester = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (requester == null)
        {
            return (false, "Không tìm thấy thông tin tài khoản hiện tại.", 0);
        }

        AspNetUser? target = null;
        if (!string.IsNullOrWhiteSpace(request.TargetUserId))
        {
            target = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == request.TargetUserId.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(request.TargetEmail))
        {
            string normEmail = request.TargetEmail.Trim().ToUpperInvariant();
            target = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.NormalizedEmail == normEmail || u.NormalizedUserName == normEmail);
        }

        if (target == null)
        {
            return (false, "Không tìm thấy thành viên bạn muốn gửi lời mời kết bạn.", 0);
        }
        if (string.Equals(target.Id, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Bạn không thể tự gửi lời mời kết bạn cho chính mình.", 0);
        }
        if (target.LockoutEnd.HasValue && target.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản này hiện đang bị tạm khóa nên không thể nhận lời mời kết bạn.", 0);
        }

        string? cleanMsg = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim();
        if (cleanMsg != null && cleanMsg.Length > 280)
        {
            return (false, "Lời nhắn kết bạn tối đa 280 ký tự.", 0);
        }

        var existing = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == currentUserId && f.AddresseeId == target.Id) ||
            (f.RequesterId == target.Id && f.AddresseeId == currentUserId));

        Friendship friendship;
        if (existing != null)
        {
            if (existing.Status == "Accepted")
            {
                return (false, $"Bạn và {target.FullName} đã là bạn bè trên BookShare.", existing.Id);
            }
            if (existing.Status == "Pending")
            {
                if (string.Equals(existing.RequesterId, currentUserId, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Bạn đã gửi lời mời kết bạn cho {target.FullName} và đang chờ phản hồi.", existing.Id);
                }
                return (false, $"{target.FullName} đã gửi lời mời kết bạn cho bạn. Hãy chọn Chấp nhận ở mục Lời mời kết bạn đến!", existing.Id);
            }

            // Nếu lời mời trước đây đã bị từ chối -> cho phép gửi lại lời mời mới
            existing.RequesterId = currentUserId;
            existing.AddresseeId = target.Id;
            existing.Status = "Pending";
            existing.Message = cleanMsg ?? "Kết bạn để giao lưu đọc sách và tham gia Câu lạc bộ cùng nhau!";
            existing.CreatedAt = DateTime.UtcNow;
            existing.RespondedAt = null;
            friendship = existing;
        }
        else
        {
            friendship = new Friendship
            {
                RequesterId = currentUserId,
                AddresseeId = target.Id,
                Status = "Pending",
                Message = cleanMsg ?? "Kết bạn để giao lưu đọc sách và tham gia Câu lạc bộ cùng nhau!",
                CreatedAt = DateTime.UtcNow
            };
            _db.Friendships.Add(friendship);
        }

        _db.Notifications.Add(new Notification
        {
            UserId = target.Id,
            Title = $"Lời mời kết bạn mới từ {requester.FullName}",
            Content = $"{requester.FullName} đã gửi lời mời kết bạn với bạn trên BookShare. Bạn có thể Chấp nhận hoặc Từ chối tại mục Kết bạn trong Không gian Câu lạc bộ.",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return (true, $"Đã gửi lời mời kết bạn tới {target.FullName}! Chờ phản hồi chấp nhận từ thành viên này.", friendship.Id);
    }

    public async Task<(bool Success, string Message)> RespondFriendRequestAsync(
        string currentUserId,
        S20RespondFriendRequestDto request)
    {
        var friendship = await _db.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .FirstOrDefaultAsync(f => f.Id == request.FriendshipId);

        if (friendship == null)
        {
            return (false, "Không tìm thấy lời mời kết bạn cần phản hồi.");
        }

        if (!string.Equals(friendship.AddresseeId, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Chỉ người nhận lời mời kết bạn mới có quyền chấp nhận hoặc từ chối.");
        }

        if (friendship.Status != "Pending")
        {
            return (false, "Lời mời kết bạn này đã được xử lý trước đó.");
        }

        friendship.Status = request.Accept ? "Accepted" : "Rejected";
        friendship.RespondedAt = DateTime.UtcNow;

        _db.Notifications.Add(new Notification
        {
            UserId = friendship.RequesterId,
            Title = request.Accept
                ? $"{friendship.Addressee.FullName} đã chấp nhận lời mời kết bạn"
                : $"Lời mời kết bạn tới {friendship.Addressee.FullName} đã bị từ chối",
            Content = request.Accept
                ? $"Bạn và {friendship.Addressee.FullName} đã trở thành bạn bè! Giờ bạn có thể thêm họ vào nhóm Câu lạc bộ đọc sách."
                : $"{friendship.Addressee.FullName} đã từ chối lời mời kết bạn của bạn.",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return request.Accept
            ? (true, $"Đã chấp nhận lời mời kết bạn của {friendship.Requester.FullName}!")
            : (true, $"Đã từ chối lời mời kết bạn của {friendship.Requester.FullName}.");
    }

    public async Task<(bool Success, string Message)> RemoveFriendshipAsync(
        string currentUserId,
        int friendshipId)
    {
        var friendship = await _db.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .FirstOrDefaultAsync(f => f.Id == friendshipId);

        if (friendship == null)
        {
            return (false, "Không tìm thấy kết nối bạn bè hoặc lời mời cần hủy.");
        }

        bool isParticipant = string.Equals(friendship.RequesterId, currentUserId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(friendship.AddresseeId, currentUserId, StringComparison.OrdinalIgnoreCase);
        if (!isParticipant)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền hủy mối quan hệ kết bạn của thành viên khác.");
        }

        bool wasPending = friendship.Status == "Pending";
        string peerName = string.Equals(friendship.RequesterId, currentUserId, StringComparison.OrdinalIgnoreCase)
            ? friendship.Addressee.FullName
            : friendship.Requester.FullName;

        _db.Friendships.Remove(friendship);
        await _db.SaveChangesAsync();

        return (true, wasPending
            ? $"Đã thu hồi lời mời kết bạn gửi tới {peerName}."
            : $"Đã hủy kết bạn với {peerName}.");
    }

    public async Task<(bool Success, string Message)> JoinClubAsync(
        string currentUserId,
        S20JoinClubDto request)
    {
        var (allowed, retrySec, rlMsg) = _rateLimiter.TryConsume(
            "s20-join-club",
            currentUserId,
            maxRequests: 8,
            window: TimeSpan.FromMinutes(5),
            customMessage: "Bạn gửi yêu cầu tham gia CLB quá nhanh (tối đa 8 lần / 5 phút). Vui lòng đợi giây lát để chống spam thông báo tới Trưởng nhóm.");
        if (!allowed)
        {
            throw new RateLimitExceededException(rlMsg, retrySec);
        }

        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId && l.IsActive);
        if (hub == null) return (false, "Không tìm thấy Câu lạc bộ bạn muốn tham gia.");

        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null) return (false, "Không tìm thấy thông tin tài khoản của bạn.");

        var existing = await _db.ClubMembers.FirstOrDefaultAsync(m => m.HubId == hub.Id && m.UserId == currentUserId);
        if (existing != null && existing.Status == "Approved")
        {
            return (false, $"Bạn đã là thành viên chính thức của CLB \"{hub.Name}\".");
        }
        if (existing != null && existing.Status == "Pending")
        {
            return (false, $"Đơn xin gia nhập CLB \"{hub.Name}\" của bạn đang chờ Trưởng nhóm duyệt.");
        }

        string status = hub.IsPublicClub ? "Approved" : "Pending";
        if (existing == null)
        {
            _db.ClubMembers.Add(new ClubMember
            {
                HubId = hub.Id,
                UserId = currentUserId,
                MemberRole = "Member",
                Status = status,
                JoinNote = string.IsNullOrWhiteSpace(request.JoinNote) ? "Đăng ký tham gia nhóm đọc sách CLB" : request.JoinNote.Trim(),
                JoinedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Status = status;
            existing.MemberRole = "Member";
            existing.JoinNote = string.IsNullOrWhiteSpace(request.JoinNote) ? "Đăng ký tham gia lại nhóm CLB" : request.JoinNote.Trim();
            existing.JoinedAt = DateTime.UtcNow;
        }

        if (status == "Pending" && !string.IsNullOrWhiteSpace(hub.ManagerUserId) && hub.ManagerUserId != currentUserId)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = hub.ManagerUserId,
                Title = $"Đơn xin gia nhập CLB \"{hub.Name}\" mới",
                Content = $"{user.FullName} vừa gửi yêu cầu tham gia nhóm CLB \"{hub.Name}\". Mời Trưởng nhóm vào xem xét phê duyệt.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return status == "Approved"
            ? (true, $"Chào mừng bạn gia nhập thành công nhóm CLB \"{hub.Name}\"!")
            : (true, $"Đã gửi yêu cầu gia nhập CLB \"{hub.Name}\". Trưởng nhóm sẽ xét duyệt trong thời gian sớm nhất!");
    }

    public async Task<(bool Success, string Message)> LeaveClubAsync(
        string currentUserId,
        int hubId)
    {
        var hub = await _db.Libraries.AsNoTracking().FirstOrDefaultAsync(l => l.Id == hubId);
        if (hub == null) return (false, "Không tìm thấy Câu lạc bộ.");

        var membership = await _db.ClubMembers.FirstOrDefaultAsync(m => m.HubId == hubId && m.UserId == currentUserId);
        if (membership == null)
        {
            return (false, "Bạn hiện không phải thành viên của CLB này.");
        }

        if (membership.MemberRole == "Leader" || string.Equals(hub.ManagerUserId, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Trưởng nhóm không thể tự rời CLB khi chưa chuyển giao quyền Trưởng nhóm cho thành viên khác.");
        }

        _db.ClubMembers.Remove(membership);
        await _db.SaveChangesAsync();
        return (true, $"Bạn đã rời khỏi nhóm CLB \"{hub.Name}\".");
    }

    public async Task<(bool Success, string Message)> ReviewClubMemberAsync(
        string currentUserId,
        string currentUserRole,
        S20ReviewClubMemberDto request)
    {
        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        if (hub == null) return (false, "Không tìm thấy Câu lạc bộ.");

        if (!await CanManageClubAsync(hub, currentUserId, currentUserRole))
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xét duyệt thành viên cho Câu lạc bộ này.");
        }

        var member = await _db.ClubMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == request.MemberId && m.HubId == hub.Id);
        if (member == null) return (false, "Không tìm thấy yêu cầu gia nhập CLB cần xử lý.");

        member.Status = request.Approve ? "Approved" : "Rejected";
        member.AddedByUserId = currentUserId;

        _db.Notifications.Add(new Notification
        {
            UserId = member.UserId,
            Title = request.Approve
                ? $"Đã được duyệt vào CLB \"{hub.Name}\""
                : $"Yêu cầu gia nhập CLB \"{hub.Name}\" chưa được duyệt",
            Content = request.Approve
                ? $"Trưởng nhóm đã chấp thuận cho bạn trở thành thành viên chính thức của CLB \"{hub.Name}\"."
                : $"Yêu cầu tham gia CLB \"{hub.Name}\" hiện chưa được chấp thuận.",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return (true, request.Approve
            ? $"Đã phê duyệt {member.User.FullName} gia nhập CLB \"{hub.Name}\"!"
            : $"Đã từ chối đơn gia nhập CLB của {member.User.FullName}.");
    }

    public async Task<(bool Success, string Message)> UpdateClubMemberRoleAsync(
        string currentUserId,
        string currentUserRole,
        S20UpdateClubMemberRoleDto request)
    {
        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        if (hub == null) return (false, "Không tìm thấy Câu lạc bộ.");

        if (!await IsHubLeaderOrAdminAsync(hub, currentUserId, currentUserRole))
        {
            throw new UnauthorizedAccessException("Chỉ Trưởng nhóm CLB này hoặc Quản trị viên mới được phân công vai trò Phó nhóm / Thành viên.");
        }

        var member = await _db.ClubMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == request.MemberId && m.HubId == hub.Id && m.Status == "Approved");
        if (member == null) return (false, "Không tìm thấy thành viên chính thức trong CLB.");
        if (member.MemberRole == "Leader") return (false, "Hãy dùng chức năng Chuyển giao Trưởng nhóm để thay đổi Trưởng nhóm CLB.");

        member.MemberRole = request.NewRole == "ViceLeader" ? "ViceLeader" : "Member";
        await _db.SaveChangesAsync();
        return (true, $"Đã cập nhật vai trò của {member.User.FullName} trong CLB sang {(member.MemberRole == "ViceLeader" ? "Phó nhóm" : "Thành viên")}.");
    }

    public async Task<(bool Success, string Message)> RemoveClubMemberAsync(
        string currentUserId,
        string currentUserRole,
        S20RemoveClubMemberDto request)
    {
        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        if (hub == null) return (false, "Không tìm thấy Câu lạc bộ.");

        if (!await IsHubLeaderOrAdminAsync(hub, currentUserId, currentUserRole))
        {
            throw new UnauthorizedAccessException("Chỉ Trưởng nhóm CLB này hoặc Quản trị viên mới được mời thành viên rời nhóm.");
        }

        var member = await _db.ClubMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == request.MemberId && m.HubId == hub.Id);
        if (member == null) return (false, "Không tìm thấy thành viên cần xóa khỏi nhóm.");
        if (member.MemberRole == "Leader" || string.Equals(hub.ManagerUserId, member.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Không thể xóa Trưởng nhóm hiện tại khỏi CLB. Hãy chuyển giao Trưởng nhóm trước.");
        }

        string removedName = member.User.FullName;
        _db.ClubMembers.Remove(member);
        await _db.SaveChangesAsync();
        return (true, $"Đã mời {removedName} rời khỏi nhóm CLB \"{hub.Name}\".");
    }

    public async Task<(bool Success, string Message)> CreateClubPostAsync(
        string currentUserId,
        string currentUserRole,
        S20CreateClubPostDto request)
    {
        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        if (hub == null) return (false, "Không tìm thấy Câu lạc bộ.");

        if (!await CanManageClubAsync(hub, currentUserId, currentUserRole))
        {
            throw new UnauthorizedAccessException("Chỉ Quản trị viên, Trưởng nhóm hoặc Phó nhóm CLB mới được đăng thông báo lên bảng tin nhóm.");
        }

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
        {
            return (false, "Tiêu đề và nội dung thông báo CLB không được để trống.");
        }

        string cleanTitle = request.Title.Trim();
        string cleanContent = request.Content.Trim();
        if (cleanTitle.Length > 180 || cleanContent.Length > 4000)
        {
            return (false, "Tiêu đề tối đa 180 ký tự và nội dung thông báo CLB tối đa 4.000 ký tự.");
        }

        var (allowed, retrySec, rlMsg) = _rateLimiter.TryConsume(
            "s20-club-post",
            currentUserId,
            maxRequests: 12,
            window: TimeSpan.FromMinutes(5),
            customMessage: "Bạn đã đăng quá nhiều thông báo lên bảng tin CLB trong 5 phút. Vui lòng đợi giây lát.");
        if (!allowed)
        {
            throw new RateLimitExceededException(rlMsg, retrySec);
        }

        _db.ClubPosts.Add(new ClubPost
        {
            HubId = hub.Id,
            AuthorUserId = currentUserId,
            Title = cleanTitle,
            Content = cleanContent,
            IsPinned = request.IsPinned,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return (true, $"Đã đăng thông báo mới lên bảng tin CLB \"{hub.Name}\"!");
    }

    public async Task<(bool Success, string Message)> DeleteClubPostAsync(
        string currentUserId,
        string currentUserRole,
        S20DeleteClubPostDto request)
    {
        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        if (hub == null) return (false, "Không tìm thấy Câu lạc bộ.");

        if (!await CanManageClubAsync(hub, currentUserId, currentUserRole))
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xóa bài đăng bảng tin của CLB này.");
        }

        var post = await _db.ClubPosts.FirstOrDefaultAsync(p => p.Id == request.PostId && p.HubId == hub.Id);
        if (post == null) return (false, "Không tìm thấy thông báo CLB cần xóa.");

        _db.ClubPosts.Remove(post);
        await _db.SaveChangesAsync();
        return (true, "Đã xóa thông báo khỏi bảng tin CLB.");
    }

    public async Task<(bool Success, string Message)> RegisterCopyDropoffAtHubAsync(
        string currentUserId,
        string currentUserRole,
        S20RegisterCopyDropoffDto request)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách cần đăng ký gửi tại Trạm Hub.");
        }

        bool isAdmin = string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        if (copy.OwnerUserId != currentUserId && !isAdmin)
        {
            throw new UnauthorizedAccessException("Bạn chỉ được phép đăng ký ký gửi những bản sao sách thuộc sở hữu của mình.");
        }

        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        if (hub == null)
        {
            return (false, "Không tìm thấy Điểm hẹn CLB / Hub đã chọn.");
        }

        copy.LibraryId = hub.Id;
        copy.ShelfLocation = $"Ký gửi tại {hub.Name[..Math.Min(22, hub.Name.Length)]}";
        copy.IsLendable = true;

        await _db.SaveChangesAsync();

        return (true,
            $"Đã đăng ký ký gửi bản sao [{copy.Barcode}] (\"{copy.Book.Title}\") tại điểm hẹn \"{hub.Name}\"! Trưởng nhóm CLB sẽ xác nhận kiểm định sách thực tế.");
    }

    public async Task<(bool Success, string Message)> CuratorVerifyPhysicalCopyAsync(
        string curatorUserId,
        string curatorUserRole,
        S20CuratorVerifyCopyDto request)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .Include(bc => bc.OwnerUser)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId);

        int targetHubId = request.HubId > 0 ? request.HubId : (copy?.LibraryId ?? 0);
        Library? hub = targetHubId > 0
            ? await _db.Libraries.FirstOrDefaultAsync(l => l.Id == targetHubId)
            : null;

        bool isAdmin = string.Equals(curatorUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        if (hub != null)
        {
            if (!await CanManageClubAsync(hub, curatorUserId, curatorUserRole))
            {
                throw new UnauthorizedAccessException("Chỉ Quản trị viên hoặc Ban điều hành (Trưởng/Phó nhóm) của chính CLB này mới được kiểm định sách tại trạm.");
            }
        }
        else if (!isAdmin)
        {
            throw new UnauthorizedAccessException("Bạn cần là Trưởng/Phó nhóm của một Câu lạc bộ hợp lệ hoặc Quản trị viên để kiểm định sách tại trạm.");
        }

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách cần kiểm định.");
        }

        var allowedConditions = new[] { "New", "Good", "Fair", "Worn" };
        copy.Condition = allowedConditions.Contains(request.VerifiedCondition) ? request.VerifiedCondition : "Good";
        copy.LibraryId = request.HubId > 0 ? request.HubId : copy.LibraryId;
        copy.ShelfLocation = string.IsNullOrWhiteSpace(request.ShelfLocation)
            ? "Trạm CLB-K1 (Đã kiểm định)"
            : request.ShelfLocation.Trim();
        copy.IsLendable = true;

        string verifyMarker = $"[{copy.Barcode}]";
        bool alreadyVerified = await _db.Notifications.AnyAsync(n =>
            n.UserId == copy.OwnerUserId &&
            n.Title.Contains("Kiểm định sách tại Trạm CLB") &&
            n.Title.Contains(verifyMarker));

        string trustNotice = "Đã cập nhật trạng thái kiểm định (điểm thưởng kiểm định chỉ cộng 1 lần/bản sao).";
        if (!alreadyVerified && copy.OwnerUser != null)
        {
            copy.OwnerUser.TrustScore = Math.Clamp(copy.OwnerUser.TrustScore + 2, 0, 200);
            _db.Notifications.Add(new Notification
            {
                UserId = copy.OwnerUserId!,
                Title = $"Kiểm định sách tại Trạm CLB {verifyMarker}",
                Content = $"Trưởng nhóm CLB đã kiểm định bản sao {verifyMarker} (\"{copy.Book.Title}\") và cộng +2 điểm uy tín.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = DateTime.UtcNow
            });
            trustNotice = "cộng +2 điểm uy tín cho chủ sách!";
        }

        await _db.SaveChangesAsync();

        return (true,
            $"Đã xác nhận kiểm định sách thực tế cho bản sao [{copy.Barcode}] (\"{copy.Book.Title}\"): Tình trạng [{copy.Condition}], vị trí [{copy.ShelfLocation}], {trustNotice}");
    }

    public async Task<(bool Success, string Message)> CuratorProcessHubHandoverAsync(
        string curatorUserId,
        string curatorUserRole,
        S20CuratorHandoverActionDto request)
    {
        var req = await _db.LoanRequests
            .Include(lr => lr.BookCopy).ThenInclude(bc => bc.Book)
            .Include(lr => lr.BookCopy).ThenInclude(bc => bc.OwnerUser)
            .Include(lr => lr.Borrower)
            .Include(lr => lr.PickupHub)
            .Include(lr => lr.Loan)
            .FirstOrDefaultAsync(lr => lr.Id == request.LoanRequestId);

        bool isAdmin = string.Equals(curatorUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        if (req == null)
        {
            if (!isAdmin && !string.Equals(curatorUserRole, "Curator", StringComparison.OrdinalIgnoreCase))
            {
                bool managesAnyClub = await _db.Libraries.AnyAsync(l => l.ManagerUserId == curatorUserId) ||
                    await _db.ClubMembers.AnyAsync(m => m.UserId == curatorUserId && m.Status == "Approved" && (m.MemberRole == "Leader" || m.MemberRole == "ViceLeader"));
                if (!managesAnyClub)
                {
                    throw new UnauthorizedAccessException("Chỉ Quản trị viên hoặc Ban điều hành (Trưởng/Phó nhóm) CLB mới được xác nhận giao nhận tại Điểm trạm.");
                }
            }
            return (false, "Không tìm thấy đơn mượn tại Trạm CLB.");
        }

        if (req.PickupHub != null)
        {
            if (!await CanManageClubAsync(req.PickupHub, curatorUserId, curatorUserRole))
            {
                throw new UnauthorizedAccessException("Chỉ Quản trị viên hoặc Trưởng/Phó nhóm phụ trách Điểm trạm CLB của đơn này mới được xác nhận giao nhận.");
            }
        }
        else if (!isAdmin)
        {
            throw new UnauthorizedAccessException("Đơn mượn này không gắn với điểm trạm CLB nào bạn quản lý.");
        }

        var now = DateTime.UtcNow;
        var copy = req.BookCopy;

        if (request.ActionType == "CheckInDropoff")
        {
            if (req.Status is "Rejected" or "Cancelled" or "Expired")
            {
                return (false, $"Đơn mượn #{req.Id} đã ở trạng thái '{req.Status}', không thể ký gửi.");
            }
            req.Status = "Approved";
            req.ApproverId ??= curatorUserId;
            copy.Status = "InTransit";

            _db.Notifications.Add(new Notification
            {
                UserId = req.BorrowerId,
                Title = $"Sách [{copy.Barcode}] đã sẵn sàng nhận tại Trạm CLB",
                Content = $"Chủ sách đã ký gửi \"{copy.Book.Title}\" tại điểm trạm. Mời bạn tới nhận sách.",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();

            return (true,
                $"Trạm CLB đã xác nhận chủ sách ký gửi bản sao [{copy.Barcode}] tại quầy. Sẵn sàng bàn giao cho {req.Borrower.FullName}!");
        }

        if (request.ActionType == "HandoverToBorrower")
        {
            if (req.Loan != null)
            {
                return (false, $"Đơn mượn #{req.Id} đã được bàn giao và kích hoạt hợp đồng trước đó.");
            }

            if (req.Status is "Rejected" or "Cancelled" or "Expired")
            {
                return (false, $"Đơn mượn #{req.Id} ở trạng thái '{req.Status}', không thể bàn giao.");
            }

            req.Status = "Approved";
            req.ApproverId ??= curatorUserId;
            copy.Status = "Borrowed";

            var loan = new Loan
            {
                LoanRequestId = req.Id,
                StartDate = now,
                DueDate = now.AddDays(req.DesiredDays > 0 ? req.DesiredDays : 14),
                RenewCount = 0,
                FineAmount = 0m,
                Status = "Active"
            };
            _db.Loans.Add(loan);

            if (copy.OwnerUser != null)
            {
                bool farmedRecently = await _db.Loans.AnyAsync(l =>
                    l.LoanRequest.BookCopyId == copy.Id &&
                    l.LoanRequest.BorrowerId == req.BorrowerId &&
                    l.StartDate >= now.AddHours(-24));
                if (!farmedRecently)
                {
                    copy.OwnerUser.LendQuotaBonus = Math.Min(copy.OwnerUser.LendQuotaBonus + 1, 10);
                    copy.OwnerUser.TrustScore = Math.Clamp(copy.OwnerUser.TrustScore + 3, 0, 200);
                }
            }

            await _db.SaveChangesAsync();

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = loan.Id,
                ReportType = "Handover",
                ReportedByUserId = curatorUserId,
                ConditionGrade = copy.Condition,
                Note = $"Biên bản giao sách trung gian tại Trạm CLB cho bản sao [{copy.Barcode}].",
                ImageUrl = copy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
                CreatedAt = now
            });

            _db.Notifications.Add(new Notification
            {
                UserId = req.BorrowerId,
                Title = $"Đã nhận sách [{copy.Barcode}] tại Trạm CLB",
                Content = $"Hợp đồng mượn cuốn \"{copy.Book.Title}\" đã bắt đầu (Hạn trả: {loan.DueDate:dd/MM/yyyy}).",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            if (copy.OwnerUserId != null)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = copy.OwnerUserId,
                    Title = $"Trạm CLB đã giao sách [{copy.Barcode}] cho người mượn",
                    Content = "Bạn được cộng +1 suất mượn thưởng và +3 điểm uy tín.",
                    Type = "LoanApproved",
                    IsRead = false,
                    EmailSentViaResend = false,
                    CreatedAt = now
                });
            }

            await _db.SaveChangesAsync();

            return (true,
                $"Trạm CLB đã bàn giao bản sao [{copy.Barcode}] cho {req.Borrower.FullName}! Đã tạo biên bản bàn giao và cộng +1 suất mượn thưởng, +3 điểm uy tín cho chủ sách.");
        }

        // ConfirmReturnAtHub
        if (req.Loan == null || req.Loan.Status == "Returned")
        {
            return (false, "Đơn mượn này chưa có hợp đồng đang hoạt động hoặc đã hoàn trả trước đó.");
        }

        req.Loan.Status = "Returned";
        req.Loan.ReturnDate = now;
        req.Borrower.TrustScore = Math.Clamp(req.Borrower.TrustScore + 2, 0, 200);

        _db.ConditionReports.Add(new ConditionReport
        {
            LoanId = req.Loan.Id,
            ReportType = "Return",
            ReportedByUserId = curatorUserId,
            ConditionGrade = copy.Condition,
            Note = $"Biên bản thu hồi sách nguyên vẹn tại Trạm CLB cho bản sao [{copy.Barcode}].",
            ImageUrl = copy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
            CreatedAt = now
        });

        var (notifiedNext, notifiedUserName) = await _loanCoordinator.HandleCopyReturnedAndNotifyQueueAsync(copy, now);

        _db.Notifications.Add(new Notification
        {
            UserId = req.BorrowerId,
            Title = $"Đã trả sách [{copy.Barcode}] tại Trạm CLB",
            Content = $"Cảm ơn bạn đã hoàn trả cuốn \"{copy.Book.Title}\" nguyên vẹn (+2 điểm uy tín).",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        string queueNotice = notifiedNext ? $" Đã tự động thông báo cho bạn đọc đứng đầu hàng chờ ({notifiedUserName})." : string.Empty;
        return (true,
            $"Trạm CLB đã thu hồi bản sao [{copy.Barcode}] từ {req.Borrower.FullName} và cộng +2 điểm uy tín!{queueNotice}");
    }

    public async Task<(bool Success, string Message)> UpdateHubStationInfoAsync(
        string curatorUserId,
        string curatorUserRole,
        S20UpdateHubStationDto request)
    {
        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        bool isAdmin = string.Equals(curatorUserRole, "Admin", StringComparison.OrdinalIgnoreCase);

        if (hub != null)
        {
            if (!await IsHubLeaderOrAdminAsync(hub, curatorUserId, curatorUserRole))
            {
                throw new UnauthorizedAccessException("Chỉ Quản trị viên hoặc Trưởng nhóm phụ trách CLB này mới được chỉnh sửa thông tin trạm.");
            }
        }
        else
        {
            if (!isAdmin)
            {
                bool leadsAnyClub = await _db.Libraries.AnyAsync(l => l.ManagerUserId == curatorUserId) ||
                    await _db.ClubMembers.AnyAsync(m => m.UserId == curatorUserId && m.Status == "Approved" && m.MemberRole == "Leader");
                if (!leadsAnyClub)
                {
                    throw new UnauthorizedAccessException("Chỉ Quản trị viên hoặc Trưởng nhóm phụ trách CLB mới được chỉnh sửa thông tin trạm.");
                }
            }
            return (false, "Không tìm thấy Điểm trạm CLB / Thư viện cần cập nhật.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Address))
        {
            return (false, "Tên điểm trạm và Địa chỉ không được để trống.");
        }

        hub.Name = request.Name.Trim();
        hub.Address = request.Address.Trim();
        if (!string.IsNullOrWhiteSpace(request.OpenHours))
        {
            hub.OpenHours = request.OpenHours.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            hub.Phone = request.Phone.Trim();
        }
        if (request.Description != null)
        {
            hub.Description = request.Description.Trim();
        }
        if (request.ClubRules != null)
        {
            hub.ClubRules = request.ClubRules.Trim();
        }
        if (request.IsPublicClub.HasValue)
        {
            hub.IsPublicClub = request.IsPublicClub.Value;
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã cập nhật thông tin vận hành & điều lệ cho \"{hub.Name}\" thành công!");
    }
}
