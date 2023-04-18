using AutoMapper;
using CorporAIte.Extensions;
using CorporAIte.Models;

public class CorporAIteService
{
    private readonly ILogger<CorporAIteService> _logger;

    private readonly SharePointService _sharePointService;

    private readonly SharePointAIService _sharePointAIService;

    private readonly AIService _openAIService;

    private readonly IMapper _mapper;

    private readonly List<string> supportedExtensions = new List<string> { ".aspx", ".docx", ".csv", ".pdf", ".pptx", ".txt" };

    public CorporAIteService(ILogger<CorporAIteService> logger,
    SharePointService sharePointService,
    SharePointAIService sharePointAIService,
    AIService openAIService,
    IMapper mapper)
    {
        _logger = logger;
        _sharePointService = sharePointService;
        _sharePointAIService = sharePointAIService;
        _openAIService = openAIService;
        _mapper = mapper;
    }

    public async Task<List<(byte[] ByteArray, DateTime LastModified)>> CalculateFolderEmbeddingsAsync(string siteUrl, string folderPath)
    {
        var supportedFiles = await _sharePointService.GetSupportedFilesInFolderAsync(siteUrl, folderPath, this.supportedExtensions);

        var tasks = supportedFiles.Select(async file =>
        {
            var lines = await GetLinesAsync(siteUrl, file.ServerRelativeUrl);
            return await this._sharePointAIService.CalculateAndUploadEmbeddingsAsync(folderPath, file.ServerRelativeUrl, lines);
        });

        var results = await Task.WhenAll(tasks);

        return results.SelectMany(x => x).ToList();
    }


    public async Task<List<(byte[] ByteArray,  DateTime LastModified)>> CalculateEmbeddingsAsync(string siteUrl, string folderPath, string fileName)
    {
        var lines = await GetLinesAsync(siteUrl, Path.Combine(folderPath, fileName));
        return await this._sharePointAIService.CalculateAndUploadEmbeddingsAsync(folderPath, fileName, lines);
    }

    private async Task<List<string>> GetLinesAsync(string siteUrl, string file)
    {
        string extension = Path.GetExtension(file).ToLowerInvariant();

        if (extension == ".aspx")
        {
            return await _sharePointService.GetSharePointPageTextAsync(siteUrl, file);
        }
        else
        {
            byte[] bytes = await _sharePointService.DownloadFileFromSharePointAsync(siteUrl, file);
            return ConvertFileContentToList(bytes, extension);
        }
    }

    private List<string> ConvertFileContentToList(byte[] bytes, string extension)
    {
        switch (extension)
        {
            case ".docx":
                return bytes.ConvertDocxToLines();
            case ".csv":
                return bytes.ConvertCsvToList();
            case ".pdf":
                return bytes.ConvertPdfToLines();
            case ".pptx":
                return bytes.ConvertPptxToLines();
            case ".txt":
                return bytes.ConvertTxtToList();
            default:
                throw new NotSupportedException($"Unsupported file extension: {extension}");
        }
    }

    private async Task<ChatMessage> ChatWithDataFolderAsync(List<string> folders, Chat chat)
    {
        var queryEmbedding = await _openAIService.CalculateEmbeddingAsync(chat.ChatHistory.Last(t => t.Role == "user").Content);

        // Process the folders concurrently
        var folderTasks = folders.Select(async folder =>
        {
            var siteUrl = this._sharePointService.ExtractSiteServerRelativeUrl(folder);

            // Get the supported files in the folder
            var files = await _sharePointService.GetSupportedFilesInFolderAsync(siteUrl, folder, this.supportedExtensions);

            // Process the files concurrently
            var fileTasks = files.Select(async file =>
            {
                var aiFiles = await this._sharePointAIService.GetAiFilesForFile(folder, Path.GetFileName( file.ServerRelativeUrl));

                var lines = await GetLinesAsync(siteUrl, file.ServerRelativeUrl);

                if (!aiFiles.Any() || aiFiles.First().LastModified < file.LastModified)
                {
                    aiFiles = await this._sharePointAIService.CalculateAndUploadEmbeddingsAsync(folder, file.ServerRelativeUrl, lines);
                }

                var scores = _openAIService.CompareEmbeddings(queryEmbedding, aiFiles.Select(a => a.ByteArray).ToList());

                return lines.Select((line, index) => new { Text = line, Score = scores.ElementAt(index) });
            });

            // Wait for all file tasks to complete and concatenate the results
            return (await Task.WhenAll(fileTasks)).SelectMany(r => r).ToList();
        });

        // Wait for all folder tasks to complete and concatenate the results
        var allResults = (await Task.WhenAll(folderTasks)).SelectMany(r => r as IEnumerable<dynamic>).ToList();

        var topResults = allResults
            .OrderByDescending(result => result.Score)
            .Take(200)
            .ToList();

        return await ChatWithBestContext(topResults, chat);
    }

    private async Task<ChatMessage> ChatWithBestContext(List<dynamic> topResults, Chat chat)
    {
        var contextQuery = string.Join(" ", topResults.Select(a => a.Text));

        while (!string.IsNullOrEmpty(contextQuery))
        {
            try
            {
                var chatResponse = await _openAIService.ChatWithContextAsync(chat.System + contextQuery,
                  chat.Temperature,
                  chat.ChatHistory.Select(a => _mapper.Map<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage>(a)));

                return _mapper.Map<ChatMessage>(chatResponse);
            }
            catch (FormatException ex)
            {
                var topResultsCount = topResults.Count();
                if (topResultsCount > 1)
                {
                    topResults = topResults.Take(topResultsCount / 2).ToList();
                    contextQuery = string.Join(" ", topResults.Select(a => a.Text));
                    await Task.Delay(500);
                }
                else
                {
                    throw new Exception("Failed to chat with context.");
                }
            }
        }

        throw new Exception("Failed to chat with context.");
    }

    public async Task<ChatMessage> ChatAsync(Chat chat)
    {
        if (chat.SourceFolders.Any())
        {
            return await ChatWithDataFolderAsync(chat.SourceFolders.Select(a => a.Path).ToList(), chat);
        }
        else
        {
            return await ChatWithFallback(chat);
        }
    }

    private async Task<ChatMessage> ChatWithFallback(Chat chat)
    {
        int initialMessageLength = chat.GetLastUserMessageLength();

        while (initialMessageLength > 0)
        {
            try
            {
                return await AttemptChatAsync(chat);
            }
            catch (FormatException)
            {
                initialMessageLength = chat.ShortenLastUserMessage();
            }
        }

        throw new InvalidOperationException("The last user message could not be shortened further without success.");
    }

    private async Task<ChatMessage> AttemptChatAsync(Chat chat)
    {
        // Prepare chat messages for the OpenAIService
        var openAiChatMessages = chat.ChatHistory
            .Select(message => _mapper.Map<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage>(message));

        // Chat with context using the OpenAIService
        var chatResponse = await _openAIService.ChatWithContextAsync(
            chat.System,
            chat.Temperature,
            openAiChatMessages);

        // Map the response to the ChatMessage model
        return _mapper.Map<ChatMessage>(chatResponse);
    }

}

