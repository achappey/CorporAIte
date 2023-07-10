using AutoMapper;
using CorporAIte.Extensions;
using CorporAIte.Models;
using OpenAI.ObjectModels.RequestModels;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public class CorporAIteService
{
    private readonly ILogger<CorporAIteService> _logger;

    private readonly SharePointService _sharePointService;

    private readonly SharePointAIService _sharePointAIService;

    private readonly AIService _openAIService;

    private readonly GraphService _graphService;

    private readonly IMapper _mapper;

    private readonly List<string> supportedExtensions = new List<string> { ".aspx",
        ".docx", ".csv", ".pdf", ".pptx", ".txt", ".url", ".msg", ".onenote" };

    private readonly HttpClient _httpClient;

    public CorporAIteService(ILogger<CorporAIteService> logger,
    SharePointService sharePointService,
    GraphService graphService,
    SharePointAIService sharePointAIService,
    AIService openAIService,
    HttpClient httpClient,
    IMapper mapper)
    {
        _logger = logger;
        _sharePointService = sharePointService;
        _sharePointAIService = sharePointAIService;
        _graphService = graphService;
        _openAIService = openAIService;
        _mapper = mapper;
        _httpClient = httpClient;
    }

    public async Task<List<(byte[] ByteArray, DateTime LastModified)>> CalculateFolderEmbeddingsAsync(string siteUrl, string folderPath)
    {
        var supportedFiles = await _sharePointAIService.GetSupportedFilesInFolderAsync(siteUrl, folderPath, this.supportedExtensions);

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
            case ".onenote":
                var noteBookId = (siteUrl + file).ExtractOneNote();

                return await _graphService.GetNotebookContent(noteBookId.teamsId, noteBookId.notebookId);
            case ".aspx":
                return await _sharePointService.GetSharePointPageTextAsync(siteUrl, file);

            case ".url":
                var url = await _sharePointService.ReadUrlFromFileAsync(siteUrl, file);
                return await this._httpClient.ConvertPageToList(url);

            case ".msg":
                byte[] mailBytes = await _sharePointService.DownloadFileFromSharePointAsync(siteUrl, file);
                var mailFile = mailBytes.ConvertFileContentToList(extension);

                var attachments = mailBytes.ExtractAttachments();

                foreach (var attachment in attachments)
                {
                    string attachmentExtension = Path.GetExtension(attachment.Item2).ToLowerInvariant();

                    try
                    {
                        mailFile.AddRange(attachment.Item1.ConvertFileContentToList(attachmentExtension));
                    }
                    catch (NotSupportedException)
                    {

                    }
                }

                return mailFile;

            default:
                byte[] bytes = await _sharePointService.DownloadFileFromSharePointAsync(siteUrl, file);
                return bytes.ConvertFileContentToList(extension);
        }
    }

    private async Task<Message> ChatWithDataFolderAsync(List<string> inputPaths, Conversation chat)
    {
        var queryEmbedding = await _openAIService.CalculateEmbeddingAsync(chat.Messages.Last(t => t.Role == "user").Content).ConfigureAwait(false);

        // Separate input paths into files and folders
        var (folders, files) = inputPaths.SeparateInputPaths();

        // Process the folders concurrently
        var folderResults = await ProcessFoldersAsync(folders, queryEmbedding, chat.SystemPrompt.ForceVectorGeneration).ConfigureAwait(false);

        // Process the single files concurrently
        var fileResults = await ProcessSingleFilesAsync(files, queryEmbedding, chat.SystemPrompt.ForceVectorGeneration).ConfigureAwait(false);

        var allResults = folderResults.Concat(fileResults).ToList();

        var topResults = allResults
            .OrderByDescending(result => result.Score)
            .Take(400)
            .ToList();

        return await ChatWithBestContext(topResults, chat).ConfigureAwait(false);
    }

    private async Task<List<dynamic>> ProcessSingleFilesAsync(List<string> fileUrls, byte[] queryEmbedding, bool forceVectorGeneration)
    {
        var fileTasks = fileUrls.Select(async fileUrl =>
        {
            // Extract folder and siteUrl from the file URL
            var folder = fileUrl.GetParentFolderFromServerRelativeUrl();
            var siteUrl = fileUrl.GetSiteUrlFromFullUrl();

            /*     var fileInfo = await _sharePointAIService.GetFileInfoAsync(siteUrl, fileUrl.RemoveBaseUrl()).ConfigureAwait(false);
                 if (fileInfo == null)
                 {
                     return Enumerable.Empty<dynamic>();
                 }

                 var (serverRelativeUrl, lastModified) = fileInfo.Value;*/

            return await ProcessFilesAsync(folder, siteUrl, new List<(string ServerRelativeUrl, DateTime LastModified)> { (fileUrl.RemoveBaseUrl(), new DateTime()) }, queryEmbedding, forceVectorGeneration).ConfigureAwait(false);
        });

        // Wait for all file tasks to complete and concatenate the results
        return (await Task.WhenAll(fileTasks)).SelectMany(r => r as IEnumerable<dynamic>).ToList();
    }

    private async Task<List<dynamic>> ProcessFoldersAsync(List<string> folders, byte[] queryEmbedding, bool forceVectorGeneration)
    {
        var folderTasks = folders.Select(async folder =>
        {
            //  var siteUrl = this._sharePointService.ExtractSiteServerRelativeUrl(folder);
            var siteUrl = folder.GetSiteUrlFromFullUrl();
            var siteRelative = folder.RemoveBaseUrl();

            // Get the supported files in the folder
            var files = await _sharePointAIService.GetSupportedFilesInFolderAsync(siteUrl, siteRelative, this.supportedExtensions).ConfigureAwait(false);

            // Process the files concurrently
            return await ProcessFilesAsync(folder, siteUrl, files, queryEmbedding, forceVectorGeneration).ConfigureAwait(false);
        });

        // Wait for all folder tasks to complete and concatenate the results
        return (await Task.WhenAll(folderTasks)).SelectMany(r => r as IEnumerable<dynamic>).ToList();
    }

    private async Task<List<dynamic>> ProcessFilesAsync(string folder, string siteUrl, List<(string ServerRelativeUrl, DateTime LastModified)> files, byte[] queryEmbedding, bool forceVectorGeneration)
    {
        var fileTasks = files.Select(async file =>
        {
            //   var aiFiles = await this._sharePointAIService.GetAiFilesForFile(folder, Path.GetFileName(file.ServerRelativeUrl)).ConfigureAwait(false);
            List<(byte[] ByteArray, DateTime LastModified)> aiFiles = null;
            var lines = await GetLinesAsync(siteUrl, file.ServerRelativeUrl).ConfigureAwait(false);

            if (lines == null || !lines.Any())
            {
                return Enumerable.Empty<dynamic>();
            }

            if (((aiFiles == null || !aiFiles.Any() && forceVectorGeneration) || (aiFiles.Any() && aiFiles.First().LastModified < file.LastModified)))
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
                Path = siteUrl + file.ServerRelativeUrl,
                Text = line,
                Score = scores.ElementAt(index)
            });
        });

        // Wait for all file tasks to complete and concatenate the results
        return (await Task.WhenAll(fileTasks)).SelectMany(r => r as IEnumerable<dynamic>).ToList();
    }

    private async Task<Message> ChatWithBestContext(List<dynamic> topResults, Conversation chat)
    {
        var uniquePaths = string.Join(", ", topResults.Select(a => a.Path).Distinct());
        var contextQuery = uniquePaths + " " + string.Join(" ", topResults.Select(a => a.Text));

        while (!string.IsNullOrEmpty(contextQuery))
        {
            try
            {
                var chatResponse = await _openAIService.ChatWithContextAsync(chat.SystemPrompt.Prompt + contextQuery,
                  chat.SystemPrompt.Temperature,
                  chat.Messages.Select(a => _mapper.Map<OpenAI.ObjectModels.RequestModels.ChatMessage>(a)),
                  chat.Functions,
                  chat.SystemPrompt.Model);

                return _mapper.Map<Message>(chatResponse);
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

    public async Task<Conversation> GetChatAsync(int chatId, string messageContent = null)
    {
        var chat = await this._sharePointAIService.GetListChat(chatId);

        if (!string.IsNullOrEmpty(messageContent))
        {
            chat.Messages.Add(new Message()
            {
                Role = "user",
                Content = messageContent
            });
        }

        return chat;
    }

    public async Task<Message> ProcessChatAsync(Conversation chat)
    {
        if (chat.Sources.Any())
        {
            return await ChatWithDataFolderAsync(chat.Sources, chat);
        }
        else
        {
            return await ChatWithFallback(chat);
        }
    }

    public async Task<Message> ListChatAsync(int chatId)
    {
        var chat = await GetChatAsync(chatId);

        return await ProcessChatAsync(chat);
    }

    private async Task<Conversation> GetTeamsChannelChat(string teamsId, string channelId, string messageId, string replyTo, bool channelChat, bool tabChat)
    {
        var messages = await this._graphService.GetAllMessagesFromConversation(teamsId, channelId, replyTo);
        var userMessage = messages.FirstOrDefault(a => a.Id == messageId);

        var tags = await this._sharePointAIService.GetTags();
        var tag = tags.FirstOrDefault(a => userMessage.Mentions.Any(c =>  c.MentionText.ToLower() == a.Name.ToLower()));

        if(tag == null && userMessage.Body.Content.Contains("New Service Message")) 
        {
            tag = tags.FirstOrDefault(y => y.Name == "Service");
        }

        var userString = "";
        var userId = userMessage != null && userMessage.From.User != null ? userMessage.From.User.Id : null;

        if(userMessage != null && userMessage.From.User != null) {
            var user = await this._graphService.GetUser(userMessage.From.User.Id);

            userString = (user.DisplayName + " (" + user.Department + ", " + user.Mail + ", " + user.EmployeeId + ")");
        }

        var chatMesages = messages
                .Where(z => !z.DeletedDateTime.HasValue)
                .Where(z => !string.IsNullOrEmpty(z.Body.Content))
                .Select(z => new Message()
                {
                    Role = z.From.Application != null ? "assistant" : "user",
                    Created = z.CreatedDateTime,
                    Content = z.From.Application != null ? z.Body.Content : z.From.User.Id == userId ? userString + " op " + z.CreatedDateTime?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + ": " + z.Body.Content 
                     : z.From.User.DisplayName + " op " + z.CreatedDateTime?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)  + ": " + z.Body.Content
                })
                .Where(a => a.Role != "assistant" || (!a.Content.Contains("Functie uitvoeren:") && !a.Content.Contains("Functie uitgevoerd:"))).ToList();

        var team = await this._graphService.GetTeam(teamsId);
        var group = await this._graphService.GetGroup(teamsId);
        
        var teamsChannel = await this._graphService.GetTeamsChannel(teamsId, channelId);
        var systemPrompt = !string.IsNullOrEmpty(tag?.SystemPrompt) ? new SystemPrompt() { 
            Prompt = tag.SystemPrompt,
            Model = tag.Model,
            Temperature = tag.Temperature,
        }  : 
            await this._sharePointAIService.GetTeamsSystemPrompt(team.DisplayName, group.Mail, teamsChannel.DisplayName, teamsChannel.Description);

        var sources = messages.SelectMany(a => a.Attachments.Where(z => !string.IsNullOrEmpty(z.ContentUrl)).Select(z => z.ContentUrl))
                    .Where(y => this.supportedExtensions.Contains(Path.GetExtension(y).ToLowerInvariant()))
                    .ToList();

        var sharePointUrl = await this._graphService.GetSharePointUrl(teamsId, channelId);

        if (channelChat)
        {
            sources.Add(sharePointUrl);
        }

        if (tabChat)
        {
            var tabs = await this._graphService.GetAllTabsFromChannel(teamsId, channelId);

            var tabSources = tabs.Select(a => a.Configuration.ContentUrl.ExtractContentSource())
            .Where(a => !string.IsNullOrEmpty(a))
            .Select(a => a.StartsWith("https://www.onenote.com/") ? a + "/teams/" + teamsId + ".onenote" : a)
            .Where(y => this.supportedExtensions.Contains(Path.GetExtension(y).ToLowerInvariant()))
            .ToList();

            sources.AddRange(tabSources);
        }

        List<FunctionDefinition>? functions = null;

        try
        {
            if(tag == null) {
                functions = await this._sharePointAIService.GetFunctions(teamsId, channelId);
           
            }
            else {
                functions = await this._sharePointAIService.GetTagFunctions(tag.ItemId, teamsId);
            }

            var functionRequests = await this._sharePointAIService.GetFunctionRequests(teamsId, channelId, replyTo);
            var functionResults = await this._sharePointAIService.GetFunctionResults(functionRequests.Select(a => a.ItemId));

            chatMesages.AddRange(functionRequests);
            chatMesages.AddRange(functionResults);

            chatMesages = chatMesages.OrderBy(a => a.Created).ToList();
        }
        catch (Exception e)
        {
        }

        return new Conversation()
        {
            SystemPrompt = systemPrompt,
            Sources = sources,
            Messages = chatMesages,
            Functions = functions?.Count > 0 ? functions : null,
        };
    }


    private async Task<Conversation> GetTeamsChat(string chatId)
    {
        var messages = await this._graphService.GetAllMessagesFromChat(chatId);
        var systemPrompt = await this._sharePointAIService.GetTeamsSystemPrompt("", "", "", "");

        var sources = messages.SelectMany(a => a.Attachments.Where(z => !string.IsNullOrEmpty(z.ContentUrl)).Select(z => z.ContentUrl))
                    .Where(y => this.supportedExtensions.Contains(Path.GetExtension(y).ToLowerInvariant()))
                    .ToList();

        return new Conversation()
        {
            SystemPrompt = systemPrompt,
            Sources = sources,
            Messages = messages
            .Where(z => z.From != null && !string.IsNullOrEmpty(z.Body.Content))
            .Select(z => new Message()
            {
                Role = z.From.Application != null ? "assistant" : "user",
                Content = z.From.Application != null ? z.Body.Content : z.From.User.DisplayName + ": " + z.Body.Content
            }).ToList()
        };
    }

    public async Task<Message> TeamsChatAsync(string chatId)
    {
        var chat = await GetTeamsChat(chatId);

        if (!chat.Messages.Any())
        {
            throw new NotSupportedException();
        }

        return await ProcessChatAsync(chat);
    }

    public async Task<Message> TeamsChannelChatAsync(string teamsId, string channelId, string messageId, string replyTo, bool channelChat, bool tabChat)
    {
        var chat = await GetTeamsChannelChat(teamsId, channelId, messageId, replyTo, channelChat, tabChat);

        if (!chat.Messages.Any() || (chat.Messages.Last().Role == "assistant" && !chat.Messages.Last().Content.Contains("AutoGPT")))
        {
            throw new NotSupportedException();
        }

        return await ProcessChatAsync(chat);
    }


    public async Task<Folders> GetOneDriveFolders(string userId)
    {
        var items = await this._graphService.GetAllFoldersFromRoot(userId);

        return new Folders()
        {
            Items = items.Select(a => this._mapper.Map<Folder>(a))
        };
    }

    private async Task<Message> ChatWithFallback(Conversation chat)
    {
        while (chat.Messages.Count > 0)
        {
            try
            {
                return await AttemptChatAsync(chat);
            }
            catch (FormatException)
            {
                chat.ShortenChatHistory();
            }
        }

        throw new InvalidOperationException("The chat history could not be shortened further without success.");
    }

    private async Task<Message> AttemptChatAsync(Conversation chat)
    {
        // Prepare chat messages for the OpenAIService
        var openAiChatMessages = chat.Messages
            .Select(message => _mapper.Map<OpenAI.ObjectModels.RequestModels.ChatMessage>(message));

        // Chat with context using the OpenAIService
        var chatResponse = await _openAIService.ChatWithContextAsync(
            chat.SystemPrompt.Prompt,
            chat.SystemPrompt.Temperature,
            openAiChatMessages,
            chat.Functions,
            chat.SystemPrompt.Model);

        // Map the response to the ChatMessage model
        return _mapper.Map<Message>(chatResponse);
    }

}

