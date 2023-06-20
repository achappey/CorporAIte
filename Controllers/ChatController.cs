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

    [HttpGet(Name = "Chat")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Message))]
    [Produces("application/json")]
    public async Task<IActionResult> Chat([FromQuery] string chatId)
    {
        try
        {
            var result = await _corporAiteService.TeamsChatAsync(chatId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

}
