using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S04BookNotesService : IS04BookNotesService
{
    private readonly BookShareDbContext _db;

    public S04BookNotesService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S04BookNotesPageViewModel> GetBookNotesPageAsync(
        string currentUserId,
        int? bookId = null,
        bool showCommunityPublic = false)
    {
        var user = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        var allBooks = await _db.Books
            .OrderBy(b => b.Title)
            .Select(b => new CatalogBookOptionDto
            {
                Id = b.Id,
                Title = b.Title,
                Author = b.Author,
                ISBN = b.Isbn
            })
            .AsNoTracking()
            .ToListAsync();

        var myNotesCount = await _db.BookNotes
            .CountAsync(n => n.ReadingProgress.UserId == currentUserId);

        var publicNotesCount = await _db.BookNotes
            .CountAsync(n => n.IsPublic);

        var query = _db.BookNotes
            .Include(n => n.ReadingProgress)
                .ThenInclude(rp => rp.Book)
            .Include(n => n.ReadingProgress)
                .ThenInclude(rp => rp.User)
            .AsNoTracking()
            .AsQueryable();

        if (showCommunityPublic)
        {
            query = query.Where(n => n.IsPublic || n.ReadingProgress.UserId == currentUserId);
        }
        else
        {
            query = query.Where(n => n.ReadingProgress.UserId == currentUserId);
        }

        if (bookId.HasValue && bookId.Value > 0)
        {
            query = query.Where(n => n.ReadingProgress.BookId == bookId.Value);
        }

        var notes = await query
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        var noteCards = notes.Select(n => new S04NoteCardDto
        {
            NoteId = n.Id,
            ReadingProgressId = n.ReadingProgressId,
            BookId = n.ReadingProgress.BookId,
            BookTitle = n.ReadingProgress.Book.Title,
            BookAuthor = n.ReadingProgress.Book.Author,
            CoverImageUrl = n.ReadingProgress.Book.CoverImageUrl,
            PageNumber = n.PageNumber,
            QuoteText = n.QuoteText,
            NoteContent = n.NoteContent,
            IsPublic = n.IsPublic,
            OwnerUserId = n.ReadingProgress.UserId,
            OwnerFullName = n.ReadingProgress.User.FullName,
            IsOwner = n.ReadingProgress.UserId == currentUserId,
            CreatedAt = n.CreatedAt
        }).ToList();

        return new S04BookNotesPageViewModel
        {
            UserId = currentUserId,
            UserFullName = user?.FullName ?? "Nguyễn Hùng (BIT250157)",
            SelectedBookId = bookId,
            ShowCommunityPublic = showCommunityPublic,
            TotalMyNotesCount = myNotesCount,
            PublicNotesCount = publicNotesCount,
            MyTrackedBooks = allBooks,
            Notes = noteCards
        };
    }

    public async Task<(bool Success, string Message, S04NoteCardDto? Note)> SaveBookNoteAsync(
        string currentUserId,
        SaveBookNoteRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.NoteContent))
        {
            return (false, "Nội dung ghi chú / cảm nhận không được để trống.", null);
        }

        int pageNumber = Math.Max(1, request.PageNumber);

        // Chỉnh sửa ghi chú hiện có
        if (request.NoteId.HasValue && request.NoteId.Value > 0)
        {
            var existingNote = await _db.BookNotes
                .Include(n => n.ReadingProgress)
                    .ThenInclude(rp => rp.Book)
                .Include(n => n.ReadingProgress)
                    .ThenInclude(rp => rp.User)
                .FirstOrDefaultAsync(n => n.Id == request.NoteId.Value);

            if (existingNote == null)
            {
                return (false, "Không tìm thấy ghi chú cần chỉnh sửa.", null);
            }

            if (existingNote.ReadingProgress.UserId != currentUserId)
            {
                throw new UnauthorizedAccessException("Chặn IDOR: Bạn không có quyền sửa ghi chú của độc giả khác.");
            }

            existingNote.PageNumber = pageNumber;
            existingNote.QuoteText = request.QuoteText?.Trim();
            existingNote.NoteContent = request.NoteContent.Trim();
            existingNote.IsPublic = request.IsPublic;

            await _db.SaveChangesAsync();

            return (true, $"Đã cập nhật ghi chú tại trang {pageNumber} cuốn \"{existingNote.ReadingProgress.Book.Title}\"!", new S04NoteCardDto
            {
                NoteId = existingNote.Id,
                ReadingProgressId = existingNote.ReadingProgressId,
                BookId = existingNote.ReadingProgress.BookId,
                BookTitle = existingNote.ReadingProgress.Book.Title,
                BookAuthor = existingNote.ReadingProgress.Book.Author,
                CoverImageUrl = existingNote.ReadingProgress.Book.CoverImageUrl,
                PageNumber = existingNote.PageNumber,
                QuoteText = existingNote.QuoteText,
                NoteContent = existingNote.NoteContent,
                IsPublic = existingNote.IsPublic,
                OwnerUserId = currentUserId,
                OwnerFullName = existingNote.ReadingProgress.User.FullName,
                IsOwner = true,
                CreatedAt = existingNote.CreatedAt
            });
        }

        // Thêm mới ghi chú
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == request.BookId);
        if (book == null)
        {
            return (false, "Vui lòng chọn đầu sách hợp lệ để lưu ghi chú.", null);
        }

        var progress = await _db.ReadingProgresses
            .FirstOrDefaultAsync(rp => rp.UserId == currentUserId && rp.BookId == book.Id);

        if (progress == null)
        {
            progress = new ReadingProgress
            {
                UserId = currentUserId,
                BookId = book.Id,
                CurrentPage = Math.Min(pageNumber, book.PageCount),
                TotalPages = book.PageCount,
                Priority = "Medium",
                Status = "Reading",
                StartedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.ReadingProgresses.Add(progress);
            await _db.SaveChangesAsync();
        }

        var newNote = new BookNote
        {
            ReadingProgressId = progress.Id,
            PageNumber = Math.Min(pageNumber, book.PageCount),
            QuoteText = request.QuoteText?.Trim(),
            NoteContent = request.NoteContent.Trim(),
            IsPublic = request.IsPublic,
            CreatedAt = DateTime.UtcNow
        };

        _db.BookNotes.Add(newNote);
        await _db.SaveChangesAsync();

        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);

        return (true, $"Đã lưu trích dẫn & ghi chú tại trang {newNote.PageNumber} cuốn \"{book.Title}\"!", new S04NoteCardDto
        {
            NoteId = newNote.Id,
            ReadingProgressId = progress.Id,
            BookId = book.Id,
            BookTitle = book.Title,
            BookAuthor = book.Author,
            CoverImageUrl = book.CoverImageUrl,
            PageNumber = newNote.PageNumber,
            QuoteText = newNote.QuoteText,
            NoteContent = newNote.NoteContent,
            IsPublic = newNote.IsPublic,
            OwnerUserId = currentUserId,
            OwnerFullName = user?.FullName ?? "Độc giả BookShare",
            IsOwner = true,
            CreatedAt = newNote.CreatedAt
        });
    }

    public async Task<(bool Success, string Message)> TogglePublicAsync(
        string currentUserId,
        int noteId,
        bool isPublic)
    {
        var note = await _db.BookNotes
            .Include(n => n.ReadingProgress)
                .ThenInclude(rp => rp.Book)
            .FirstOrDefaultAsync(n => n.Id == noteId);

        if (note == null)
        {
            return (false, "Không tìm thấy ghi chú trong CSDL.");
        }

        if (note.ReadingProgress.UserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Chặn IDOR: Bạn không có quyền thay đổi chế độ hiển thị ghi chú của người khác.");
        }

        note.IsPublic = isPublic;
        await _db.SaveChangesAsync();

        string stateText = isPublic ? "Công khai cho cộng đồng cùng đọc" : "Riêng tư (Chỉ mình tôi xem)";
        return (true, $"Đã chuyển ghi chú trang {note.PageNumber} ({note.ReadingProgress.Book.Title}) sang chế độ: {stateText}.");
    }

    public async Task<(bool Success, string Message)> DeleteBookNoteAsync(
        string currentUserId,
        int noteId)
    {
        var note = await _db.BookNotes
            .Include(n => n.ReadingProgress)
            .FirstOrDefaultAsync(n => n.Id == noteId);

        if (note == null)
        {
            return (false, "Không tìm thấy ghi chú cần xóa.");
        }

        if (note.ReadingProgress.UserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Chặn IDOR: Bạn không có quyền xóa ghi chú của người khác.");
        }

        _db.BookNotes.Remove(note);
        await _db.SaveChangesAsync();

        return (true, "Đã xóa ghi chú cá nhân khỏi bảng BookNotes.");
    }
}
