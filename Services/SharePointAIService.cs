using System.Text.RegularExpressions;
using CorporAIte;

public class SharePointAIService
{
    private readonly ILogger<SharePointAIService> _logger;
    private readonly SharePointService _sharePointService;
    private readonly AIService _openAIService;

    private readonly string _siteUrl;
    private readonly string _folderUrl;

    public SharePointAIService(ILogger<SharePointAIService> logger,
    AIService openAIService,
    SharePointService _sharePoint,
    AppConfig config)
    {
        this._sharePointService = _sharePoint;
        this._siteUrl = config.SharePoint.AISiteUrl;
        this._folderUrl = config.SharePoint.AIFolderUrl;
        this._openAIService = openAIService;
        this._logger = logger;
    }

    private string MakeValidSharePointFileName(string fileName)
    {
        string invalidChars = @"[~#%&*\{\}:<>\?\/\+\|\""\.]";
        string invalidStartingChars = @"(^\.|\.$)";

        // Replace any invalid characters with an underscore.
        string validFileName = Regex.Replace(fileName, invalidChars, "_");

        // Remove any invalid starting characters (e.g. a period).
        validFileName = Regex.Replace(validFileName, invalidStartingChars, "");

        // Truncate the filename to 128 characters.
        if (validFileName.Length > 128)
        {
            validFileName = validFileName.Substring(0, 128);
        }

        return validFileName;
    }


    public async Task<List<(byte[] ByteArray,  DateTime LastModified)>> GetAiFilesForFile(string folderPath, string file)
    {
        return await _sharePointService.GetFilesByExtensionFromFolder(this._siteUrl, this._folderUrl, ".ai", MakeValidSharePointFileName(folderPath + Path.GetFileNameWithoutExtension(file)));
    }


    public async Task<List<(byte[] ByteArray,  DateTime LastModified)>> CalculateAndUploadEmbeddingsAsync(string folderPath, string fileName, List<string> lines)
    {
        const int maxBatchSize = 2000;
        int batchSize = Math.Min(maxBatchSize, lines.Count);
        int suffix = 1;
        List<(byte[] ByteArray,  DateTime LastModified)> uploadedFiles = new List<(byte[] ByteArray,  DateTime LastModified)>();

        while (lines.Any())
        {
            try
            {
                List<string> currentBatch = lines.Take(batchSize).ToList();
                byte[] embeddings = await _openAIService.CalculateEmbeddingAsync(currentBatch);
                string fileNameWithSuffix = MakeValidSharePointFileName($"{folderPath}{Path.GetFileNameWithoutExtension(fileName)}-{suffix}") + ".ai";
                await _sharePointService.UploadFileToSharePointAsync(embeddings, this._siteUrl, this._folderUrl, fileNameWithSuffix);

                uploadedFiles.Add((embeddings, DateTime.Now));
                lines = lines.Skip(batchSize).ToList();
                suffix++;
                batchSize = Math.Min(maxBatchSize, lines.Count);
            }
            catch (Exception ex)
            {
                batchSize = Math.Max(1, batchSize / 2);
                if (batchSize == 1)
                {
                    _logger.LogError(ex, "Failed to calculate embeddings with batchSize 1.");
                    break;
                }
            }
        }

        return uploadedFiles;
    }

}