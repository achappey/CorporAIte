using System.Text.RegularExpressions;
using CorporAIte;
using CorporAIte.Models;
using Microsoft.SharePoint.Client;

public class SharePointAIService
{
    private readonly ILogger<SharePointAIService> _logger;
    private readonly SharePointService _sharePointService;
    private readonly AIService _openAIService;

    private readonly string _vectorSiteUrl;
    private readonly string _vectorFolderUrl;

    private readonly string _chatSiteUrl;
    private readonly string _baseSiteUrl;

    public SharePointAIService(ILogger<SharePointAIService> logger,
    AIService openAIService,
    SharePointService _sharePoint,
    AppConfig config)
    {
        this._sharePointService = _sharePoint;
        this._vectorSiteUrl = config.SharePoint.AIVectorSiteUrl;
        this._vectorFolderUrl = config.SharePoint.AIVectorFolderUrl;
        this._chatSiteUrl = config.SharePoint.AIChatSiteUrl;
        this._baseSiteUrl = config.SharePoint.AIBaseSiteUrl;
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

    public async Task<Conversation> GetListChat(int itemId)
    {
        var conversation = await this._sharePointService.GetListItemFromList(this._baseSiteUrl + this._chatSiteUrl, "Conversaties", itemId);
        var messages = await this._sharePointService.GetListItemsFromList(this._baseSiteUrl + this._chatSiteUrl, "Berichten", $@"
                <View>
                    <Query>
                        <Where>
                            <Eq>
                                <FieldRef Name='Conversatie' LookupId='TRUE' />
                                <Value Type='Lookup'>{conversation.Id}</Value>
                            </Eq>
                        </Where>
                        <OrderBy>
                            <FieldRef Name='Created' Ascending='TRUE' />
                        </OrderBy>
                    </Query>
                </View>");

        var lookupId = (conversation["Systeem_x0020_Prompt"] as FieldLookupValue).LookupId;

        var systemPrompt = await this._sharePointService.GetListItemFromList(this._baseSiteUrl + this._chatSiteUrl, "Systeem Prompts", lookupId);


        return new Conversation()
        {
            SystemPrompt = new SystemPrompt()
            {
                BaseUrl = (systemPrompt["Bron"] as FieldUrlValue).Url,
                Prompt = systemPrompt["Prompt"].ToString(),
                ForceVectorGeneration = systemPrompt["Altijdvectorsgenereren"] != null ? bool.Parse(systemPrompt["Altijdvectorsgenereren"].ToString()) : false,
                Temperature = float.Parse(systemPrompt["Temperatuur"].ToString())
            },
            SourceFolder = conversation["Map"]?.ToString(),
            Messages = messages.Select(a => new Message()
            {
                Content = a["Bericht"].ToString(),
                Role = a["Title"].ToString()
            }).ToList()
        };
    }

    public async Task<List<(byte[] ByteArray, DateTime LastModified)>> GetAiFilesForFile(string folderPath, string file)
    {
        return await _sharePointService.GetFilesByExtensionFromFolder(this._baseSiteUrl + this._vectorSiteUrl, this._vectorFolderUrl, ".ai", MakeValidSharePointFileName(folderPath + Path.GetFileName(file)));
    }


    public async Task<List<(byte[] ByteArray, DateTime LastModified)>> CalculateAndUploadEmbeddingsAsync(string folderPath, string fileName, List<string> lines)
    {
        const int maxBatchSize = 2000;
        int batchSize = Math.Min(maxBatchSize, lines.Count);
        int suffix = 1;
        List<(byte[] ByteArray, DateTime LastModified)> uploadedFiles = new List<(byte[] ByteArray, DateTime LastModified)>();

        while (lines.Any())
        {
            try
            {
                List<string> currentBatch = lines.Take(batchSize).ToList();
                byte[] embeddings = await _openAIService.CalculateEmbeddingAsync(currentBatch);
                string fileNameWithSuffix = MakeValidSharePointFileName($"{folderPath}{Path.GetFileName(fileName)}-{suffix}") + ".ai";
                await _sharePointService.UploadFileToSharePointAsync(embeddings, this._baseSiteUrl + this._vectorSiteUrl, this._vectorFolderUrl, fileNameWithSuffix);

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