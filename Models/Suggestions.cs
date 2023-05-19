
using System.Text.Json.Serialization;

namespace CorporAIte.Models
{
    public class Suggestions
    {
        [JsonPropertyName("prompts")]
        public IEnumerable<string> Prompts { get; set; }

        public Suggestions()
        {
            Prompts = new List<string>();
        }

    }


}
