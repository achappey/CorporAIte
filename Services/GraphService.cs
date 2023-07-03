using HtmlAgilityPack;
using Microsoft.Graph;

public class GraphService
{
    private readonly GraphServiceClient _graph;

    public GraphService(GraphServiceClient graph)
    {
        this._graph = graph;
    }

    public List<string> GetOneDriveFiles()
    {
        return new List<string>();
    }

    public async Task<List<string>> GetNotebookContent(string teamsId, string notebookId)
    {
        // Get all the users in the tenant
        var notebook = await _graph.Groups[teamsId].Onenote.Notebooks[notebookId].Sections.Request().GetAsync();

        // List to store all the paragraphs
        List<string> paragraphs = new List<string>();

        // For each user, get their notebooks
        foreach (var section in notebook)
        {
            var pages = await _graph.Groups[teamsId].Onenote.Sections[section.Id].Pages.Request().GetAsync();

            foreach (var page in pages)
            {
                var content = await _graph.Groups[teamsId].Onenote.Pages[page.Id].Content.Request().GetAsync();

                using (StreamReader reader = new StreamReader(content))
                {
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(await reader.ReadToEndAsync());
                    var pageParagraphs = htmlDoc.DocumentNode.Descendants()
                                    .Where(n => n.Name == "p" || n.Name == "li")
                                    .Select(n => n.InnerText)
                                    .ToList();

                    paragraphs.AddRange(pageParagraphs);
                }
            }
        }

        return paragraphs;
    }

    public async Task<List<ChatMessage>> GetAllMessagesFromChat(string chatId)
    {
        List<ChatMessage> chatMessages = new List<ChatMessage>();

        // Get all messages in the chat.
        var messagesRequest = _graph.Chats[chatId].Messages.Request();
        do
        {
            var messagesPage = await messagesRequest.GetAsync();

            // Add each message's content to the list.
            foreach (var message in messagesPage)
            {
                chatMessages.Add(message);
            }

            // Get the next page of messages, if there is one.
            messagesRequest = messagesPage.NextPageRequest;

        } while (messagesRequest != null);

        return chatMessages.OrderBy(a => a.CreatedDateTime).ToList();
    }
 public async Task<User> GetUser(string user)
    {
         return await _graph.Users[user].Request().Select("department,mail,displayName").GetAsync();
    }
 public async Task<Group> GetGroup(string teamId)
    {
         return await _graph.Groups[teamId].Request().Select("mail").GetAsync();
    }
    public async Task<Team> GetTeam(string teamId)
    {
         return await _graph.Teams[teamId].Request().Select("displayName").GetAsync();
    }

    public async Task<Channel> GetTeamsChannel(string teamId, string channelId)
    {
         return await _graph.Teams[teamId].Channels[channelId].Request().Select("displayName,description").GetAsync();
    }

    public async Task<IEnumerable<ChatMessageMention>> GetMentions(string teamId, string channelId, string messageId)
    {

        var rootMessage = await _graph.Teams[teamId].Channels[channelId].Messages[messageId].Request().GetAsync();

        return rootMessage.Mentions;
    }

    public async Task<List<ChatMessage>> GetAllMessagesFromConversation(string teamId, string channelId, string messageId)
    {
        List<ChatMessage> conversationMessages = new List<ChatMessage>();

        // Get the root message using the messageId.
        var rootMessage = await _graph.Teams[teamId].Channels[channelId].Messages[messageId].Request().GetAsync();
        conversationMessages.Add(rootMessage);

        // Get all replies to the root message.
        var repliesRequest = _graph.Teams[teamId].Channels[channelId].Messages[messageId].Replies.Request();
        do
        {
            var repliesPage = await repliesRequest.GetAsync();

            // Add each reply's content to the list.
            foreach (var reply in repliesPage)
            {
                //    conversationMessages.Add(reply.Body.Content);
                conversationMessages.Add(reply);
            }

            // Get the next page of replies, if there is one.
            repliesRequest = repliesPage.NextPageRequest;

        } while (repliesRequest != null);

        return conversationMessages.OrderBy(a => a.CreatedDateTime).ToList();
    }

    public async Task<List<TeamsTab>> GetAllTabsFromChannel(string teamId, string channelId)
    {
        List<TeamsTab> channelTabs = new List<TeamsTab>();

        // Get all tabs in the channel
        var tabsRequest = _graph.Teams[teamId].Channels[channelId].Tabs.Request();
        do
        {
            var tabsPage = await tabsRequest.GetAsync();

            // Add each tab to the list.
            foreach (var tab in tabsPage)
            {
                channelTabs.Add(tab);
            }

            // Get the next page of tabs, if there is one.
            tabsRequest = tabsPage.NextPageRequest;

        } while (tabsRequest != null);

        return channelTabs;
    }


    public async Task<List<DriveItem>> GetAllFilesFromOneDriveFolder(string userId, string folderId)
    {
        List<DriveItem> files = new List<DriveItem>();

        // Retrieve the root folder or a specific subfolder by ID
        var driveItemsRequest = this._graph.Users[userId].Drive.Items[folderId].Children.Request();

        do
        {
            var driveItems = await driveItemsRequest.GetAsync();

            foreach (var driveItem in driveItems)
            {
                if (driveItem.Folder != null)
                {
                    // Recursively retrieve files from subfolders
                    var subfolderFiles = await GetAllFilesFromOneDriveFolder(userId, driveItem.Id);
                    files.AddRange(subfolderFiles);
                }
                else
                {
                    // Add individual files to the list
                    files.Add(driveItem);
                }
            }

            // Retrieve the next page of results, if available
            driveItemsRequest = driveItems.NextPageRequest;
        }
        while (driveItemsRequest != null);

        return files;
    }

    public async Task<string> GetSharePointUrl(string teamId, string channelId)
    {
        // Get the DriveItem object for the channel's files folder.
        var filesFolder = await _graph.Teams[teamId].Channels[channelId].FilesFolder.Request().GetAsync();

        // The SharePoint URL is stored in the WebUrl property of the DriveItem.
        return filesFolder.WebUrl;
    }


    public async Task<List<DriveItem>> GetAllFoldersFromRoot(string userId)
    {
        List<DriveItem> folders = new List<DriveItem>();

        var driveItemsRequest = this._graph.Users[userId].Drive.Root.Children.Request();
        do
        {
            var driveItems = await driveItemsRequest.GetAsync();

            foreach (var driveItem in driveItems)
            {
                if (driveItem.Folder != null)
                {
                    folders.Add(driveItem);
                }
            }

            driveItemsRequest = driveItems.NextPageRequest;
        }
        while (driveItemsRequest != null);

        return folders;
    }

}
