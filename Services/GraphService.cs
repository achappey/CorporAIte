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
