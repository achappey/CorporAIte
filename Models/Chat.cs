namespace CorporAIte.Models;

public class Chat
{
    public string System { get; set; }

    public IEnumerable<ChatMessage> ChatHistory { get; set; }

	public Chat()
	{
		
		
	}
}