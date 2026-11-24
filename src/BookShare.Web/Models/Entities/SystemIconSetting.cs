using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class SystemIconSetting
{
    public string IconKey { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string CategoryGroup { get; set; } = null!;

    public string DefaultIconClass { get; set; } = null!;

    public string? CustomImageUrl { get; set; }

    public byte[]? CustomImageData { get; set; }

    public string? CustomImageContentType { get; set; }

    public bool UseCustomImage { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? UpdatedByUserId { get; set; }
}
