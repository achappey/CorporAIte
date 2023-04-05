
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

public class CorporAIteService
{

    private readonly SharePointService _sharePointService;

    private readonly AIService _openAIService;

    public CorporAIteService(SharePointService sharePointService,
    AIService openAIService)
    {
        _sharePointService = sharePointService;
        _openAIService = openAIService;
    }

    public async Task CalculateEmbeddings(string siteUrl, string folderPath, string fileName)
    {
        var bytes = await this._sharePointService.DownloadFileFromSharePoint(siteUrl, folderPath + "/" + fileName);
        var lines = ConvertCsvToList(bytes);

        const int maxBatchSize = 2000;
        int batchSize = lines.Count;
        int suffix = 1;

        while (batchSize > 0)
        {
            try
            {
                var embeddings = await this._openAIService.CalculateEmbeddings(lines.Take(batchSize).ToList());
                var fileNameWithSuffix = $"{Path.GetFileNameWithoutExtension(fileName)}-{suffix}.ai";
                await this._sharePointService.UploadFileToSharePoint(embeddings, siteUrl, folderPath, fileNameWithSuffix);

                lines = lines.Skip(batchSize).ToList();
                suffix++;
                batchSize = Math.Min(maxBatchSize, lines.Count);
            }
            catch (Exception ex)
            {
                batchSize = Math.Max(1, batchSize / 2);
            }
        }
    }

    public async Task<string> ChatWithData(string siteUrl, string folderPath, string fileName, List<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage> messages, string contextDescription = "")
    {
        var files = await this._sharePointService.GetFilesByExtensionFromFolder(siteUrl, folderPath, ".ai", Path.GetFileNameWithoutExtension(fileName));

        var queryEmbedding = await this._openAIService.CalculateEmbedding(messages.Where(t => t.Role == "user").Last().Content);

        var bytes = await this._sharePointService.DownloadFileFromSharePoint(siteUrl, folderPath + "/" + fileName);
        var lines = ConvertCsvToList(bytes);
        var vectors = await _openAIService.CompareEmbeddings(queryEmbedding, files);

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
                var chatResponse = await _openAIService.ChatWithContext(contextDescription + contextquery,
                  messages);

                return chatResponse;
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

    public async Task<string> Chat(string contextDescription, List<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage> messages)
    {
        var chatResponse = await _openAIService.ChatWithContext(contextDescription,
                 messages);

        return chatResponse;
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
