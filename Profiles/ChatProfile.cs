using AutoMapper;
using CorporAIte.Models;

namespace CorporAIte.Profiles;

public class ChatProfile : Profile
{
	public ChatProfile()
	{
		CreateMap<ChatMessage, OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage>();
		CreateMap<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage, ChatMessage>();

		CreateMap<Message, OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage>();
		CreateMap<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage, Message>();

		CreateMap<Microsoft.Graph.DriveItem, Folder>();

	}
}