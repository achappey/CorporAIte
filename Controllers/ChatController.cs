using Microsoft.AspNetCore.Mvc;

namespace CorporAIte.Controllers;

[ApiController]
[Route("[controller]")]
public class ChatController : ControllerBase
{

    private readonly ILogger<ChatController> _logger;
    private readonly CorporAIteService _corporAiteService;

    public ChatController(ILogger<ChatController> logger, CorporAIteService corporAiteService)
    {
        _logger = logger;
        _corporAiteService = corporAiteService;
    }

    [HttpPost(Name = "Chat")]
    public async Task<IActionResult> Chat(string contextQuery = "", [FromBody] OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage[] messages = null)
    {
        try
        {
            if (messages != null && messages.Length > 0)
            {
                // If messages are included in the request body, use the new chat method that takes an array of messages
                var result = await this._corporAiteService.Chat(contextQuery, messages.ToList());
                return Ok(result);
            }
            else
            {
                return BadRequest("Messages are required.");
            }

        }
        catch (Exception ex)
        {
            // Log the error here, or rethrow it if you want to let it propagate up the call stack
            return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while processing the chat request.");
        }
    }


}
