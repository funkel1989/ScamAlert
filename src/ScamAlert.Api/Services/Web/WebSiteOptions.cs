namespace ScamAlert.Api.Services.Web;

public sealed class WebSiteOptions
{
    public const string SectionName = "Web";

    public string PublicBaseUrl { get; set; } = "https://localhost:7091";
    public string InstallerDownloadUrl { get; set; } = "https://github.com/";

    /// <summary>Legal entity name shown in policies (update before production).</summary>
    public string LegalEntityName { get; set; } = "ScamAlert";

    /// <summary>Support and privacy contact email.</summary>
    public string SupportEmail { get; set; } = "support@scamalert.com";

    /// <summary>ISO date string for policy "last updated" (YYYY-MM-DD).</summary>
    public string LegalEffectiveDate { get; set; } = "2026-05-19";
}
