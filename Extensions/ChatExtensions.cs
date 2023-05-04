using CorporAIte.Models;

namespace CorporAIte.Extensions
{
    public static class ChatExtensions
    {


        public static void ShortenChatHistory(this Chat chat)
        {
            int startIndex = chat.ChatHistory[0].Role == "system" ? 1 : 0;

            if (chat.ChatHistory.Count > startIndex + 1)
            {
                chat.ChatHistory.RemoveAt(startIndex); // Remove the oldest message in the chat history after the system prompt

                if (chat.ChatHistory.Count > startIndex + 1 && chat.ChatHistory[startIndex].Role == "assistant")
                {
                    chat.ChatHistory.RemoveAt(startIndex); // Remove the oldest user message if the first message after the system prompt is from the assistant
                }
            }
            else
            {
                throw new InvalidOperationException("The chat history is empty or could not be shortened further.");
            }
        }


    }
}