
namespace CorporAIte.Models
{
    public class SystemPrompt
    {
        public int ItemId { get; set; }

        public string Prompt { get; set; }

        public float Temperature { get; set; }

        public bool ForceVectorGeneration { get; set; }

        public SystemPrompt()
        {
        }
    }

    
}
