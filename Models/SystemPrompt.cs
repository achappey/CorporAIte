
namespace CorporAIte.Models
{
    public class SystemPrompt
    {
        public string BaseUrl { get; set; } = "https://fakton.sharepoint.com";

        public int ItemId { get; set; }

        public string Prompt { get; set; }

        public float Temperature { get; set; }

        public bool ForceVectorGeneration { get; set; }

        public SystemPrompt()
        {
        }
    }

    
}
