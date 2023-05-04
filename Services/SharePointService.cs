
using CorporAIte;
using HtmlAgilityPack;
using Microsoft.SharePoint.Client;
using PnP.Framework;

public class SharePointService
{
    private readonly string _tenantName;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private static Dictionary<string, (byte[], DateTime)> _cache = new Dictionary<string, (byte[], DateTime)>();
    private static readonly int MAX_CACHE_SIZE = 1000;
    private static readonly TimeSpan CACHE_EXPIRATION_TIME = TimeSpan.FromDays(1);
    private static readonly object _cacheLock = new object();

    private static Dictionary<string, (List<(byte[] ByteArray, DateTime LastModified)>, DateTime)> _vectorCache = new Dictionary<string, (List<(byte[] ByteArray, DateTime LastModified)>, DateTime)>();
    private static readonly object _vectorCacheLock = new object();

    private static Dictionary<string, (List<string>, DateTime)> _pageCache = new Dictionary<string, (List<string>, DateTime)>();
    private static readonly object _pageCacheLock = new object();
    private readonly ICacheService _cacheService;

    public SharePointService(AppConfig config, ICacheService cacheService)
    {
        this._tenantName = config.SharePoint.TenantName;
        this._clientId = config.SharePoint.ClientId;
        this._clientSecret = config.SharePoint.ClientSecret;
        this._cacheService = cacheService;
    }

    public string BaseUrl
    {
        get
        {
            return $"https://{this._tenantName}.sharepoint.com";
        }
    }


