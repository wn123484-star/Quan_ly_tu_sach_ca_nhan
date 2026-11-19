using System;

namespace BookShare.Web.Models.Entities;

// [BS-READING-ENTITY] Phiên tập trung Pomodoro & Tốc độ đọc sách
public partial class ReadingSession
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public int BookId { get; set; }

    public int DurationMinutes { get; set; }

    public int PagesRead { get; set; }

    public DateOnly SessionDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AspNetUser User { get; set; } = null!;

    public virtual Book Book { get; set; } = null!;
}
