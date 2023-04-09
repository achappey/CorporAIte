namespace CorporAIte.Models
{
    /// <summary>
    /// Represents a chat message with role and content.
    /// </summary>
    public class ChatMessage
    {
        /// <summary>
        /// Gets or sets the role of the sender of the chat message.
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Gets or sets the content of the chat message.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChatMessage"/> class.
        /// </summary>
        public ChatMessage()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChatMessage"/> class with the specified role and content.
        /// </summary>
        /// <param name="role">The role of the sender of the chat message.</param>
        /// <param name="content">The content of the chat message.</param>
        public ChatMessage(string role, string content)
        {
            Role = role ?? throw new ArgumentNullException(nameof(role));
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }
    }
}
