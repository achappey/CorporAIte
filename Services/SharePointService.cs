
using CorporAIte;
using CorporAIte.Extensions;
using HtmlAgilityPack;
using Microsoft.SharePoint.Client;
using PnP.Framework;

public class SharePointService
{
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly ICacheService _cacheService;

    public SharePointService(AppConfig config, ICacheService cacheService)
    {
        this._clientId = config.SharePoint.ClientId;
        this._clientSecret = config.SharePoint.ClientSecret;
        this._cacheService = cacheService;
    }

    public ClientContext GetContext(string url)
    {
        AuthenticationManager authManager = new AuthenticationManager();

        var context = authManager.GetACSAppOnlyContext(url, this._clientId, this._clientSecret);
        context.RequestTimeout = Timeout.Infinite;

        return context;
    }

    public async Task<string> ReadUrlFromFileAsync(string siteUrl, string pageUrl)
    {
        // Connect to the SharePoint site using the ClientContext.
        using (var context = GetContext(siteUrl))
        {
            var file = context.Web.GetFileByServerRelativeUrl(pageUrl);
            context.Load(file);
            await context.ExecuteQueryAsync();

            // Read the content of the .url file.
            ClientResult<Stream> streamResult = file.OpenBinaryStream();
            await context.ExecuteQueryAsync();

            using (var memoryStream = new MemoryStream())
            {
                streamResult.Value.CopyTo(memoryStream);
                memoryStream.Position = 0;
                using (var reader = new StreamReader(memoryStream))
                {
                    string urlFileContent = await reader.ReadToEndAsync();

                    // Extract the URL from the file content.
                    string urlKey = "URL=";
                    int startIndex = urlFileContent.IndexOf(urlKey) + urlKey.Length;
                    int endIndex = urlFileContent.IndexOf("\r\n", startIndex);
                    if (endIndex == -1) endIndex = urlFileContent.Length;
                    string url = urlFileContent.Substring(startIndex, endIndex - startIndex);
                    return url;
                }
            }
        }
    }

    public async Task<List<string>> GetSharePointPageTextAsync(string siteUrl, string pageUrl)
    {
        var cacheKey = $"GetSharePointPageTextAsync{pageUrl}";
        var pageParagraphs = _cacheService.Get<List<string>>(cacheKey);

        if (pageParagraphs is null)
        {
            using (var context = GetContext(siteUrl))
            {
                var file = context.Web.GetFileByServerRelativeUrl(pageUrl);
                context.Load(file, f => f.ListItemAllFields, f => f.ListItemAllFields["ContentTypeId"], f => f.ListItemAllFields["CanvasContent1"]);
                await context.ExecuteQueryRetryAsync().ConfigureAwait(false);

                var listItem = file.ListItemAllFields;

                if (listItem["ContentTypeId"].ToString().StartsWith("0x0101009D1CB255DA76424F860D91F20E6C4118"))
                {
                    if (listItem["CanvasContent1"] is not null)
                    {
                        string htmlContent = listItem["CanvasContent1"].ToString();
                        var htmlDoc = new HtmlDocument();
                        htmlDoc.LoadHtml(htmlContent);

                        pageParagraphs = htmlDoc.ExtractTextFromHtmlParagraphs();
                        _cacheService.Set(cacheKey, pageParagraphs);
                    }
                }
            }
        }

        return pageParagraphs;
    }



    public async Task<byte[]> DownloadFileFromSharePointAsync(string siteUrl, string filePath)
    {
        var cacheKey = "DownloadFileFromSharePointAsync" + siteUrl + filePath;

        byte[] fileBytes = _cacheService.Get<byte[]>(cacheKey);

        if (fileBytes == null)
        {

            // Create a client context object for the SharePoint site
            using (var context = GetContext(siteUrl))
            {
                // Get the file object from SharePoint by path
                var file = context.Web.GetFileByServerRelativeUrl(filePath);

                // Load the file object
                context.Load(file);

                // Execute the query to load the file object
                await context.ExecuteQueryRetryAsync();

                // Download the file from SharePoint and convert it to a byte array
                var fileStream = file.OpenBinaryStream();

                await context.ExecuteQueryRetryAsync();

                using (var memoryStream = new MemoryStream())
                {
                    await fileStream.Value.CopyToAsync(memoryStream);
                    fileBytes = memoryStream.ToArray();
                }

                // Add the file bytes to the cache
                _cacheService.Set(cacheKey, fileBytes);

            }
        }

        return fileBytes;
    }

    public async Task<List<(byte[] ByteArray, DateTime LastModified)>> GetFilesByExtensionFromFolder(string siteUrl, string folderUrl, string extension, string startsWith = "")
    {
        string cacheKey = $"GetFilesByExtensionFromFolder:{siteUrl}:{folderUrl}:{extension}:{startsWith}";

        List<(byte[] ByteArray, DateTime LastModified)> byteArrays = _cacheService.Get<List<(byte[] ByteArray, DateTime LastModified)>>(cacheKey);

        if (byteArrays == null)
        {
            byteArrays = new List<(byte[] ByteArray, DateTime LastModified)>();

            using (var clientContext = GetContext(siteUrl))
            {
                var web = clientContext.Web;
                var folder = web.GetFolderByServerRelativeUrl(folderUrl);
                clientContext.Load(folder, a => a.Files);
                await clientContext.ExecuteQueryRetryAsync();

                var filteredFiles = folder.Files.Where(file =>
                    file.Name.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetExtension(file.Name).Equals(extension, StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var file in filteredFiles)
                {
                    clientContext.Load(file);
                    await clientContext.ExecuteQueryRetryAsync();

                    var fileStream = file.OpenBinaryStream();
                    await clientContext.ExecuteQueryRetryAsync();

                    using (var memoryStream = new MemoryStream())
                    {
                        fileStream.Value.CopyTo(memoryStream);
                        byteArrays.Add((memoryStream.ToArray(), file.TimeLastModified));
                    }
                }
            }

            _cacheService.Set(cacheKey, byteArrays);
        }

        return byteArrays;
    }


    public async Task UploadFileToSharePointAsync(byte[] fileBytes, string siteUrl, string folderUrl, string fileName)
    {
        const int blockSize = 2 * 1024 * 1024; // 2 MB

        // Create a client context object for the SharePoint site
        using (var context = GetContext(siteUrl))
        {
            // Get the folder object in which to upload the file
            var folder = context.Web.GetFolderByServerRelativeUrl(folderUrl);

            // Upload file based on its size
            if (fileBytes.Length <= blockSize)
            {
                await context.UploadSmallFileAsync(folder, fileBytes, fileName);
            }
            else
            {
                await context.UploadLargeFileAsync(folder, fileBytes, fileName, blockSize, folderUrl);
            }
        }
    }



}
