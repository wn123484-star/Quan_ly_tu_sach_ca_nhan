using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Reservation
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public string UserId { get; set; } = null!;

    public int QueuePosition { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? NotifiedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public virtual Book Book { get; set; } = null!;

    public virtual AspNetUser User { get; set; } = null!;
}
