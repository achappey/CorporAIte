using AutoMapper;
using CorporAIte.Models;

namespace CorporAIte.Profiles;

public class ChatProfile : Profile
{
	public ChatProfile()
	{
		CreateMap<ChatMessage, OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage>();
	}
}