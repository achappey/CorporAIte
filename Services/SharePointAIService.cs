using System.Text.RegularExpressions;
using CorporAIte;
using CorporAIte.Extensions;
using CorporAIte.Models;
using Microsoft.SharePoint.Client;
using OpenAI.ObjectModels.RequestModels;

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
            var conversation = await context.GetListItemFromList("Conversaties", itemId);
            var attachments = await context.GetAttachmentsFromListItem(_baseSiteUrl + _chatSiteUrl, "Conversaties", itemId);
            var sources = attachments.Select(a => _baseSiteUrl + a.ServerRelativeUrl).ToList();

            if (conversation?[FieldNames.Map] != null)
            {
                sources.Add(conversation[FieldNames.Map].ToString());
            }

            var messages = await context.GetListItemsFromList("Berichten", $@"
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
            var systemPrompt = await context.GetListItemFromList("Systeem Prompts", lookupId.GetValueOrDefault());

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


    public async Task<List<FunctionDefinition>> GetAllFunctions(string siteUrl)
    {
        var functionDefinitions = new List<FunctionDefinition>();

        using (var context = _sharePointService.GetContext(siteUrl))
        {
            var functionItems = await context.GetListItemsFromList("Functies", CamlQuery.CreateAllItemsQuery().ViewXml);

            foreach (var functionItem in functionItems)
            {
                var functionList = GetFunctionList(context, functionItem);
                await context.ExecuteQueryRetryAsync();

                var viewFields = GetViewFields(context, functionList);
                await context.ExecuteQueryRetryAsync();

                var fields = functionList.GetFields(viewFields);

                functionDefinitions.Add(CreateFunctionDefinition(functionItem, functionList, fields));
            }
        }

        return functionDefinitions;
    }

    private List GetFunctionList(ClientContext context, ListItem functionItem)
    {
        var listTitle = functionItem["Lijst"].ToString();
        var list = context.Web.GetListByTitle(listTitle);
        context.Load(list, f => f.Description, f => f.DefaultView);

        return list;
    }

    private string[] GetViewFields(ClientContext context, List functionList)
    {
        var view = functionList.GetView(functionList.DefaultView.Id);
        context.Load(view, v => v.ViewFields);

        return view.ViewFields.ToArray();
    }

    private FunctionDefinition CreateFunctionDefinition(ListItem functionItem, List functionList, IEnumerable<Field> fields)
    {
        return new FunctionDefinition
        {
            Name = functionItem["Title"].ToString(),
            Description = functionList.Description,
            Parameters = new FunctionParameters
            {
                Properties = fields.ToDictionary(
                    field => field.Title,
                    field => new FunctionParameterPropertyValue
                    {
                        Type = field.FieldTypeKind.SharePointFieldToJson(),
                        Description = field.Description
                    }),
                Required = fields.Select(a => a.Title).ToList()
            }
        };
    }


    public async Task<IEnumerable<Message>> GetFunctionRequests(string siteUrl, string channelId, string messageId)
    {
        List<Message> result = new List<Message>();

        using (var context = _sharePointService.GetContext(siteUrl))
        {
            var items = await context.GetListItemsFromList("Functie verzoeken", $@"
            <View>
                <Query>
                    <Where>
                    <And>
                        <Eq>
                            <FieldRef Name='{FieldNames.Conversatie}' />
                            <Value Type='Text'>{messageId}</Value>
                        </Eq>
                          <Eq>
                            <FieldRef Name='Title' />
                            <Value Type='Text'>{channelId}</Value>
                        </Eq>
                        </And>
                    </Where>
                    <OrderBy>
                        <FieldRef Name='{FieldNames.Created}' Ascending='TRUE' />
                    </OrderBy>
                </Query>
            </View>");

            foreach (var item in items)
            {
                result.Add(new Message()
                {
                    Role = "assistant",
                    Content = "",
                    Created = Convert.ToDateTime(item[FieldNames.Created]),
                    FunctionCall = new CorporAIte.Models.FunctionCall()
                    {
                        Name = item["Naam"].ToString(),
                        Arguments = item["Argumenten"].ToString()
                    }
                });
            }

        }

        return result;
    }

    public async Task<IEnumerable<Message>> GetFunctionResults(string siteUrl, string channelId, string messageId)
    {
        List<Message> result = new List<Message>();

        using (var context = _sharePointService.GetContext(siteUrl))
        {
            var items = await context.GetListItemsFromList("Functie resultaten", $@"
            <View>
                <Query>
                    <Where>
                    <And>
                        <Eq>
                            <FieldRef Name='{FieldNames.Conversatie}' />
                            <Value Type='Text'>{messageId}</Value>
                        </Eq>
                          <Eq>
                            <FieldRef Name='{FieldNames.Channel}' />
                            <Value Type='Text'>{channelId}</Value>
                        </Eq>
                        </And>
                    </Where>
                    <OrderBy>
                        <FieldRef Name='{FieldNames.Created}' Ascending='TRUE' />
                    </OrderBy>
                </Query>
            </View>");

            foreach (var item in items)
            {
                result.Add(new Message()
                {
                    Role = "function",
                    Name = item["Title"].ToString(),
                    Created = Convert.ToDateTime(item[FieldNames.Created]),
                    Content = item["Resultaat"].ToString()
                });
            }

        }

        return result;
    }

    public async Task<SystemPrompt> GetTeamsSystemPrompt()
    {
        using (var context = _sharePointService.GetContext(_baseSiteUrl + _chatSiteUrl))
        {
            string caml = string.Format("<View><Query><Where><Eq><FieldRef Name='Title'/><Value Type='Text'>{0}</Value></Eq></Where></Query></View>", "Teams");

            var messages = await context.GetListItemsFromList("Systeem Prompts", caml);
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