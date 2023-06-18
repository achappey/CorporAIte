using CorporAIte.Models;
using Microsoft.AspNetCore.Mvc;

namespace CorporAIte.Controllers;

[ApiController]
[Route("[controller]")]
public class TeamsChatController : ControllerBase
{
    private readonly ILogger<TeamsChatController> _logger;

    private readonly CorporAIteService _corporAiteService;

    public TeamsChatController(ILogger<TeamsChatController> logger, CorporAIteService corporAiteService)
    {
        _logger = logger;
        _corporAiteService = corporAiteService;
    }

    [HttpGet(Name = "TeamsChat")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Message))]
    [Produces("application/json")]
    public async Task<IActionResult> TeamsChat([FromQuery] string teamsId, [FromQuery] string channelId, [FromQuery] string messageId)
    {
        try
        {
            var result = await _corporAiteService.TeamsChatAsync(teamsId, channelId, messageId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

}
