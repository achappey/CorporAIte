
namespace CorporAIte.Models
{
    public class Chat
    {
        public string System { get; set; }

        public float Temperature { get; set; }

        public List<ChatMessage> ChatHistory { get; set; }

    //    public string? SiteUrl { get; set; }

  //      public string? FolderUrl { get; set; }

//        public List<int>? ItemIds { get; set; }

        public Chat()
        {
            ChatHistory = new List<ChatMessage>();
        }

        public Chat(string system, List<ChatMessage> chatHistory)
        {
            System = system;
            ChatHistory = chatHistory ?? new List<ChatMessage>();
        }
    }
}
