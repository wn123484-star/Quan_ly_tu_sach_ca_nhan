using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Library
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string Address { get; set; } = null!;

    public string ManagerUserId { get; set; } = null!;

    public string? Phone { get; set; }

    public string HubType { get; set; } = null!;

    public string? OpenHours { get; set; }

    public bool IsActive { get; set; }

    public string? Description { get; set; }

    public string? ClubRules { get; set; }

    public bool IsPublicClub { get; set; } = true;

    public virtual ICollection<BookCopy> BookCopies { get; set; } = new List<BookCopy>();

    public virtual ICollection<ClubMember> ClubMembers { get; set; } = new List<ClubMember>();

    public virtual ICollection<ClubPost> ClubPosts { get; set; } = new List<ClubPost>();

    public virtual ICollection<LoanRequest> LoanRequests { get; set; } = new List<LoanRequest>();

    public virtual AspNetUser ManagerUser { get; set; } = null!;
}
