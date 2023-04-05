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
    public async Task<IActionResult> ContextChat(string siteUrl, string folderPath, string fileName, 
    [FromBody] OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage[] messages, string contextQuery = "")
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

            var result = await this._corporAiteService.ChatWithData(siteUrl, folderPath, fileName, messages.ToList(), contextQuery);

            return Ok(result);
        }
        catch (Exception ex)
        {
            // Log the error here, or rethrow it if you want to let it propagate up the call stack

            return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while creating embeddings.");
        }
    }
}
