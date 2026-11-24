using BookShare.Web.Models.ViewModels;
using Microsoft.AspNetCore.Html;

namespace BookShare.Web.Services.Interfaces;

public interface IDatabaseBootstrapService
{
    Task EnsureInitializedAsync(CancellationToken ct = default);
}

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
