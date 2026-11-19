using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class ShelfItem
{
    public int Id { get; set; }

    public int ShelfId { get; set; }

    public int BookCopyId { get; set; }

    public DateTime AddedAt { get; set; }

    public virtual BookCopy BookCopy { get; set; } = null!;

    public virtual Shelf Shelf { get; set; } = null!;
}
