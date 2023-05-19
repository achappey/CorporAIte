using CorporAIte.Models;
using Microsoft.AspNetCore.Mvc;

namespace CorporAIte.Controllers;

[ApiController]
[Route("[controller]")]
public class ChatNameController : ControllerBase
{
    private readonly ILogger<ChatNameController> _logger;

    private readonly CorporAIteService _corporAiteService;

    public ChatNameController(ILogger<ChatNameController> logger, CorporAIteService corporAiteService)
    {
        _logger = logger;
        _corporAiteService = corporAiteService;
    }

    [HttpGet(Name = "ChatName")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [Produces("application/json")]
    public async Task<IActionResult> ChatName([FromQuery] int chatId)
    {
        try
        {
            var result = await _corporAiteService.GetChatNameAsync(chatId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

}
