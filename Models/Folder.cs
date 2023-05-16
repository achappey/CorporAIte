namespace CorporAIte.Models
{

    public class Folder
    {

        public string Name { get; set; }

        public string WebUrl { get; set; }

        public Folder()
        {
        }


    }

    public class Folders
    {

        public IEnumerable<Folder> Items { get; set; }

        public Folders()
        {
        }


    }
}
