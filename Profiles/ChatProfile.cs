using AutoMapper;
using CorporAIte.Models;

namespace CorporAIte.Profiles;

public class ChatProfile : Profile
{
    public ChatProfile()
    {
        CreateMap<Message, OpenAI.ObjectModels.RequestModels.ChatMessage>();
        CreateMap<OpenAI.ObjectModels.RequestModels.ChatMessage, Message>();

        CreateMap<Microsoft.Graph.DriveItem, Folder>();

    }
}