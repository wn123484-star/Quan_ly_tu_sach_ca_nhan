using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class AspNetUser
{
    public string Id { get; set; } = null!;

    public string? UserName { get; set; }

    public string? NormalizedUserName { get; set; }

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public bool EmailConfirmed { get; set; }

    public string? PasswordHash { get; set; }

    public string? SecurityStamp { get; set; }

    public string? ConcurrencyStamp { get; set; }

    public string? PhoneNumber { get; set; }

    public bool PhoneNumberConfirmed { get; set; }

    public bool TwoFactorEnabled { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public bool LockoutEnabled { get; set; }

    public int AccessFailedCount { get; set; }

    public string FullName { get; set; } = null!;

    public string? Address { get; set; }

    public string? AvatarUrl { get; set; }

    public int TrustScore { get; set; }

    public int LendQuotaBonus { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AiChatMessage> AiChatMessages { get; set; } = new List<AiChatMessage>();

    public virtual ICollection<AiChatSession> AiChatSessions { get; set; } = new List<AiChatSession>();

    public virtual ICollection<AspNetUserClaim> AspNetUserClaims { get; set; } = new List<AspNetUserClaim>();

    public virtual ICollection<AspNetUserLogin> AspNetUserLogins { get; set; } = new List<AspNetUserLogin>();

    public virtual ICollection<AspNetUserToken> AspNetUserTokens { get; set; } = new List<AspNetUserToken>();

    public virtual ICollection<BookCopy> BookCopies { get; set; } = new List<BookCopy>();

    public virtual ICollection<ConditionReport> ConditionReports { get; set; } = new List<ConditionReport>();

    public virtual ICollection<Dispute> DisputeRaisedByUsers { get; set; } = new List<Dispute>();

    public virtual ICollection<Dispute> DisputeResolvedByAdmins { get; set; } = new List<Dispute>();

    public virtual ICollection<Library> Libraries { get; set; } = new List<Library>();

    public virtual ICollection<LoanRequest> LoanRequestApprovers { get; set; } = new List<LoanRequest>();

    public virtual ICollection<LoanRequest> LoanRequestBorrowers { get; set; } = new List<LoanRequest>();

    public virtual ICollection<Membership> Memberships { get; set; } = new List<Membership>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<OtpVerification> OtpVerifications { get; set; } = new List<OtpVerification>();

    public virtual ICollection<ReadingProgress> ReadingProgresses { get; set; } = new List<ReadingProgress>();

    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    public virtual ICollection<Shelf> Shelves { get; set; } = new List<Shelf>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public virtual ICollection<AspNetRole> Roles { get; set; } = new List<AspNetRole>();
}
