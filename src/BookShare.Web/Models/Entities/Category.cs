using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string IconClass { get; set; } = null!;

    public virtual ICollection<Book> Books { get; set; } = new List<Book>();
}
