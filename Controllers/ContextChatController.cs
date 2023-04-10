using CorporAIte.Models;
using Microsoft.AspNetCore.Mvc;

namespace CorporAIte.Controllers;

[ApiController]
[Route("[controller]")]
public class ContextChatController : ControllerBase
{

    private readonly ILogger<ContextChatController> _logger;
    private readonly CorporAIteService _corporAiteService;

    public ContextChatController(ILogger<ContextChatController> logger, CorporAIteService corporAiteService)
    {
        _logger = logger;
        _corporAiteService = corporAiteService;
    }

    [HttpPost(Name = "ContextChat")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ChatMessage))]
    public async Task<IActionResult> ContextChat([FromQuery] string siteUrl, 
    [FromQuery] string folderPath, 
    [FromQuery] string fileName,
    [FromBody] Chat chat)
    {
        try
        {
            if (string.IsNullOrEmpty(siteUrl))
            {
                return BadRequest("The site URL is required.");
            }

            if (string.IsNullOrEmpty(folderPath))
            {
                return BadRequest("The folder path is required.");
            }

            if (string.IsNullOrEmpty(fileName))
            {
                return BadRequest("The file name is required.");
            }

            var result = await this._corporAiteService.ChatWithDataAsync(siteUrl, folderPath, fileName, chat);

            return Ok(result);
        }
        catch (Exception ex)
        {
             this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
}
