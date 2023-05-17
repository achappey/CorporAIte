
namespace CorporAIte.Models
{
    public class Conversation
    {
        public int ItemId { get; set; }

        public int SystemPromptId { get; set; }

        public SystemPrompt SystemPrompt { get; set; }

        public List<Message> Messages { get; set; }

        public List<string> Sources { get; set; }

        public string Title { get; set; }

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
