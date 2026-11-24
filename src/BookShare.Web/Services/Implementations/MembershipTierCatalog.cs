using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// Nguồn chân lý duy nhất cho chính sách 4 cấp gói hội viên mượn sách (100% tiền ảo nội bộ, 0đ giá bán sách).
/// </summary>
public static class MembershipTierCatalog
{
    public const decimal TierSwitchFee = 15_000m;

    public sealed record TierSwitchQuote(
        string CurrentTier,
        string TargetTier,
        int DurationMonths,
        bool IsSwitch,
        bool IsSameActiveTier,
        int RemainingDays,
        decimal NewPlanPrice,
        decimal ProrationCredit,
        decimal SwitchFee,
        decimal TotalPayable);

    public sealed record TierPolicy(
        string Tier,
        string DisplayName,
        string ShortTag,
        int MaxConcurrentLoans,
        int MaxBorrowDays,
        int MaxRenewals,
        int RenewalDays,
        int PriorityRank,
        decimal Price1Month,
        decimal Price6Months,
        decimal Price12Months,
        string AllowedBooksSummary,
        string DepositPolicy,
        string QueuePriorityLabel,
        string[] Highlights);

    public static readonly TierPolicy Free = new(
        Tier: "Free",
        DisplayName: "Độc Giả Cơ Bản",
        ShortTag: "GÓI MIỄN PHÍ",
        MaxConcurrentLoans: 2,
        MaxBorrowDays: 14,
        MaxRenewals: 1,
        RenewalDays: 7,
        PriorityRank: 0,
        Price1Month: 0m,
        Price6Months: 0m,
        Price12Months: 0m,
        AllowedBooksSummary: "Kho chung Thư viện & sách P2P phổ thông (không bao gồm sách hot có ≥ 3 người chờ)",
        DepositPolicy: "Bảo lãnh tiêu chuẩn theo từng đầu sách",
        QueuePriorityLabel: "Hàng chờ thường",
        Highlights: new[]
        {
            "Mượn đồng thời tối đa 2 cuốn (+ thưởng chia sẻ P2P)",
            "Thời hạn mượn tối đa 14 ngày / lượt, gia hạn 1 lần (+7 ngày)",
            "Mượn sách Kho chung Thư viện & sách cộng đồng phổ thông",
            "Theo dõi tiến độ đọc & sổ tay trích dẫn không giới hạn"
        });

    public static readonly TierPolicy Standard = new(
        Tier: "Standard",
        DisplayName: "Hội Viên Đọc Sâu",
        ShortTag: "PHỔ BIẾN",
        MaxConcurrentLoans: 3,
        MaxBorrowDays: 21,
        MaxRenewals: 2,
        RenewalDays: 7,
        PriorityRank: 1,
        Price1Month: 29000m,
        Price6Months: 149000m,
        Price12Months: 249000m,
        AllowedBooksSummary: "Toàn bộ Kho chung Thư viện + Toàn bộ Tủ sách P2P Cộng đồng & sách chuyên ngành",
        DepositPolicy: "Bảo lãnh tiêu chuẩn + ưu tiên xác nhận tại Trạm CLB",
        QueuePriorityLabel: "Ưu tiên Bạc (trên gói Free)",
        Highlights: new[]
        {
            "Mượn đồng thời tối đa 3 cuốn (+ thưởng chia sẻ P2P)",
            "Thời hạn mượn tối đa 21 ngày / lượt, gia hạn 2 lần (+7 ngày)",
            "Mở khóa toàn bộ Tủ sách P2P & sách học thuật chuyên ngành",
            "Xếp hàng ưu tiên Bạc khi đặt trước Sách Hot"
        });

