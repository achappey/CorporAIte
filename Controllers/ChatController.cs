using CorporAIte.Models;
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
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ChatMessage))]
    public async Task<IActionResult> Chat([FromBody] Chat chat)
    {
        if (chat == null || !chat.ChatHistory.Any())
        {
            return BadRequest("Messages are required.");
        }

        try
        {
            var result = await _corporAiteService.ChatAsync(chat);
            return Ok(result);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }


}
