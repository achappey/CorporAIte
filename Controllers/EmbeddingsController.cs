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
    public async Task<IActionResult> CreateEmbeddings(string siteUrl, string folderPath, string fileName)
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
            // Log the error here, or rethrow it if you want to let it propagate up the call stack

            return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while creating embeddings.");
        }
    }

}
