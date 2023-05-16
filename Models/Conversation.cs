
namespace CorporAIte.Models
{
    public class Conversation
    {
        public int ItemId { get; set; }

        public int SystemPromptId { get; set; }

        public SystemPrompt SystemPrompt { get; set; }

        public List<Message> Messages { get; set; }

        //public List<SourceFolder>? SourceFolders { get; set; }

        public string SourceFolder { get; set; }

        public Conversation()
        {
            Messages = new List<Message>();
        }

        public Conversation(List<Message> chatHistory)
        {
            Messages = chatHistory ?? new List<Message>();
        }
    }

    
}
