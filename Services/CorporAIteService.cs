
using System.Globalization;
using AutoMapper;
using CorporAIte.Models;
using CsvHelper;
using CsvHelper.Configuration;

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

    public async Task CalculateEmbeddingsAsync(string siteUrl, string folderPath, string fileName)
    {
        byte[] bytes = await _sharePointService.DownloadFileFromSharePointAsync(siteUrl, Path.Combine(folderPath, fileName));
        var lines = ConvertCsvToList(bytes);

        const int maxBatchSize = 2000;
        int batchSize = lines.Count;
        int suffix = 1;

        while (lines.Any())
        {
            try
            {
                var currentBatch = lines.Take(batchSize).ToList();
                var embeddings = await _openAIService.CalculateEmbeddingAsync(currentBatch);
                var fileNameWithSuffix = $"{Path.GetFileNameWithoutExtension(fileName)}-{suffix}.ai";
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
                    this._logger.LogError(ex, "Failed to calculate embeddings with batchSize 1.");
                    break;
                }
            }
        }
    }

    public async Task<ChatMessage> ChatWithDataAsync(string siteUrl, string folderPath, string fileName, Chat chat)
    {
        var files = await this._sharePointService.GetFilesByExtensionFromFolder(siteUrl, folderPath, ".ai", Path.GetFileNameWithoutExtension(fileName));

        var queryEmbedding = await this._openAIService.CalculateEmbeddingAsync(chat.ChatHistory.Where(t => t.Role == "user").Last().Content);

        var bytes = await this._sharePointService.DownloadFileFromSharePointAsync(siteUrl, folderPath + "/" + fileName);
        var lines = ConvertCsvToList(bytes);
        var vectors = _openAIService.CompareEmbeddings(queryEmbedding, files);

        var topResults = lines
                    .Select((l, i) => new { Text = l, Score = vectors.ElementAt(i) })
                    .OrderByDescending(r => r.Score)
                    .Take(100)
                    .ToList();

        var contextquery = string.Join(" ", topResults.Select(a => a.Text));

        while (!string.IsNullOrEmpty(contextquery))
        {
            try
            {
                var chatResponse = await _openAIService.ChatWithContextAsync(chat.System + contextquery,
                  chat.ChatHistory.Select(a => this._mapper.Map<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage>(a)));

                return this._mapper.Map<ChatMessage>(chatResponse);
            }
            catch (FormatException ex)
            {
                var topResultsCount = topResults.Count();
                if (topResultsCount > 1)
                {
                    topResults = topResults.Take(topResultsCount / 2).ToList();
                    contextquery = string.Join(" ", topResults.Select(a => a.Text));
                    Thread.Sleep(500);
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