    public static readonly TierPolicy Premium = new(
        Tier: "Premium",
        DisplayName: "Hội Viên Học Thuật",
        ShortTag: "KHUYÊN DÙNG",
        MaxConcurrentLoans: 5,
        MaxBorrowDays: 30,
        MaxRenewals: 2,
        RenewalDays: 10,
        PriorityRank: 2,
        Price1Month: 49000m,
        Price6Months: 249000m,
        Price12Months: 399000m,
        AllowedBooksSummary: "Toàn bộ Kho Thư viện + P2P + Giáo trình hiếm, sách chuyên khảo & Sách Hot",
        DepositPolicy: "Giảm 50% ký quỹ bảo lãnh P2P",
        QueuePriorityLabel: "Ưu tiên Vàng (nhảy đầu hàng chờ)",
        Highlights: new[]
        {
            "Mượn đồng thời tối đa 5 cuốn (+ thưởng chia sẻ P2P)",
            "Thời hạn mượn tối đa 30 ngày / lượt, gia hạn 2 lần (+10 ngày)",
            "Mượn trọn vẹn sách chuyên khảo, giáo trình hiếm & Sách Hot",
            "Ưu tiên Vàng nhảy lên đầu hàng chờ + Giảm 50% cọc bảo lãnh"
        });

    public static readonly TierPolicy Scholar = new(
        Tier: "Scholar",
        DisplayName: "Nghiên Cứu VIP",
        ShortTag: "CAO CẤP NHẤT",
        MaxConcurrentLoans: 10,
        MaxBorrowDays: 45,
        MaxRenewals: 3,
        RenewalDays: 14,
        PriorityRank: 3,
        Price1Month: 89000m,
        Price6Months: 449000m,
        Price12Months: 699000m,
        AllowedBooksSummary: "Toàn hệ sinh thái BookShare (Kho Thư viện + P2P + Sách hiếm / Nghiên cứu + Ưu tiên CLB)",
        DepositPolicy: "Miễn 100% ký quỹ bảo lãnh P2P",
        QueuePriorityLabel: "Ưu tiên VIP Cao nhất (Top 1)",
        Highlights: new[]
        {
            "Mượn đồng thời tối đa 10 cuốn (+ thưởng chia sẻ P2P)",
            "Thời hạn mượn tối đa 45 ngày / lượt, gia hạn 3 lần (+14 ngày)",
            "Mở khóa mọi đầu sách hiếm, tài liệu nghiên cứu & ký gửi VIP",
            "Miễn 100% cọc bảo lãnh & ưu tiên Top 1 hàng chờ Sách Hot"
        });

    public static readonly TierPolicy[] AllPlans = { Free, Standard, Premium, Scholar };

    public static string NormalizeTier(string? tier) => (tier ?? string.Empty).Trim() switch
    {
        var t when t.Equals("Standard", StringComparison.OrdinalIgnoreCase) => "Standard",
        var t when t.Equals("Premium", StringComparison.OrdinalIgnoreCase) => "Premium",
        var t when t.Equals("Scholar", StringComparison.OrdinalIgnoreCase) => "Scholar",
        _ => "Free"
    };

    public static TierPolicy GetPolicy(string? tier) => NormalizeTier(tier) switch
    {
        "Standard" => Standard,
        "Premium" => Premium,
        "Scholar" => Scholar,
        _ => Free
    };

    public static int NormalizeDurationMonths(int durationMonths) => durationMonths switch
    {
        1 => 1,
        6 => 6,
        _ => 12
    };

    public static decimal GetPrice(string? tier, int durationMonths)
    {
        var policy = GetPolicy(tier);
        return NormalizeDurationMonths(durationMonths) switch
        {
            1 => policy.Price1Month,
            6 => policy.Price6Months,
            _ => policy.Price12Months
        };
    }

    public static TierPolicy GetEffectivePolicy(IEnumerable<Membership>? memberships, DateTime nowUtc)
    {
        if (memberships == null)
        {
            return Free;
        }

        var best = memberships
            .Where(m => m.IsActive && (!m.ExpiryDate.HasValue || m.ExpiryDate.Value > nowUtc))
            .Select(m => GetPolicy(m.Tier))
            .OrderByDescending(p => p.PriorityRank)
            .FirstOrDefault();

        return best ?? Free;
    }

