
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

    public async Task<ChatMessage> ChatWithDataAsync(string siteUrl, string folderPath, string fileName, Chat chat)
    {
        var files = await _sharePointService.GetFilesByExtensionFromFolder(siteUrl, folderPath, "ai", Path.GetFileNameWithoutExtension(fileName));
        var queryEmbedding = await _openAIService.CalculateEmbeddingAsync(chat.ChatHistory.Where(t => t.Role == "user").Last().Content);

        var bytes = await _sharePointService.DownloadFileFromSharePointAsync(siteUrl, folderPath + "/" + fileName);
        var lines = ConvertFileContentToList(bytes, Path.GetExtension(fileName).ToLowerInvariant());

        var vectors = _openAIService.CompareEmbeddings(queryEmbedding, files);

        var topResults = lines
            .Select((l, i) => new { Text = l, Score = vectors.ElementAt(i) })
            .OrderByDescending(r => r.Score)
            .Take(200)
            .ToList();

        return await ChatWithBestContext(topResults.Select(r => (dynamic)r).ToList(), chat);
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
        var chatResponse = await _openAIService.ChatWithContextAsync(chat.System,
                chat.Temperature,
                chat.ChatHistory.Select(a => this._mapper.Map<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage>(a)));

        return this._mapper.Map<ChatMessage>(chatResponse);
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
