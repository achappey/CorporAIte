using System.Text.RegularExpressions;
using CorporAIte;
using CorporAIte.Extensions;
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
    private readonly ICacheService _cacheService;
    public SharePointAIService(ILogger<SharePointAIService> logger,
    AIService openAIService,
    SharePointService _sharePoint,
    AppConfig config, ICacheService cacheService)
    {
        this._sharePointService = _sharePoint;
        this._vectorSiteUrl = config.SharePoint.AIVectorSiteUrl;
        this._vectorFolderUrl = config.SharePoint.AIVectorFolderUrl;
        this._chatSiteUrl = config.SharePoint.AIChatSiteUrl;
        this._baseSiteUrl = config.SharePoint.AIBaseSiteUrl;
        this._openAIService = openAIService;
        this._logger = logger;
        this._cacheService = cacheService;
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
        using (var context = _sharePointService.GetContext(_baseSiteUrl + _chatSiteUrl))
        {
            var conversation = await context.GetListItemFromList(_baseSiteUrl + _chatSiteUrl, "Conversaties", itemId);
            var attachments = await context.GetAttachmentsFromListItem(_baseSiteUrl + _chatSiteUrl, "Conversaties", itemId);
            var sources = attachments.Select(a => _baseSiteUrl + a.ServerRelativeUrl).ToList();

            if (conversation?[FieldNames.Map] != null)
            {
                sources.Add(conversation[FieldNames.Map].ToString());
            }

            var messages = await context.GetListItemsFromList(_baseSiteUrl + _chatSiteUrl, "Berichten", $@"
            <View>
                <Query>
                    <Where>
                        <Eq>
                            <FieldRef Name='{FieldNames.Conversatie}' LookupId='TRUE' />
                            <Value Type='Lookup'>{conversation?.Id}</Value>
                        </Eq>
                    </Where>
                    <OrderBy>
                        <FieldRef Name='{FieldNames.Created}' Ascending='TRUE' />
                    </OrderBy>
                </Query>
            </View>");

            var lookupId = (conversation?[FieldNames.SysteemPrompt] as FieldLookupValue)?.LookupId;
            var systemPrompt = await context.GetListItemFromList(_baseSiteUrl + _chatSiteUrl, "Systeem Prompts", lookupId.GetValueOrDefault());

            float.TryParse(systemPrompt?[FieldNames.Temperatuur]?.ToString(), out var temperature);
            bool.TryParse(systemPrompt?[FieldNames.Altijdvectorsgenereren]?.ToString(), out var forceVectorGeneration);

            return new Conversation()
            {
                SystemPrompt = new SystemPrompt()
                {
                    Prompt = systemPrompt?[FieldNames.Prompt]?.ToString(),
                    ConversationNamePrompt = systemPrompt?[FieldNames.Naamconversatie]?.ToString(),
                    PredictionPrompt = systemPrompt?[FieldNames.Suggestievraag]?.ToString(),
                    ForceVectorGeneration = forceVectorGeneration,
                    Temperature = temperature
                },
                Sources = sources,
                Messages = messages?.Select(a => new Message()
                {
                    Content = a[FieldNames.Bericht]?.ToString(),
                    Role = a[FieldNames.Title]?.ToString(),
                    Format = a[FieldNames.Formaat]?.ToString(),
                    Emotional = a[FieldNames.Emotioneel].ParseToInt(),
                    Authoritarian = a[FieldNames.Autoritair].ParseToInt(),
                    Concrete = a[FieldNames.Concreet].ParseToInt(),
                    Convincing = a[FieldNames.Overtuigend].ParseToInt(),
                    Friendly = a[FieldNames.Vriendelijk].ParseToInt(),
                }).ToList()
            };
        }
    }

    public async Task<SystemPrompt> GetTeamsSystemPrompt()
    {
        using (var context = _sharePointService.GetContext(_baseSiteUrl + _chatSiteUrl))
        {
            string caml = string.Format("<View><Query><Where><Eq><FieldRef Name='Title'/><Value Type='Text'>{0}</Value></Eq></Where></Query></View>", "Teams");

            var messages = await context.GetListItemsFromList(_baseSiteUrl + _chatSiteUrl, "Systeem Prompts", caml);
            var systemPrompt = messages.First();

            float.TryParse(systemPrompt?[FieldNames.Temperatuur]?.ToString(), out var temperature);

            return new SystemPrompt()
            {
                Prompt = systemPrompt?[FieldNames.Prompt]?.ToString(),
                ForceVectorGeneration = true,
                Temperature = temperature
            };

        }
    }

    public async Task<List<(byte[] ByteArray, DateTime LastModified)>> GetAiFilesForFile(string folderPath, string file)
    {
        var cacheKey = $"CalculateAndUploadEmbeddingsAsync{folderPath}{file}";
        return _cacheService.Get<List<(byte[] ByteArray, DateTime LastModified)>>(cacheKey);

        //   return await _sharePointService.GetFilesByExtensionFromFolder(this._baseSiteUrl + this._vectorSiteUrl, this._vectorFolderUrl, ".ai", MakeValidSharePointFileName(folderPath + Path.GetFileName(file)));
    }

    public async Task<(string ServerRelativeUrl, DateTime LastModified)?> GetFileInfoAsync(string siteUrl, string filePath)
    {
        using (var ctx = this._sharePointService.GetContext(siteUrl))
        {
            return await ctx.GetFileInfoAsync(filePath);
        }

    }

    public async Task<List<(string ServerRelativeUrl, DateTime LastModified)>> GetSupportedFilesInFolderAsync(string siteUrl, string folderPath, List<string> supportedExtensions)
    {
        using (var clientContext = this._sharePointService.GetContext(siteUrl))
        {
            return await clientContext.GetSupportedFilesInFolderAsync(folderPath, supportedExtensions);
        }
    }

    public async Task<List<(byte[] ByteArray, DateTime LastModified)>> CalculateAndUploadEmbeddingsAsync(string folderPath, string fileName, List<string> lines)
    {
        var dasdsa = Path.GetFileName(fileName);
        var cacheKey = $"CalculateAndUploadEmbeddingsAsync{folderPath}{Path.GetFileName(fileName)}";
        var uploadedFiles = _cacheService.Get<List<(byte[] ByteArray, DateTime LastModified)>>(cacheKey);

        if (uploadedFiles == null)
        {

            const int maxBatchSize = 2000;
            int batchSize = Math.Min(maxBatchSize, lines.Count);
            int suffix = 1;
            uploadedFiles = new List<(byte[] ByteArray, DateTime LastModified)>();

            while (lines.Any())
            {
                try
                {
                    List<string> currentBatch = lines.Take(batchSize).ToList();
                    byte[] embeddings = await _openAIService.CalculateEmbeddingAsync(currentBatch);
                    string fileNameWithSuffix = MakeValidSharePointFileName($"{folderPath}{Path.GetFileName(fileName)}-{suffix}") + ".ai";
                    //await _sharePointService.UploadFileToSharePointAsync(embeddings, this._baseSiteUrl + this._vectorSiteUrl, this._vectorFolderUrl, fileNameWithSuffix);

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

            _cacheService.Set(cacheKey, uploadedFiles);
        }
        return uploadedFiles;
    }

}