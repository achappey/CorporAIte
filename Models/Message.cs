
namespace CorporAIte.Models
{
    /// <summary>
    /// Represents a chat message with role and content.
    /// </summary>
    public class Message
    {
        public int ItemId { get; set; }

        public int ConversationId { get; set; }

        /// <summary>
        /// Gets or sets the role of the sender of the chat message.
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Gets or sets the content of the chat message.
        /// </summary>
        public string? Content { get; set; }

        public string Name { get; set; }

        public FunctionCall FunctionCall { get; set; }

        public string Format { get; set; }

        public int Emotional { get; set; }

        public int Authoritarian { get; set; }

        public int Concrete { get; set; }

        public int Convincing { get; set; }

        public int Friendly { get; set; }

        public DateTimeOffset? Created { get; set; }

    }

     public class FunctionCall
    {
        public string Name { get; set; } = null!;

        public string Arguments { get; set; } = null!;

    }
}
