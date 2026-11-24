namespace BookShare.Web.Models.ViewModels;

public class SystemIconItemDto
{
    public string IconKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CategoryGroup { get; set; } = string.Empty;
    public string DefaultIconClass { get; set; } = "bi-app";
    public string? CustomImageUrl { get; set; }
    public bool UseCustomImage { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ToggleIconModeDto
{
    public string IconKey { get; set; } = string.Empty;
    public bool UseCustomImage { get; set; }
}

public class ResetIconRequestDto
{
    public string IconKey { get; set; } = string.Empty;
}
