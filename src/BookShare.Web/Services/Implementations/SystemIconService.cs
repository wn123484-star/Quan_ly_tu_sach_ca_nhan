using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Html;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BookShare.Web.Services.Implementations;

public class SystemIconService : ISystemIconService
{
    public const string CacheKey = "bookshare:system-icons:v1";
    private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB
    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".svg" };

    public static readonly (string IconKey, string DisplayName, string CategoryGroup, string DefaultIconClass)[] DefaultCatalog =
    {
        ("brand_logo", "Logo chính BookShare", "Thương hiệu", "bi-book-half"),
        ("brand_favicon", "Biểu tượng tab trình duyệt (Favicon)", "Thương hiệu", "bi-bookmark-star-fill"),
        ("nav_home", "Trang Chủ", "Thanh điều hướng", "bi-compass"),
        ("nav_discover", "Khám Phá", "Thanh điều hướng", "bi-globe2"),
        ("nav_shelf", "Tủ Sách & Đọc", "Thanh điều hướng", "bi-bookshelf"),
        ("nav_loan", "Mượn Trả & P2P", "Thanh điều hướng", "bi-arrow-left-right"),
        ("nav_community", "Ví & Cộng Đồng", "Thanh điều hướng", "bi-gem"),
        ("nav_admin", "Điều Hành Hệ Thống", "Thanh điều hướng", "bi-shield-lock-fill"),
        ("nav_cart_shelf", "Tủ Sách & Phiếu Mượn", "Thanh điều hướng", "bi-journal-bookmark-fill"),
        ("quick_library", "Kho Chung Thư Viện", "Lối tắt & Kho sách", "bi-buildings-fill"),
        ("quick_p2p", "Tủ Sách P2P Cộng Đồng", "Lối tắt & Kho sách", "bi-people-fill"),
        ("quick_filter", "Bộ Lọc Đa Tiêu Chí", "Lối tắt & Kho sách", "bi-funnel-fill"),
        ("quick_isbn", "Biên Mục Mã ISBN", "Lối tắt & Kho sách", "bi-upc-scan"),
        ("quick_hot", "Hàng Chờ Sách Hot", "Lối tắt & Kho sách", "bi-fire"),
        ("ai_fab", "Trợ Lý AI Thủ Thư", "AI & Tiện ích", "bi-stars"),
        ("qr_scan", "Quét Mã QR Đa Năng", "AI & Tiện ích", "bi-qr-code-scan"),
        ("book_placeholder", "Ảnh Bìa Sách Mặc Định", "Lối tắt & Kho sách", "bi-book"),
        ("avatar_default", "Ảnh Đại Diện Mặc Định", "AI & Tiện ích", "bi-person-circle")
    };

    private static readonly Dictionary<string, (string DisplayName, string CategoryGroup, string DefaultIconClass)> CatalogLookup =
        DefaultCatalog.ToDictionary(
            x => x.IconKey,
            x => (x.DisplayName, x.CategoryGroup, x.DefaultIconClass),
            StringComparer.OrdinalIgnoreCase);

    private readonly BookShareDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SystemIconService> _logger;

    public SystemIconService(
        BookShareDbContext db,
        IMemoryCache cache,
        IWebHostEnvironment env,
        ILogger<SystemIconService> logger)
    {
        _db = db;
        _cache = cache;
        _env = env;
        _logger = logger;
    }

    public async Task<List<SystemIconItemDto>> GetAllIconsAsync()
    {
        var dbItems = await _db.SystemIconSettings
            .AsNoTracking()
            .ToListAsync();

        var byKey = dbItems.ToDictionary(x => x.IconKey, StringComparer.OrdinalIgnoreCase);
        var result = new List<SystemIconItemDto>(DefaultCatalog.Length);

        foreach (var def in DefaultCatalog)
        {
            if (byKey.TryGetValue(def.IconKey, out var row))
            {
                result.Add(ToDto(row));
            }
            else
            {
                result.Add(new SystemIconItemDto
                {
                    IconKey = def.IconKey,
                    DisplayName = def.DisplayName,
                    CategoryGroup = def.CategoryGroup,
                    DefaultIconClass = def.DefaultIconClass,
                    CustomImageUrl = null,
                    UseCustomImage = false,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        return result;
    }

    public IHtmlContent RenderIconHtml(string iconKey, string fallbackClass, string extraCss = "")
    {
        var map = GetCachedMap();
        var encoder = HtmlEncoder.Default;
        var safeExtra = string.IsNullOrWhiteSpace(extraCss) ? string.Empty : " " + encoder.Encode(extraCss.Trim());

        if (map.TryGetValue(iconKey, out var item)
            && item.UseCustomImage
            && !string.IsNullOrWhiteSpace(item.CustomImageUrl))
        {
            var safeUrl = encoder.Encode(item.CustomImageUrl);
            var safeFallbackUrl = encoder.Encode($"/assets/system-icons/{Uri.EscapeDataString(iconKey)}");
            var safeAlt = encoder.Encode(item.DisplayName);
            var safeFallback = encoder.Encode(string.IsNullOrWhiteSpace(item.DefaultIconClass) ? fallbackClass : item.DefaultIconClass);
            var html = $"<img src=\"{safeUrl}\" alt=\"{safeAlt}\" class=\"bs-sys-icon{safeExtra}\" onerror=\"if(!this.dataset.dbTried){{this.dataset.dbTried='1';this.src='{safeFallbackUrl}';return;}}this.style.display='none';if(this.nextElementSibling)this.nextElementSibling.classList.remove('d-none');\" /><i class=\"bi {safeFallback}{safeExtra} d-none\" aria-hidden=\"true\"></i>";
            return new HtmlString(html);
        }

        var iconClass = map.TryGetValue(iconKey, out var defItem) && !string.IsNullOrWhiteSpace(defItem.DefaultIconClass)
            ? defItem.DefaultIconClass
            : fallbackClass;
        return new HtmlString($"<i class=\"bi {encoder.Encode(iconClass)}{safeExtra}\" aria-hidden=\"true\"></i>");
    }

    public string GetImageUrl(string iconKey, string fallbackUrl)
    {
        var map = GetCachedMap();
        if (map.TryGetValue(iconKey, out var item)
            && item.UseCustomImage
            && !string.IsNullOrWhiteSpace(item.CustomImageUrl))
        {
            return item.CustomImageUrl;
        }
        return fallbackUrl;
    }

    public async Task<(bool Success, string Message, SystemIconItemDto? Item)> UploadCustomIconAsync(
        string adminUserId,
        string iconKey,
        IFormFile file)
    {
        string cleanKey = (iconKey ?? string.Empty).Trim();
        if (!CatalogLookup.TryGetValue(cleanKey, out var def))
        {
            return (false, "Mã biểu tượng không nằm trong danh mục hỗ trợ của hệ thống.", null);
        }

        if (file == null || file.Length == 0)
        {
            return (false, "Vui lòng chọn một tệp ảnh biểu tượng hợp lệ trước khi tải lên.", null);
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return (false, "Dung lượng ảnh biểu tượng tối đa là 2MB.", null);
        }

        string ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
        {
            return (false, "Định dạng không được hỗ trợ. Chỉ chấp nhận tệp .png, .jpg, .jpeg, .webp hoặc .svg.", null);
        }

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        var (validContent, validationError) = ValidateImagePayload(ext, bytes);
        if (!validContent)
        {
            return (false, validationError, null);
        }

        string webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : _env.WebRootPath;
        string iconsDir = Path.Combine(webRoot, "uploads", "icons");
        Directory.CreateDirectory(iconsDir);

        string safeKey = Regex.Replace(cleanKey.ToLowerInvariant(), @"[^a-z0-9_]", "");
        string fileName = $"{safeKey}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
        string fullPath = Path.Combine(iconsDir, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes);

        string publicUrl = $"/uploads/icons/{fileName}";
        string contentType = ResolveContentType(ext);

        var entity = await _db.SystemIconSettings.FirstOrDefaultAsync(x => x.IconKey == cleanKey);
        string? previousUrl = entity?.CustomImageUrl;

        if (entity == null)
        {
            entity = new SystemIconSetting
            {
                IconKey = cleanKey,
                DisplayName = def.DisplayName,
                CategoryGroup = def.CategoryGroup,
                DefaultIconClass = def.DefaultIconClass,
                CustomImageUrl = publicUrl,
                CustomImageData = bytes,
                CustomImageContentType = contentType,
                UseCustomImage = true,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = adminUserId
            };
            _db.SystemIconSettings.Add(entity);
        }
        else
        {
            entity.CustomImageUrl = publicUrl;
            entity.CustomImageData = bytes;
            entity.CustomImageContentType = contentType;
            entity.UseCustomImage = true;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedByUserId = adminUserId;
        }

        await _db.SaveChangesAsync();
        TryDeleteOldIconFile(webRoot, previousUrl);
        _cache.Remove(CacheKey);

        return (true, $"Đã tải ảnh tùy chỉnh cho biểu tượng \"{entity.DisplayName}\" và bật áp dụng ngay trên toàn cổng!", ToDto(entity));
    }

    public async Task<(bool Success, string Message, SystemIconItemDto? Item)> ToggleIconModeAsync(
        string adminUserId,
        string iconKey,
        bool useCustomImage)
    {
        string cleanKey = (iconKey ?? string.Empty).Trim();
        if (!CatalogLookup.TryGetValue(cleanKey, out var def))
        {
            return (false, "Không tìm thấy biểu tượng yêu cầu trong hệ thống.", null);
        }

        var entity = await _db.SystemIconSettings.FirstOrDefaultAsync(x => x.IconKey == cleanKey);
        if (entity == null)
        {
            entity = new SystemIconSetting
            {
                IconKey = cleanKey,
                DisplayName = def.DisplayName,
                CategoryGroup = def.CategoryGroup,
                DefaultIconClass = def.DefaultIconClass,
                CustomImageUrl = null,
                UseCustomImage = false,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = adminUserId
            };
            _db.SystemIconSettings.Add(entity);
        }

        if (useCustomImage && string.IsNullOrWhiteSpace(entity.CustomImageUrl))
        {
            return (false, "Vui lòng tải ảnh tùy chỉnh lên trước khi gạt sang chế độ Ảnh tùy chỉnh.", ToDto(entity));
        }

        entity.UseCustomImage = useCustomImage;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedByUserId = adminUserId;

        await _db.SaveChangesAsync();
        _cache.Remove(CacheKey);

        string modeLabel = useCustomImage ? "Ảnh tùy chỉnh (Phải)" : "Mặc định hệ thống (Trái)";
        return (true, $"Đã chuyển biểu tượng \"{entity.DisplayName}\" sang chế độ {modeLabel}.", ToDto(entity));
    }

    public async Task<(bool Success, string Message)> ResetIconToDefaultAsync(
        string adminUserId,
        string iconKey)
    {
        string cleanKey = (iconKey ?? string.Empty).Trim();
        string webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : _env.WebRootPath;

        if (string.Equals(cleanKey, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var all = await _db.SystemIconSettings.ToListAsync();
            foreach (var item in all)
            {
                TryDeleteOldIconFile(webRoot, item.CustomImageUrl);
                item.CustomImageUrl = null;
                item.CustomImageData = null;
                item.CustomImageContentType = null;
                item.UseCustomImage = false;
                item.UpdatedAt = DateTime.UtcNow;
                item.UpdatedByUserId = adminUserId;
            }
            await _db.SaveChangesAsync();
            _cache.Remove(CacheKey);
            return (true, "Đã khôi phục toàn bộ 18 biểu tượng toàn trang về mặc định hệ thống!");
        }

        if (!CatalogLookup.TryGetValue(cleanKey, out _))
        {
            return (false, "Không tìm thấy mã biểu tượng cần khôi phục.");
        }

        var entity = await _db.SystemIconSettings.FirstOrDefaultAsync(x => x.IconKey == cleanKey);
        if (entity != null)
        {
            TryDeleteOldIconFile(webRoot, entity.CustomImageUrl);
            entity.CustomImageUrl = null;
            entity.CustomImageData = null;
            entity.CustomImageContentType = null;
            entity.UseCustomImage = false;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedByUserId = adminUserId;
            await _db.SaveChangesAsync();
        }

        _cache.Remove(CacheKey);
        return (true, $"Đã khôi phục biểu tượng \"{entity?.DisplayName ?? cleanKey}\" về mặc định hệ thống.");
    }

    public async Task<(byte[]? Bytes, string ContentType)> GetCustomIconBinaryAsync(string iconKey)
    {
        string cleanKey = (iconKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanKey))
        {
            return (null, "application/octet-stream");
        }

        var entity = await _db.SystemIconSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IconKey == cleanKey);

        if (entity == null || !entity.UseCustomImage)
        {
            return (null, "application/octet-stream");
        }

        if (entity.CustomImageData is { Length: > 0 })
        {
            string ct = string.IsNullOrWhiteSpace(entity.CustomImageContentType)
                ? ResolveContentType(Path.GetExtension(entity.CustomImageUrl ?? ".png"))
                : entity.CustomImageContentType;
            EnsureDiskIconFile(entity.CustomImageUrl, entity.CustomImageData);
            return (entity.CustomImageData, ct);
        }

        if (!string.IsNullOrWhiteSpace(entity.CustomImageUrl) &&
            entity.CustomImageUrl.StartsWith("/uploads/icons/", StringComparison.OrdinalIgnoreCase))
        {
            string webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                : _env.WebRootPath;
            string fullPath = Path.Combine(webRoot, "uploads", "icons", Path.GetFileName(entity.CustomImageUrl));
            if (File.Exists(fullPath))
            {
                var diskBytes = await File.ReadAllBytesAsync(fullPath);
                return (diskBytes, ResolveContentType(Path.GetExtension(fullPath)));
            }
        }

        return (null, "application/octet-stream");
    }

    private Dictionary<string, SystemIconItemDto> GetCachedMap()
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, SystemIconItemDto>? cached) && cached != null)
        {
            return cached;
        }

        var fallback = DefaultCatalog.ToDictionary(
            x => x.IconKey,
            x => new SystemIconItemDto
            {
                IconKey = x.IconKey,
                DisplayName = x.DisplayName,
                CategoryGroup = x.CategoryGroup,
                DefaultIconClass = x.DefaultIconClass,
                UseCustomImage = false
            },
            StringComparer.OrdinalIgnoreCase);

        try
        {
            var rows = _db.SystemIconSettings.AsNoTracking().ToList();
            foreach (var row in rows)
            {
                if (row.UseCustomImage && row.CustomImageData is { Length: > 0 } && !string.IsNullOrWhiteSpace(row.CustomImageUrl))
                {
                    EnsureDiskIconFile(row.CustomImageUrl, row.CustomImageData);
                }
                fallback[row.IconKey] = ToDto(row);
            }

            _cache.Set(CacheKey, fallback, TimeSpan.FromMinutes(15));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SystemIconSettings lookup fell back to defaults.");
        }

        return fallback;
    }

    private void EnsureDiskIconFile(string? customImageUrl, byte[]? bytes)
    {
        if (string.IsNullOrWhiteSpace(customImageUrl) || bytes is not { Length: > 0 } ||
            !customImageUrl.StartsWith("/uploads/icons/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            string webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                : _env.WebRootPath;
            string iconsDir = Path.Combine(webRoot, "uploads", "icons");
            string fileName = Path.GetFileName(customImageUrl);
            if (string.IsNullOrWhiteSpace(fileName)) return;
            string fullPath = Path.Combine(iconsDir, fileName);
            if (!File.Exists(fullPath))
            {
                Directory.CreateDirectory(iconsDir);
                File.WriteAllBytes(fullPath, bytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restore custom icon file {Url} to disk.", customImageUrl);
        }
    }

    private static string ResolveContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream"
    };

    private static (bool IsValid, string ErrorMessage) ValidateImagePayload(string ext, byte[] bytes)
    {
        if (bytes.Length < 4)
        {
            return (false, "Tệp ảnh quá nhỏ hoặc bị hỏng.");
        }

        if (ext == ".png")
        {
            bool isPng = bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
            return isPng ? (true, string.Empty) : (false, "Nội dung tệp không đúng chữ ký ảnh PNG hợp lệ.");
        }

        if (ext is ".jpg" or ".jpeg")
        {
            bool isJpg = bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            return isJpg ? (true, string.Empty) : (false, "Nội dung tệp không đúng chữ ký ảnh JPEG hợp lệ.");
        }

        if (ext == ".webp")
        {
            bool isWebp = bytes.Length >= 12
                && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
            return isWebp ? (true, string.Empty) : (false, "Nội dung tệp không đúng chữ ký ảnh WebP hợp lệ.");
        }

        if (ext == ".svg")
        {
            string text = Encoding.UTF8.GetString(bytes);
            if (!text.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Tệp SVG không hợp lệ (thiếu thẻ gốc <svg>).");
            }
            if (Regex.IsMatch(text, @"<\s*(script|iframe|object|embed|foreignObject)\b|javascript\s*:|\bon[a-z]+\s*=", RegexOptions.IgnoreCase))
            {
                return (false, "Tệp SVG chứa mã kịch bản không an toàn nên đã bị hệ thống từ chối.");
            }
            return (true, string.Empty);
        }

        return (false, "Định dạng ảnh không được hỗ trợ.");
    }

    private static void TryDeleteOldIconFile(string webRoot, string? oldUrl)
    {
        if (string.IsNullOrWhiteSpace(oldUrl) || !oldUrl.StartsWith("/uploads/icons/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        try
        {
            string fileName = Path.GetFileName(oldUrl);
            if (string.IsNullOrWhiteSpace(fileName)) return;
            string fullPath = Path.Combine(webRoot, "uploads", "icons", fileName);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // Bỏ qua lỗi xóa tệp cũ không ảnh hưởng luồng nghiệp vụ
        }
    }

    private static SystemIconItemDto ToDto(SystemIconSetting x) => new()
    {
        IconKey = x.IconKey,
        DisplayName = x.DisplayName,
        CategoryGroup = x.CategoryGroup,
        DefaultIconClass = x.DefaultIconClass,
        CustomImageUrl = x.CustomImageUrl,
        UseCustomImage = x.UseCustomImage,
        UpdatedAt = x.UpdatedAt
    };
}
