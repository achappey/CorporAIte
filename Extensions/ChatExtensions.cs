using CorporAIte.Models;

namespace CorporAIte.Extensions
{
    public static class ChatExtensions
    {
       
    public static int GetLastUserMessageLength(this Chat chat)
    {
        return chat.ChatHistory.Count > 0 && chat.ChatHistory.Last().Role == "user" ? chat.ChatHistory.Last().Content.Length : 0;
    }

    public static int ShortenLastUserMessage(this Chat chat)
    {
        if (chat.ChatHistory.Count > 0 && chat.ChatHistory.Last().Role == "user" && chat.ChatHistory.Last().Content.Length > 1)
        {
            chat.ChatHistory.Last().Content = chat.ChatHistory.Last().Content.Substring(0, chat.ChatHistory.Last().Content.Length / 2);
            return chat.ChatHistory.Last().Content.Length;
        }
        else
        {
            throw new InvalidOperationException("The last user message is empty or could not be shortened further.");
        }
    }

    }
}