    /// <summary>
    /// Converts a site path to a tenant URL by appending it to the base URL.
    /// </summary>
    /// <param name="site">The site path to convert.</param>
    /// <returns>The tenant URL.</returns>
    private string ToTenantUrl(string site)
    {
        if (string.IsNullOrWhiteSpace(site))
        {
            return BaseUrl;
        }

        if (site.StartsWith("/sites/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{BaseUrl}{site}";
        }

        return $"{BaseUrl}/sites/{site}";
    }



    public ClientContext GetContext(string url)
    {
        AuthenticationManager authManager = new AuthenticationManager();

        return authManager.GetACSAppOnlyContext(this.ToTenantUrl(url), this._clientId, this._clientSecret);
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

                        pageParagraphs = ExtractTextFromHtmlParagraphs(htmlDoc);
                        _cacheService.Set(cacheKey, pageParagraphs);
                    }
                }
            }
        }

        return pageParagraphs;
    }

    private List<string> ExtractTextFromHtmlParagraphs(HtmlDocument htmlDoc)
    {
        var paragraphs = htmlDoc.DocumentNode.SelectNodes("//p");
        var pageParagraphs = new List<string>();

        if (paragraphs is not null)
        {
            foreach (var paragraph in paragraphs)
            {
                if (paragraph.InnerText is not null)
                {
                    var text = paragraph.InnerText.Trim();

                    if (!string.IsNullOrEmpty(text))
                    {
                        pageParagraphs.Add(text);
                    }
                }
            }
        }

        return pageParagraphs;
    }


    public async Task<List<string>> GetSharePointPageTextAsync2(string siteUrl, string pageUrl)
    {
        var cacheKey = "GetSharePointPageTextAsync" + pageUrl;

        List<string> pageParagraphs = _cacheService.Get<List<string>>(cacheKey);

        if (pageParagraphs == null)
        {
            pageParagraphs = new List<string>();
            using (var context = GetContext(siteUrl))
            {
                var file = context.Web.GetFileByServerRelativeUrl(pageUrl);
                //       context.Load(file, f => f.ListItemAllFields);
                context.Load(file, f => f.ListItemAllFields, f => f.ListItemAllFields["ContentTypeId"], f => f.ListItemAllFields["CanvasContent1"]);

                await context.ExecuteQueryRetryAsync();

                var listItem = file.ListItemAllFields;

                if (listItem["ContentTypeId"].ToString().StartsWith("0x0101009D1CB255DA76424F860D91F20E6C4118"))
                {
                    if (listItem["CanvasContent1"] != null)
                    {
                        string htmlContent = listItem["CanvasContent1"].ToString();
                        var htmlDoc = new HtmlDocument();
                        htmlDoc.LoadHtml(htmlContent);

                        var paragraphs = htmlDoc.DocumentNode.SelectNodes("//p");
                        if (paragraphs != null)
                        {
                            foreach (var paragraph in paragraphs)
                            {
                                if (paragraph.InnerText != null)
                                {
                                    var text = paragraph.InnerText.Trim();

                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        pageParagraphs.Add(text);
                                    }

                                }

                            }

                            _cacheService.Set(cacheKey, pageParagraphs);


                        }
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

    public async Task<List<(string ServerRelativeUrl, DateTime LastModified)>> GetSupportedFilesInFolderAsync(string siteUrl, string folderPath, List<string> supportedExtensions)
    {
        using (var clientContext = GetContext(siteUrl))
        {
            var web = clientContext.Web;
            var folder = web.GetFolderByServerRelativeUrl(folderPath);
            var supportedFiles = new List<(string, DateTime)>();

            await RetrieveSupportedFilesRecursively(clientContext, folder, supportedFiles, supportedExtensions);

            return supportedFiles.Where(a => !a.Item1.StartsWith(folderPath + "/Forms/")).ToList();
        }
    }

    private async Task RetrieveSupportedFilesRecursively(ClientContext clientContext, Folder folder, List<(string, DateTime)> supportedFiles, List<string> supportedExtensions, bool includeSubfolders = true)
    {
        clientContext.Load(folder, a => a.Files, a => a.Folders);
        await clientContext.ExecuteQueryRetryAsync();

        var filteredFiles = folder.Files.Where(file =>
            supportedExtensions.Contains(Path.GetExtension(file.Name).ToLowerInvariant())).ToList();

        foreach (var file in filteredFiles)
        {
            clientContext.Load(file, a => a.ServerRelativeUrl, a => a.TimeLastModified);
            await clientContext.ExecuteQueryRetryAsync();
            supportedFiles.Add((file.ServerRelativeUrl, file.TimeLastModified));
        }

        if (includeSubfolders)
        {
            var excludeForms = folder.Folders.Where(a => a.Name != "Forms");

            foreach (var subfolder in excludeForms)
            {
                await RetrieveSupportedFilesRecursively(clientContext, subfolder, supportedFiles, supportedExtensions, includeSubfolders);
            }
        }
    }

    public async Task<(string ServerRelativeUrl, DateTime LastModified)?> GetFileInfoAsync(string siteUrl, string filePath)
    {
        // Assuming you have already set up the SharePoint ClientContext
        using (var ctx = GetContext(siteUrl))
        {
            var file = ctx.Web.GetFileByServerRelativeUrl(filePath);
            ctx.Load(file, f => f.ServerRelativeUrl, f => f.TimeLastModified);
            await ctx.ExecuteQueryAsync();

            if (file != null)
            {
                return (file.ServerRelativeUrl, file.TimeLastModified);
            }
        }

        return null;
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
                await UploadSmallFileAsync(context, folder, fileBytes, fileName);
            }
            else
            {
                await UploadLargeFileAsync(context, folder, fileBytes, fileName, blockSize);
            }
        }
    }

    private async Task UploadSmallFileAsync(ClientContext context, Folder folder, byte[] fileBytes, string fileName)
    {
        FileCreationInformation fileInfo = new FileCreationInformation
        {
            Content = fileBytes,
            Url = fileName,
            Overwrite = true
        };

        folder.Files.Add(fileInfo);
        await context.ExecuteQueryRetryAsync();
    }

    private async Task UploadLargeFileAsync(ClientContext context, Folder folder, byte[] fileBytes, string fileName, int blockSize)
    {
        long fileOffset = 0;
        long totalBytesRead = 0;
        bool isFirstSlice = true;
        Microsoft.SharePoint.Client.File uploadFile = null;
        Guid uploadId = Guid.NewGuid();

        using (MemoryStream stream = new MemoryStream(fileBytes))
        {
            int bytesRead;
            byte[] buffer = new byte[blockSize];

            // Read and upload file slices
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalBytesRead += bytesRead;
                bool isLastSlice = totalBytesRead == fileBytes.Length;

                using (MemoryStream sliceStream = new MemoryStream(buffer, 0, bytesRead))
                {
                    if (isFirstSlice)
                    {
                        uploadFile = UploadFirstSlice(context, folder, fileName, uploadId, sliceStream);
                        isFirstSlice = false;
                    }
                    else if (isLastSlice)
                    {
                        await FinishUploadAsync(context, folder.ServerRelativeUrl, fileName, uploadId, fileOffset, sliceStream);
                    }
                    else
                    {
                        fileOffset = await UploadSliceAsync(context, folder.ServerRelativeUrl, fileName, uploadId, fileOffset, sliceStream);
                    }
                }
            }
        }
    }

    private Microsoft.SharePoint.Client.File UploadFirstSlice(ClientContext context, Folder folder, string fileName, Guid uploadId, MemoryStream sliceStream)
    {
        FileCreationInformation fileInfo = new FileCreationInformation
        {
            ContentStream = new MemoryStream(), // Add an empty file
            Url = fileName,
            Overwrite = true
        };

        var uploadFile = folder.Files.Add(fileInfo);
        uploadFile.StartUpload(uploadId, sliceStream);
        context.ExecuteQueryRetryAsync();

        return uploadFile;
    }

    private async Task<long> UploadSliceAsync(ClientContext context, string folderUrl, string fileName, Guid uploadId, long fileOffset, MemoryStream sliceStream)
    {
        var uploadFile = context.Web.GetFileByServerRelativeUrl(folderUrl + System.IO.Path.AltDirectorySeparatorChar + fileName);
        var bytesUploaded = uploadFile.ContinueUpload(uploadId, fileOffset, sliceStream);
        await context.ExecuteQueryRetryAsync();

        return bytesUploaded.Value;
    }

    private async Task FinishUploadAsync(ClientContext context, string folderUrl, string fileName, Guid uploadId, long fileOffset, MemoryStream sliceStream)
    {
        var uploadFile = context.Web.GetFileByServerRelativeUrl(folderUrl + System.IO.Path.AltDirectorySeparatorChar + fileName);
        uploadFile.FinishUpload(uploadId, fileOffset, sliceStream);
        await context.ExecuteQueryRetryAsync();
    }

    public string ExtractSiteServerRelativeUrl(string serverRelativeFullPath)
    {
        if (string.IsNullOrWhiteSpace(serverRelativeFullPath))
        {
            throw new ArgumentException("Server relative full path must not be null or empty.");
        }

        int indexOfSites = serverRelativeFullPath.IndexOf("/sites/", StringComparison.OrdinalIgnoreCase);

        if (indexOfSites == -1)
        {
            throw new ArgumentException("Invalid SharePoint server relative full path.");
        }

        int endIndex = serverRelativeFullPath.IndexOf('/', indexOfSites + "/sites/".Length);

        if (endIndex == -1)
        {
            endIndex = serverRelativeFullPath.Length;
        }

        string siteServerRelativeUrl = serverRelativeFullPath.Substring(0, endIndex);

        return siteServerRelativeUrl;
    }



}
