namespace CorporAIte.Models
{

    public class Tag
    {

        public string Name { get; set; }

        public string SystemPrompt { get; set; }
        
        public int ItemId { get; set; }

        public float Temperature { get; set; }

        public string Model { get; set; }


        public Tag()
        {
        }


    }
}