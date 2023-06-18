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
