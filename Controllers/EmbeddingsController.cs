using Microsoft.AspNetCore.Mvc;

namespace CorporAIte.Controllers;

[ApiController]
[Route("[controller]")]
public class EmbeddingsController : ControllerBase
{
    private readonly ILogger<EmbeddingsController> _logger;
    
    private readonly CorporAIteService _corporAiteService;

    public EmbeddingsController(ILogger<EmbeddingsController> logger, CorporAIteService corporAiteService)
    {
        _logger = logger;
        _corporAiteService = corporAiteService;
    }

    [HttpGet(Name = "CreateEmbeddings")]
    public async Task<IActionResult> CreateEmbeddings([FromQuery] string siteUrl, 
    [FromQuery] string folderPath, 
    [FromQuery] string fileName)
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

            await this._corporAiteService.CalculateEmbeddingsAsync(siteUrl, folderPath, fileName);

            return Ok("Embeddings created successfully.");
        }
        catch (Exception ex)
        {
             this._logger.LogError(ex, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

}
