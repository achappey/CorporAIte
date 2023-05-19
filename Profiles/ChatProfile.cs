using AutoMapper;
using CorporAIte.Models;

namespace CorporAIte.Profiles;

public class ChatProfile : Profile
{
    public ChatProfile()
    {
        CreateMap<Message, OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage>()
           .AfterMap((src, dest) =>
        {
            if (src.Role == "user")
            {
                if (!string.IsNullOrEmpty(src.Format))
                {
                    dest.Content += $" Formateer als {{src.Format}}";
                }

                string attributes = $"{src.Emotional}{src.Authoritarian}{src.Concrete}{src.Convincing}{src.Friendly}";
                dest.Content += attributes;
            }
        });
		
        CreateMap<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage, Message>();

        CreateMap<Microsoft.Graph.DriveItem, Folder>();

    }
}