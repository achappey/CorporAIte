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

    public async Task<IEnumerable<Tag>> GetTags()
    {
        List<FunctionDefinition> result = new List<FunctionDefinition>();

        using (var context = _sharePointService.GetContext(_baseSiteUrl + _vectorSiteUrl))
        {
            var tags = await context.GetListItemsFromList("AI Tags", CamlQuery.CreateAllItemsQuery().ViewXml);

            return tags.Select(a => new Tag()
            {
                Name = a["Title"].ToString(),
                ItemId = a.Id,
                Model = a["Model"]?.ToString(),
                Temperature = (float)Convert.ToDouble(a["Temperatuur"]),
                SystemPrompt = a["Systeemprompt"].ToString(),
            });
        }
    }



    public async Task<List<FunctionDefinition>> GetFunctions(string teamsId, string channelId)
    {
        List<FunctionDefinition> result = new List<FunctionDefinition>();

        using (var context = _sharePointService.GetContext(_baseSiteUrl + _vectorSiteUrl))
        {
            var teams = await context.GetTeamsItem(teamsId);
            var channel = await context.GetTeamsChannelItem(teams.Id, channelId);

            var channelFunctions = await context.GetListItemsFromList("AI Kanaal Functies", $@"
            <View>
                <Query>
                    <Where>
                        <Eq>
                            <FieldRef Name='Kanaal' LookupId='TRUE' />
                            <Value Type='Lookup'>{channel.Id}</Value>
                        </Eq>
                    </Where>
                </Query>
            </View>");

            var functionIds = channelFunctions.Select(a => $@"<Value Type='Number'>{(a["Functie"] as FieldLookupValue).LookupId}</Value>");

            var functions = await context.GetListItemsFromList("AI Functies", $@"
            <View>
                <Query>
                    <Where>
                        <In>
                            <FieldRef Name='ID' />
                            <Values>
                                {string.Join("", functionIds)}
                            </Values>
                        </In>
                    </Where>
                </Query>
            </View>");

            foreach (var item in functions)
            {
                var contentType = context.Web.GetContentTypeByName(item["Definitie"].ToString());
                context.Load(contentType, f => f.Description, f => f.Fields);

                await context.ExecuteQueryRetryAsync();

                var fields = contentType.Fields.Where(a => a.Group == "Fakton AI");

                result.Add(new FunctionDefinition()
                {
                    Name = item["Naam"].ToString(),
                    Description = contentType.Description,
                    Parameters = new FunctionParameters()
                    {
                        Properties = fields.ToDictionary(
                field => field.Title,
                field => new FunctionParameterPropertyValue
                {
                    Type = field.FieldTypeKind.SharePointFieldToJson(),
                    Description = field.Description,
                    Enum = field.FieldTypeKind == FieldType.Choice ? (field as FieldChoice).Choices : null
                }),
                        Required = fields
                        .Where(a => a.Required)
                        .Select(a => a.Title)
                        .ToList()
                    }
                });


            }

        }

        return result;
    }

    public async Task<List<FunctionDefinition>> GetTagFunctions(int tagId, string teamsId)
    {
        List<FunctionDefinition> result = new List<FunctionDefinition>();

        using (var context = _sharePointService.GetContext(_baseSiteUrl + _vectorSiteUrl))
        {
            var teams = await context.GetTeamsItem(teamsId);

            var channelFunctions = await context.GetListItemsFromList("AI Tag Functies", $@"
            <View>
                <Query>
                    <Where>
                        <Eq>
                            <FieldRef Name='Tag' LookupId='TRUE' />
                            <Value Type='Lookup'>{tagId}</Value>
                        </Eq>
                    </Where>
                </Query>
            </View>");
            channelFunctions = channelFunctions.Where(a => a["Team"] == null || (a["Team"] as FieldLookupValue).LookupId == teams.Id);

            foreach (var message in channelFunctions)
            {

                var functieLookup = (message["Functie"] as FieldLookupValue);

                var functions = await context.GetListItemsFromList("AI Functies", $@"
            <View>
                <Query>
                    <Where>
                    <Eq>
                    <FieldRef Name='ID' />
                    <Value Type='Number'>{functieLookup.LookupId}</Value>
                    </Eq>
                    </Where>
                </Query>
            </View>");

                foreach (var item in functions)
                {
                    var contentType = context.Web.GetContentTypeByName(item["Definitie"].ToString());
                    context.Load(contentType, f => f.Description, f => f.Fields);

                    await context.ExecuteQueryRetryAsync();

                    var fields = contentType.Fields.Where(a => a.Group == "Fakton AI");

                    result.Add(new FunctionDefinition()
                    {
                        Name = item["Naam"].ToString(),
                        Description = contentType.Description,
                        Parameters = new FunctionParameters()
                        {
                            Properties = fields.ToDictionary(
                    field => field.Title,
                    field => new FunctionParameterPropertyValue
                    {
                        Type = field.FieldTypeKind.SharePointFieldToJson(),
                        Description = field.Description,
                        Enum = field.FieldTypeKind == FieldType.Choice ? (field as FieldChoice).Choices : null
                    }),
                            Required = fields
                            .Where(a => a.Required)
                            .Select(a => a.Title)
                            .ToList()
                        }
                    });


                }
            }

        }

        return result;
    }

    public async Task<IEnumerable<Message>> GetFunctionRequests(string teamsId, string channelId, string replyToId)
    {
        List<Message> result = new List<Message>();

        using (var context = _sharePointService.GetContext(_baseSiteUrl + _vectorSiteUrl))
        {
            var teams = await context.GetTeamsItem(teamsId);
            var channel = await context.GetTeamsChannelItem(teams.Id, channelId);

            var channelMessages = await context.GetListItemsFromList("AI Kanaalberichten", $@"
            <View>
                <Query>
                    <Where>
                        <And>
                        <Eq>
                            <FieldRef Name='Kanaal' LookupId='TRUE' />
                            <Value Type='Lookup'>{channel.Id}</Value>
                        </Eq>
                        <Eq>
                            <FieldRef Name='Antwoordop' />
                            <Value Type='Text'>{replyToId}</Value>
                        </Eq>
                        </And>
                    </Where>
                </Query>
                <RowLimit>1</RowLimit>
            </View>");

            foreach (var message in channelMessages)
            {
                var functionRequests = await context.GetListItemsFromList("AI Functie Verzoek", $@"
            <View>
                <Query>
                    <Where>
                    <Eq>
                    <FieldRef Name='Kanaalbericht' LookupId='TRUE' />
                    <Value Type='Lookup'>{message.Id}</Value>
                    </Eq>
                    </Where>
                </Query>
            </View>");

                foreach (var item in functionRequests)
                {
                    result.Add(new Message()
                    {
                        Role = "assistant",
                        Content = "",
                        ItemId = item.Id,
                        Created = Convert.ToDateTime(item[FieldNames.Created]),
                        FunctionCall = new CorporAIte.Models.FunctionCall()
                        {
                            Name = item[FieldNames.Title].ToString(),
                            Arguments = item[FieldNames.Arguments].ToString()
                        }
                    });
                }
            }

        }

        return result.OrderBy(a => a.Created);
    }

    public async Task<IEnumerable<Message>> GetFunctionChatRequests(string conversationId)
    {
        List<Message> result = new List<Message>();

        using (var context = _sharePointService.GetContext(_baseSiteUrl + _vectorSiteUrl))
        {
            var channelMessages = await context.GetListItemsFromList("AI Groepchats", $@"
            <View>
                <Query>
                    <Where>
                        <Eq>
                            <FieldRef Name='Title' />
                            <Value Type='Text'>{conversationId}</Value>
                        </Eq>
                    </Where>
                </Query>
                <RowLimit>1</RowLimit>
            </View>");

            foreach (var message in channelMessages)
            {
                var functionRequests = await context.GetListItemsFromList("AI Functie Verzoek", $@"
            <View>
                <Query>
                    <Where>
                    <Eq>
                    <FieldRef Name='Groepschat' LookupId='TRUE' />
                    <Value Type='Lookup'>{message.Id}</Value>
                    </Eq>
                    </Where>
                </Query>
            </View>");

                foreach (var item in functionRequests)
                {
                    result.Add(new Message()
                    {
                        Role = "assistant",
                        Content = "",
                        ItemId = item.Id,
                        Created = Convert.ToDateTime(item[FieldNames.Created]),
                        FunctionCall = new CorporAIte.Models.FunctionCall()
                        {
                            Name = item[FieldNames.Title].ToString(),
                            Arguments = item[FieldNames.Arguments].ToString()
                        }
                    });
                }
            }

        }

        return result.OrderBy(a => a.Created);
    }

    public async Task<IEnumerable<Message>> GetFunctionResults(IEnumerable<int> requestIds)
    {
        List<Message> result = new List<Message>();

        if (requestIds.Count() == 0)
        {
            return result;

        }

        using (var context = _sharePointService.GetContext(_baseSiteUrl + _vectorSiteUrl))
        {


            var channelMessageIds = requestIds.Select(a => $@"<Value Type='Lookup'>{a}</Value>");

            foreach (var message in requestIds)
            {
                var items = await context.GetListItemsFromList("AI Functie Resultaat", $@"
            <View>
                <Query>
                    <Where>
                      <Eq><FieldRef Name='Verzoek' LookupId='TRUE' /><Value Type='Lookup'>{message}</Value></Eq>
                    </Where>
                </Query>
            </View>");

                foreach (var item in items)
                {
                    result.Add(new Message()
                    {
                        Role = "function",
                        Name = item[FieldNames.Title].ToString(),
                        Created = Convert.ToDateTime(item[FieldNames.Created]),
                        Content = item[FieldNames.Results].ToString()
                    });
                }
            }

        }

        return result.OrderBy(a => a.Created);
    }


    public async Task<IEnumerable<Message>> GetFunctionResults2(string teamsId, string channelId, string messageId)
    {
        List<Message> result = new List<Message>();

        using (var context = _sharePointService.GetContext(_baseSiteUrl + _vectorSiteUrl))
        {


            var items = await context.GetListItemsFromList("AI Functie resultaten", $@"
            <View>
                <Query>
                    <Where>
                    <And>
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
                          <Eq>
                            <FieldRef Name='{FieldNames.Teams}' />
                            <Value Type='Text'>{teamsId}</Value>
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
                    Name = item[FieldNames.Title].ToString(),
                    Created = Convert.ToDateTime(item[FieldNames.Created]),
                    Content = item[FieldNames.Results].ToString()
                });
            }

        }

        return result;
    }

    public async Task<SystemPrompt> GetTeamsSystemPrompt(string teamName, string teamEmail, string channelName, string description)
    {
        using (var context = _sharePointService.GetContext(_baseSiteUrl + _chatSiteUrl))
        {
            string caml = string.Format("<View><Query><Where><Eq><FieldRef Name='Title'/><Value Type='Text'>{0}</Value></Eq></Where></Query></View>", "Teams");

            var messages = await context.GetListItemsFromList("Systeem Prompts", caml);
            var systemPrompt = messages.First();

            float.TryParse(systemPrompt?[FieldNames.Temperatuur]?.ToString(), out var temperature);

            return new SystemPrompt()
            {
                Prompt = systemPrompt?[FieldNames.Prompt]?.ToString()
                .Replace("{name}", teamName)
                .Replace("{channel}", channelName)
                .Replace("{teamEmail}", teamEmail)
                .Replace("{description}", description),
                ForceVectorGeneration = true,
                Temperature = temperature
            };

        }
    }
    public async Task<SystemPrompt> GetRoleSystemPrompt(int roleId)
    {
        using (var context = _sharePointService.GetContext(_baseSiteUrl + _vectorSiteUrl))
        {
            var role = await context.GetListItemFromList("AI Rollen", roleId);


            return new SystemPrompt()
            {
                Prompt = role?["Rol"]?.ToString(),
                ForceVectorGeneration = true,
                Model = "gpt-4",
                Temperature = (float)0.1
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