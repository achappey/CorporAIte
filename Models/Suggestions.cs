
using System.Text.Json.Serialization;

namespace CorporAIte.Models
{
    public class Suggestions
    {
        [JsonPropertyName("prompts")]
        public IEnumerable<Suggestion> Prompts { get; set; }

        public Suggestions()
        {
            Prompts = new List<Suggestion>();
        }

    }

    public class Suggestion
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; }

        public Suggestion()
        {
        }

    }
}
