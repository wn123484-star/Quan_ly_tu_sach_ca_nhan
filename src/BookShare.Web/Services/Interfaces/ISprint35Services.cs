using BookShare.Web.Models.ViewModels;
using Microsoft.AspNetCore.Html;

namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [CORE-DB] Giao diện dịch vụ khởi tạo & đồng bộ cấu trúc CSDL mở rộng, vai trò gốc và dữ liệu nền khi ứng dụng khởi động.
/// </summary>
/// <remarks>Triển khai bởi: DatabaseBootstrapService.</remarks>
public interface IDatabaseBootstrapService
{
    Task EnsureInitializedAsync(CancellationToken ct = default);
}

/// <summary>
/// [CORE-ICONS] Giao diện dịch vụ quản lý 18 biểu tượng tùy biến của hệ thống (Logo, AI FAB, các khu vực chức năng).
/// </summary>
/// <remarks>Triển khai bởi: SystemIconService.</remarks>
public interface ISystemIconService
{
    Task<List<SystemIconItemDto>> GetAllIconsAsync();
    IHtmlContent RenderIconHtml(string iconKey, string fallbackClass, string extraCss = "");
    string GetImageUrl(string iconKey, string fallbackUrl);
    Task<(bool Success, string Message, SystemIconItemDto? Item)> UploadCustomIconAsync(
        string adminUserId,
        string iconKey,
        IFormFile file);
    Task<(bool Success, string Message, SystemIconItemDto? Item)> ToggleIconModeAsync(
        string adminUserId,
        string iconKey,
        bool useCustomImage);
    Task<(bool Success, string Message)> ResetIconToDefaultAsync(
        string adminUserId,
        string iconKey);
    Task<(byte[]? Bytes, string ContentType)> GetCustomIconBinaryAsync(string iconKey);
}
