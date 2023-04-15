

namespace CorporAIte;

public class AppConfig
{
    public SharePoint SharePoint { get; set; } = null!;    
    public string OpenAI { get; set; } = null!;
}

public class SharePoint
{
    public string ClientId { get; set; } = null!;
    public string ClientSecret { get; set; } = null!;
    public string TenantName { get; set; } = null!;
    public string AISiteUrl { get; set; } = null!;
    public string AIFolderUrl { get; set; } = null!;

}