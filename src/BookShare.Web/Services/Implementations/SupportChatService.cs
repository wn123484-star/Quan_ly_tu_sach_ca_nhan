using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [BS-AGENT-AI-CHAT] Quản lý tin nhắn trực tiếp Độc giả <-> Thủ thư / Chủ sách trong bảng SupportChatMessages.
/// </summary>
public class SupportChatService : ISupportChatService
{
    private readonly BookShareDbContext _db;

    public SupportChatService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<List<SupportChatMessageItemDto>> GetMessagesAsync(
        string currentUserId,
        string currentRole,
        string channel = "Librarian")
    {
        string cleanChannel = string.Equals(channel, "Owner", StringComparison.OrdinalIgnoreCase) ? "Owner" : "Librarian";
        bool isStaff = currentRole == "Librarian" || currentRole == "Admin" || currentRole == "Curator";

        var query = _db.SupportChatMessages
            .AsNoTracking()
            .Include(m => m.SenderUser)
                .ThenInclude(u => u.Roles)
            .Where(m => m.Channel == cleanChannel);

        if (!isStaff)
        {
            query = query.Where(m =>
                m.SenderUserId == currentUserId ||
                m.ReceiverUserId == currentUserId ||
                m.ReceiverUserId == null);
        }

        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(40)
            .ToListAsync();

        rows.Reverse();

        return rows.Select(m => new SupportChatMessageItemDto
        {
            Id = m.Id,
            SenderUserId = m.SenderUserId,
            SenderName = m.SenderUser?.FullName ?? "Thành viên BookShare",
            SenderRole = m.SenderUser?.Roles.FirstOrDefault()?.Name ?? "User",
            ReceiverUserId = m.ReceiverUserId,
            Channel = m.Channel,
            MessageText = m.MessageText,
            IsMine = m.SenderUserId == currentUserId,
            CreatedAt = m.CreatedAt
        }).ToList();
    }

    public async Task<(bool Success, string Message, SupportChatMessageItemDto? SavedMessage)> SendMessageAsync(
        string senderUserId,
        string senderRole,
        SendSupportChatRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageText))
        {
            return (false, "Vui lòng nhập nội dung tin nhắn.", null);
        }

        string cleanChannel = string.Equals(request.Channel, "Owner", StringComparison.OrdinalIgnoreCase) ? "Owner" : "Librarian";
        var sender = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == senderUserId);

        if (sender == null)
        {
            return (false, "Không tìm thấy tài khoản gửi tin nhắn.", null);
        }

        string? receiverId = request.ReceiverUserId;
        if (string.IsNullOrWhiteSpace(receiverId))
        {
            bool isStaff = senderRole == "Librarian" || senderRole == "Admin";
            if (!isStaff && cleanChannel == "Librarian")
            {
                var lib = await _db.AspNetUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == "u-librarian-01" || u.Roles.Any(r => r.Name == "Librarian"));
                receiverId = lib?.Id;
            }
        }

        var entity = new SupportChatMessage
        {
            SenderUserId = sender.Id,
            ReceiverUserId = receiverId,
            Channel = cleanChannel,
            MessageText = request.MessageText.Trim(),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.SupportChatMessages.Add(entity);
        await _db.SaveChangesAsync();

        var dto = new SupportChatMessageItemDto
        {
            Id = entity.Id,
            SenderUserId = sender.Id,
            SenderName = sender.FullName,
            SenderRole = sender.Roles.FirstOrDefault()?.Name ?? senderRole,
            ReceiverUserId = entity.ReceiverUserId,
            Channel = entity.Channel,
            MessageText = entity.MessageText,
            IsMine = true,
            CreatedAt = entity.CreatedAt
        };

        return (true, "Đã gửi tin nhắn trực tiếp!", dto);
    }
}
