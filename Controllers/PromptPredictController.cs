using CorporAIte.Models;
using Microsoft.AspNetCore.Mvc;

namespace CorporAIte.Controllers;

[ApiController]
[Route("[controller]")]
public class PromptPredictController : ControllerBase
{
    private readonly ILogger<PromptPredictController> _logger;

    private readonly CorporAIteService _corporAiteService;

    public PromptPredictController(ILogger<PromptPredictController> logger, CorporAIteService corporAiteService)
    {
        _logger = logger;
        _corporAiteService = corporAiteService;
    }

    [HttpGet(Name = "PredictPrompt")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Suggestions))]
    [Produces("application/json")]
    public async Task<IActionResult> PredictPrompt([FromQuery] int chatId)
    {
        try
        {
            var result = await _corporAiteService.GetPromptPredictionAsync(chatId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

}