    public static TierSwitchQuote CalculateSwitchQuote(
        Membership? activePaidRecord,
        string? targetTier,
        int durationMonths,
        DateTime nowUtc)
    {
        string normalizedTarget = NormalizeTier(targetTier);
        int months = NormalizeDurationMonths(durationMonths);
        decimal newPrice = GetPrice(normalizedTarget, months);

        bool hasActivePaid = activePaidRecord != null
            && activePaidRecord.IsActive
            && !activePaidRecord.Tier.Equals("Free", StringComparison.OrdinalIgnoreCase)
            && (!activePaidRecord.ExpiryDate.HasValue || activePaidRecord.ExpiryDate.Value > nowUtc);

        string currentTier = hasActivePaid ? NormalizeTier(activePaidRecord!.Tier) : "Free";
        int remainingDays = hasActivePaid && activePaidRecord!.ExpiryDate.HasValue
            ? Math.Max(0, (int)Math.Ceiling((activePaidRecord.ExpiryDate.Value - nowUtc).TotalDays))
            : 0;

        if (!hasActivePaid || normalizedTarget == "Free")
        {
            return new TierSwitchQuote(
                CurrentTier: currentTier,
                TargetTier: normalizedTarget,
                DurationMonths: months,
                IsSwitch: false,
                IsSameActiveTier: false,
                RemainingDays: remainingDays,
                NewPlanPrice: newPrice,
                ProrationCredit: 0m,
                SwitchFee: 0m,
                TotalPayable: newPrice);
        }

        if (currentTier.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return new TierSwitchQuote(
                CurrentTier: currentTier,
                TargetTier: normalizedTarget,
                DurationMonths: months,
                IsSwitch: false,
                IsSameActiveTier: true,
                RemainingDays: remainingDays,
                NewPlanPrice: newPrice,
                ProrationCredit: 0m,
                SwitchFee: 0m,
                TotalPayable: 0m);
        }

        double totalSpanDays = activePaidRecord!.ExpiryDate.HasValue
            ? Math.Max(30.0, (activePaidRecord.ExpiryDate.Value - activePaidRecord.StartDate).TotalDays)
            : 30.0;
        double remSpanDays = activePaidRecord.ExpiryDate.HasValue
            ? Math.Clamp((activePaidRecord.ExpiryDate.Value - nowUtc).TotalDays, 0.0, totalSpanDays)
            : 0.0;
        int estimatedOldMonths = totalSpanDays <= 45 ? 1 : totalSpanDays <= 210 ? 6 : 12;
        decimal oldPrice = GetPrice(currentTier, estimatedOldMonths);
        decimal rawCredit = oldPrice * (decimal)(remSpanDays / totalSpanDays);
        decimal prorationCredit = Math.Min(newPrice, Math.Floor(rawCredit / 1000m) * 1000m);
        decimal totalPayable = Math.Max(TierSwitchFee, newPrice - prorationCredit + TierSwitchFee);

        return new TierSwitchQuote(
            CurrentTier: currentTier,
            TargetTier: normalizedTarget,
            DurationMonths: months,
            IsSwitch: true,
            IsSameActiveTier: false,
            RemainingDays: remainingDays,
            NewPlanPrice: newPrice,
            ProrationCredit: prorationCredit,
            SwitchFee: TierSwitchFee,
            TotalPayable: totalPayable);
    }

    public static List<MembershipTierPlanDto> ToPlanDtos() => AllPlans.Select(p => new MembershipTierPlanDto
    {
        Tier = p.Tier,
        DisplayName = p.DisplayName,
        ShortTag = p.ShortTag,
        MaxConcurrentLoans = p.MaxConcurrentLoans,
        MaxBorrowDays = p.MaxBorrowDays,
        MaxRenewals = p.MaxRenewals,
        RenewalDays = p.RenewalDays,
        PriorityRank = p.PriorityRank,
        Price1Month = p.Price1Month,
        Price6Months = p.Price6Months,
        Price12Months = p.Price12Months,
        AllowedBooksSummary = p.AllowedBooksSummary,
        DepositPolicy = p.DepositPolicy,
        QueuePriorityLabel = p.QueuePriorityLabel,
        Highlights = p.Highlights.ToList()
    }).ToList();
}
