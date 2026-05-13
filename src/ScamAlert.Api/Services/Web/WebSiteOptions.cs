namespace ScamAlert.Api.Services.Web;

public sealed class WebSiteOptions
{
    public const string SectionName = "Web";

    public string PublicBaseUrl { get; set; } = "https://localhost:7091";
    public string InstallerDownloadUrl { get; set; } = "https://github.com/";
}
