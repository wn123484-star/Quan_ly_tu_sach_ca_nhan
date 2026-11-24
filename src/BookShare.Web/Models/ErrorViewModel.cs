namespace BookShare.Web.Models;

public class ErrorViewModel
{
    public int StatusCode { get; set; } = 500;
    public string Title { get; set; } = "Có sự cố xảy ra";
    public string Description { get; set; } = string.Empty;
    public string Guidance { get; set; } = string.Empty;
    public string? OriginalPath { get; set; }
    public string? SuggestedUrl { get; set; }
    public string? SuggestedLabel { get; set; }
    public bool IsAuthenticated { get; set; }
    public string? CurrentRole { get; set; }
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public string ShortRequestId
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RequestId))
            {
                return string.Empty;
            }
            var trimmed = RequestId.Trim();
            return trimmed.Length <= 16 ? trimmed : trimmed[..16];
        }
    }
}
