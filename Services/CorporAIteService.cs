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

    private readonly List<string> supportedExtensions = new List<string> { ".aspx",
    ".docx", ".csv", ".pdf", ".pptx", ".txt", ".url" };

    private readonly HttpClient _httpClient;

    public CorporAIteService(ILogger<CorporAIteService> logger,
    SharePointService sharePointService,
    SharePointAIService sharePointAIService,
    AIService openAIService,
    HttpClient httpClient,
    IMapper mapper)
    {
        _logger = logger;
        _sharePointService = sharePointService;
        _sharePointAIService = sharePointAIService;
        _openAIService = openAIService;
        _mapper = mapper;
        _httpClient = httpClient;
    }

    public async Task<List<(byte[] ByteArray, DateTime LastModified)>> CalculateFolderEmbeddingsAsync(string siteUrl, string folderPath)
    {
        var supportedFiles = await _sharePointService.GetSupportedFilesInFolderAsync(siteUrl, folderPath, this.supportedExtensions);

        var tasks = supportedFiles.Select(async file =>
        {
            var lines = await GetLinesAsync(siteUrl, file.ServerRelativeUrl);

            if (lines == null || !lines.Any())
            {
                return Enumerable.Empty<(byte[] ByteArray, DateTime LastModified)>();
            }

            return await this._sharePointAIService.CalculateAndUploadEmbeddingsAsync(folderPath, file.ServerRelativeUrl, lines);
        });

        var results = await Task.WhenAll(tasks);

        return results.SelectMany(x => x).ToList();
    }



    public async Task<List<(byte[] ByteArray, DateTime LastModified)>> CalculateEmbeddingsAsync(string siteUrl, string folderPath, string fileName)
    {
        var lines = await GetLinesAsync(siteUrl, Path.Combine(folderPath, fileName));
        return await this._sharePointAIService.CalculateAndUploadEmbeddingsAsync(folderPath, fileName, lines);
    }

    private async Task<List<string>> GetLinesAsync(string siteUrl, string file)
    {
        if (string.IsNullOrEmpty(siteUrl) || string.IsNullOrEmpty(file))
        {
            throw new ArgumentNullException("SiteUrl or file cannot be null or empty.");
        }

        string extension = Path.GetExtension(file).ToLowerInvariant();

        switch (extension)
        {
            case ".aspx":
                return await _sharePointService.GetSharePointPageTextAsync(siteUrl, file);

            case ".url":
                var url = await _sharePointService.ReadUrlFromFileAsync(siteUrl, file);
                return await this._httpClient.ConvertPageToList(url);

            default:
                byte[] bytes = await _sharePointService.DownloadFileFromSharePointAsync(siteUrl, file);
                return bytes.ConvertFileContentToList(extension);
        }
    }

    private async Task<ChatMessage> ChatWithDataFolderAsync(List<string> inputPaths, Chat chat)
    {
        var queryEmbedding = await _openAIService.CalculateEmbeddingAsync(chat.ChatHistory.Last(t => t.Role == "user").Content).ConfigureAwait(false);

        // Separate input paths into files and folders
        var (folders, files) = inputPaths.SeparateInputPaths();

        // Process the folders concurrently
        var folderResults = await ProcessFoldersAsync(folders, queryEmbedding).ConfigureAwait(false);

        // Process the single files concurrently
        var fileResults = await ProcessSingleFilesAsync(files, queryEmbedding).ConfigureAwait(false);

        var allResults = folderResults.Concat(fileResults).ToList();

        var topResults = allResults
            .OrderByDescending(result => result.Score)
            .Take(200)
            .ToList();

        return await ChatWithBestContext(topResults, chat).ConfigureAwait(false);
    }

    
    private async Task<List<dynamic>> ProcessSingleFilesAsync(List<string> fileUrls, byte[] queryEmbedding)
    {
        var fileTasks = fileUrls.Select(async fileUrl =>
        {
            // Extract folder and siteUrl from the file URL
            var folder = fileUrl.GetParentFolderFromServerRelativeUrl();
            var siteUrl = this._sharePointService.ExtractSiteServerRelativeUrl(folder);

            var fileInfo = await _sharePointService.GetFileInfoAsync(siteUrl, fileUrl).ConfigureAwait(false);
            if (fileInfo == null)
            {
                return Enumerable.Empty<dynamic>();
            }

            var (serverRelativeUrl, lastModified) = fileInfo.Value;

            return await ProcessFilesAsync(folder, siteUrl, new List<(string ServerRelativeUrl, DateTime LastModified)> { (serverRelativeUrl, lastModified) }, queryEmbedding).ConfigureAwait(false);
        });

        // Wait for all file tasks to complete and concatenate the results
        return (await Task.WhenAll(fileTasks)).SelectMany(r => r as IEnumerable<dynamic>).ToList();
    }

    private async Task<List<dynamic>> ProcessFoldersAsync(List<string> folders, byte[] queryEmbedding)
    {
        var folderTasks = folders.Select(async folder =>
        {
            var siteUrl = this._sharePointService.ExtractSiteServerRelativeUrl(folder);

            // Get the supported files in the folder
            var files = await _sharePointService.GetSupportedFilesInFolderAsync(siteUrl, folder, this.supportedExtensions).ConfigureAwait(false);

            // Process the files concurrently
            return await ProcessFilesAsync(folder, siteUrl, files, queryEmbedding).ConfigureAwait(false);
        });

        // Wait for all folder tasks to complete and concatenate the results
        return (await Task.WhenAll(folderTasks)).SelectMany(r => r as IEnumerable<dynamic>).ToList();
    }

    private async Task<List<dynamic>> ProcessFilesAsync(string folder, string siteUrl, List<(string ServerRelativeUrl, DateTime LastModified)> files, byte[] queryEmbedding)
    {
        var fileTasks = files.Select(async file =>
        {
            var aiFiles = await this._sharePointAIService.GetAiFilesForFile(folder, Path.GetFileName(file.ServerRelativeUrl)).ConfigureAwait(false);

            var lines = await GetLinesAsync(siteUrl, file.ServerRelativeUrl).ConfigureAwait(false);

            if (lines == null || !lines.Any())
            {
                return Enumerable.Empty<dynamic>();
            }

            if (aiFiles == null || !aiFiles.Any() || aiFiles.First().LastModified < file.LastModified)
            {
                aiFiles = await this._sharePointAIService.CalculateAndUploadEmbeddingsAsync(folder, file.ServerRelativeUrl, lines).ConfigureAwait(false);
            }

            if (aiFiles == null || !aiFiles.Any())
            {
                return Enumerable.Empty<dynamic>();
            }

            var scores = _openAIService.CompareEmbeddings(queryEmbedding, aiFiles.Select(a => a.ByteArray).ToList());

            return lines.Select((line, index) => new
            {
                Path = file.ServerRelativeUrl,
                Text = line,
                Score = scores.ElementAt(index)
            });
        });

        // Wait for all file tasks to complete and concatenate the results
        return (await Task.WhenAll(fileTasks)).SelectMany(r => r as IEnumerable<dynamic>).ToList();
    }

    private async Task<ChatMessage> ChatWithBestContext(List<dynamic> topResults, Chat chat)
    {
        var uniquePaths = string.Join(", ", topResults.Select(a => this._sharePointService.BaseUrl + a.Path).Distinct());
        var contextQuery = uniquePaths + " " + string.Join(" ", topResults.Select(a => a.Text));

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
                    uniquePaths = string.Join(", ", topResults.Select(a => a.Path).Distinct());
                    contextQuery = uniquePaths + " " + string.Join(" ", topResults.Select(a => a.Text));
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

