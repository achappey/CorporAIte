using CorporAIte.Models;
using Microsoft.AspNetCore.Mvc;

namespace CorporAIte.Controllers;

[ApiController]
[Route("[controller]")]
public class ListChatController : ControllerBase
{
    private readonly ILogger<ListChatController> _logger;

    private readonly CorporAIteService _corporAiteService;

    public ListChatController(ILogger<ListChatController> logger, CorporAIteService corporAiteService)
    {
        _logger = logger;
        _corporAiteService = corporAiteService;
    }

    [HttpPost(Name = "ListChat")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Message))]
    public async Task<IActionResult> ListChat([FromQuery] int chatId)
    {
        try
        {
            var result = await _corporAiteService.ListChatAsync(chatId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

}
