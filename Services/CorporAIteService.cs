
using System.Globalization;
using System.Text;
using AutoMapper;
using CorporAIte.Models;
using CsvHelper;
using CsvHelper.Configuration;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

public class CorporAIteService
{
    private readonly ILogger<CorporAIteService> _logger;

    private readonly SharePointService _sharePointService;

    private readonly AIService _openAIService;

    private readonly IMapper _mapper;

    public CorporAIteService(ILogger<CorporAIteService> logger,
    SharePointService sharePointService,
    AIService openAIService,
    IMapper mapper)
    {
        _logger = logger;
        _sharePointService = sharePointService;
        _openAIService = openAIService;
        _mapper = mapper;
    }

    public static List<string> ConvertDocxToText(byte[] docxBytes)
    {
        var result = new List<string>();

        using (var memoryStream = new MemoryStream(docxBytes))
        {
            using (var wordDocument = WordprocessingDocument.Open(memoryStream, false))
            {
                var body = wordDocument.MainDocumentPart.Document.Body;
                var paragraphs = body.Descendants<Paragraph>();


                foreach (var paragraph in paragraphs)
                {
                    var sb = new StringBuilder();
                    var runs = paragraph.Elements<Run>();

                    foreach (var run in runs)
                    {
                        var textElements = run.Elements<Text>();
                        var text = string.Join(string.Empty, textElements.Select(t => t.Text));
                        sb.Append(text);
                    }

                    if (!string.IsNullOrEmpty(sb.ToString()))
                    {
                        result.Add(sb.ToString());
                    }

                }

                return result;
            }
        }
    }

    public async Task CalculateEmbeddingsAsync(string siteUrl, string folderPath, string fileName)
    {
        byte[] bytes = await _sharePointService.DownloadFileFromSharePointAsync(siteUrl, Path.Combine(folderPath, fileName));

        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        List<string> lines = ConvertFileContentToList(bytes, extension);

        const int maxBatchSize = 2000;
        int batchSize = Math.Min(maxBatchSize, lines.Count);
        int suffix = 1;

        while (lines.Any())
        {
            try
            {
                List<string> currentBatch = lines.Take(batchSize).ToList();
                byte[] embeddings = await _openAIService.CalculateEmbeddingAsync(currentBatch);
                string fileNameWithSuffix = $"{Path.GetFileNameWithoutExtension(fileName)}-{suffix}.ai";
                await _sharePointService.UploadFileToSharePointAsync(embeddings, siteUrl, folderPath, fileNameWithSuffix);

                lines = lines.Skip(batchSize).ToList();
                suffix++;
                batchSize = Math.Min(maxBatchSize, lines.Count);
            }
            catch (Exception ex)
            {
                batchSize = Math.Max(1, batchSize / 2);
                if (batchSize == 1)
                {
                    // Log the exception and break the loop if it still fails with batchSize == 1
                    _logger.LogError(ex, "Failed to calculate embeddings with batchSize 1.");
                    break;
                }
            }
        }
    }

    private async Task<List<byte[]>> GetAiFilesForFile(string siteUrl, string folderPath, string file)
    {
        return await _sharePointService.GetFilesByExtensionFromFolder(siteUrl, folderPath, "ai", Path.GetFileNameWithoutExtension(file));
    }

    public async Task<ChatMessage> ChatWithDataListAsync(List<string> files, Chat chat)
    {
        // Calculate the embedding for the last user's message in the chat history
        var queryEmbedding = await _openAIService.CalculateEmbeddingAsync(chat.ChatHistory.Last(t => t.Role == "user").Content);

        var results = new List<dynamic>();

        foreach (var file in files)
        {
            // Get AI files associated with the current file

            var siteUrl = this._sharePointService.ExtractSiteServerRelativeUrl(file);
            var folderPath = this._sharePointService.ExtractServerRelativeFolderPath(file);
            var aiFiles = await GetAiFilesForFile(siteUrl, folderPath, file);

            // Download the current file content from SharePoint
            var bytes = await _sharePointService.DownloadFileFromSharePointAsync(siteUrl, file);

            // Convert the file content into a list of lines
            var lines = ConvertFileContentToList(bytes, Path.GetExtension(file).ToLowerInvariant());

            // Compare embeddings and get the scores
            var scores = _openAIService.CompareEmbeddings(queryEmbedding, aiFiles);

            // Create result items with text and score
            results.AddRange(lines
                .Select((line, index) => new { Text = line, Score = scores.ElementAt(index) }));
        }

        // Take the top 150 results
        var topResults = results
            .OrderByDescending(result => result.Score)
            .Take(200)
            .ToList();

        // Chat with the best context
        return await ChatWithBestContext(topResults, chat);
    }


    private List<string> ConvertFileContentToList(byte[] bytes, string extension)
    {
        switch (extension)
        {
            case ".docx":
                return ConvertDocxToText(bytes);
            case ".csv":
                return ConvertCsvToList(bytes);
            default:
                throw new NotSupportedException($"Unsupported file extension: {extension}");
        }
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
        // Check if there are any item IDs in the chat
        if (chat.SourceFiles.Any())
        {
            // Chat with data list using item IDs
            return await ChatWithDataListAsync(chat.SourceFiles.Select(a => a.Path).ToList(), chat);
        }
        else
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


    private static List<string> ConvertCsvToList(byte[] csvBytes)
    {
        var records = new List<string>();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = args => args.Header.ToLower(),
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            MissingFieldFound = null
        };

        using (var reader = new StreamReader(new MemoryStream(csvBytes)))
        using (var csv = new CsvReader(reader, config))
        {
            var recordsList = csv.GetRecords<dynamic>();

            foreach (var record in recordsList)
            {
                var recordDictionary = record as IDictionary<string, object>;
                var formattedRecord = new List<string>();

                foreach (var entry in recordDictionary)
                {
                    formattedRecord.Add($"{entry.Key}:{entry.Value.ToString().Trim()}");
                }

                records.Add(string.Join(";", formattedRecord));
            }
        }

        return records;
    }
}
