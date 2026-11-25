using System;

namespace BookShare.Web.Models.Entities;

public partial class ClubPost
{
    public int Id { get; set; }

    public int HubId { get; set; }

    public string AuthorUserId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    public bool IsPinned { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Library Hub { get; set; } = null!;

    public virtual AspNetUser AuthorUser { get; set; } = null!;
}
