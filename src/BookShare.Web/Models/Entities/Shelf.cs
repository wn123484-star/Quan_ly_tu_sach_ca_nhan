using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Shelf
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsPublic { get; set; }

    public int DisplayOrder { get; set; }

    public virtual ICollection<ShelfItem> ShelfItems { get; set; } = new List<ShelfItem>();

    public virtual AspNetUser User { get; set; } = null!;
}
