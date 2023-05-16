using CorporAIte.Models;
using Microsoft.AspNetCore.Mvc;

namespace CorporAIte.Controllers;

[ApiController]
[Route("[controller]")]
public class OneDriveFoldersController : ControllerBase
{
    private readonly ILogger<OneDriveFoldersController> _logger;

    private readonly CorporAIteService _corporAiteService;

    public OneDriveFoldersController(ILogger<OneDriveFoldersController> logger, CorporAIteService corporAiteService)
    {
        _logger = logger;
        _corporAiteService = corporAiteService;
    }

    [HttpGet(Name = "GetOneDriveFolders")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Folders))]
    public async Task<IActionResult> GetOneDriveFolders([FromQuery] string userId)
    {

        try
        {
            var result = await _corporAiteService.GetOneDriveFolders(userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

}
