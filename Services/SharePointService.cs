
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
        var cacheKey = "GetSharePointPageTextAsync" + pageUrl;

        List<string> pageParagraphs = _cacheService.Get<List<string>>(cacheKey);

        if (pageParagraphs == null)
        {
            pageParagraphs = new List<string>();

            using (var context = GetContext(siteUrl))
            {
                var file = context.Web.GetFileByServerRelativeUrl(pageUrl);
                context.Load(file, f => f.ListItemAllFields);
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

            return supportedFiles;
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

        /*      if (includeSubfolders)
              {
                  foreach (var subfolder in folder.Folders)
                  {
                      await RetrieveSupportedFilesRecursively(clientContext, subfolder, supportedFiles, supportedExtensions, includeSubfolders);
                  }
              }*/
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
                    file.Name.StartsWith(startsWith) &&
                    Path.GetExtension(file.Name).ToLowerInvariant() == extension).ToList();

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
        int blockSize = 2 * 1024 * 1024;
        //  int blockSize = 2097152;

        // Create a client context object for the SharePoint site
        using (var context = GetContext(siteUrl))
        {
            // Get the folder object in which to upload the file
            var folder = context.Web.GetFolderByServerRelativeUrl(folderUrl);

            if (fileBytes.Length < 2097152)
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
            else
            {

                byte[] buffer = new byte[blockSize];
                long fileoffset = 0;
                long totalBytesRead = 0;
                int bytesRead;
                byte[] lastBuffer = null;
                bool first = true;
                bool last = false;
                Microsoft.SharePoint.Client.File uploadFile = null;
                Guid uploadId = Guid.NewGuid();

                using (MemoryStream stream = new MemoryStream(fileBytes))
                {
                    // Create a new file object and set its properties
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        totalBytesRead = totalBytesRead + bytesRead;

                        if (first)
                        {
                            using (MemoryStream contentStream = new MemoryStream())
                            {
                                // Add an empty file.
                                FileCreationInformation fileInfo = new FileCreationInformation();
                                fileInfo.ContentStream = contentStream;
                                fileInfo.Url = fileName;
                                fileInfo.Overwrite = true;
                                uploadFile = folder.Files.Add(fileInfo);

                                // Start upload by uploading the first slice.
                                using (MemoryStream s = new MemoryStream(buffer))
                                {
                                    // Call the start upload method on the first slice
                                    var bytesUploaded = uploadFile.StartUpload(uploadId, s);
                                    await context.ExecuteQueryRetryAsync();
                                    // fileoffset is the pointer where the next slice will be added
                                    fileoffset = bytesUploaded.Value;
                                }

                                // we can only start the upload once
                                first = false;
                            }
                        }
                        else
                        {
                            uploadFile = context.Web.GetFileByServerRelativeUrl(folderUrl + System.IO.Path.AltDirectorySeparatorChar + fileName);

                            using (MemoryStream s = new MemoryStream(buffer))
                            {

                                if (totalBytesRead == fileBytes.Length)
                                {
                                    last = true;
                                    lastBuffer = new byte[bytesRead];
                                    Array.Copy(buffer, 0, lastBuffer, 0, bytesRead);
                                }

                                if (last)
                                {
                                    uploadFile = uploadFile.FinishUpload(uploadId, fileoffset, new MemoryStream(lastBuffer));
                                    await context.ExecuteQueryRetryAsync();

                                    return;
                                }
                                else
                                {
                                    var bytesUploaded = uploadFile.ContinueUpload(uploadId, fileoffset, s);
                                    await context.ExecuteQueryRetryAsync();
                                    fileoffset = bytesUploaded.Value;
                                }


                            }
                        }

                    }
                }
            }

        }
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
