using Microsoft.AspNetCore.Mvc;

namespace CorporAIte.Controllers;

[ApiController]
[Route("[controller]")]
public class FolderEmbeddingsController : ControllerBase
{
    private readonly ILogger<FolderEmbeddingsController> _logger;
    
    private readonly CorporAIteService _corporAiteService;

    public FolderEmbeddingsController(ILogger<FolderEmbeddingsController> logger, CorporAIteService corporAiteService)
    {
        _logger = logger;
        _corporAiteService = corporAiteService;
    }

    [HttpGet(Name = "CreateFolderEmbeddings")]
    public async Task<IActionResult> CreateFolderEmbeddings([FromQuery] string siteUrl, 
    [FromQuery] string folderPath)
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

            await this._corporAiteService.CalculateFolderEmbeddingsAsync(siteUrl, folderPath);

            return Ok("Embeddings created successfully.");
        }
        catch (Exception ex)
        {
             this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

}
