
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

    private static Dictionary<string, (List<byte[]>, DateTime)> _vectorCache = new Dictionary<string, (List<byte[]>, DateTime)>();
    private static readonly object _verctorCacheLock = new object();

    public SharePointService(string tenantName, string clientId, string clientSecret)
    {
        this._tenantName = tenantName;
        this._clientId = clientId;
        this._clientSecret = clientSecret;
    }

    private string BaseUrl
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

    public async Task<byte[]> DownloadFileFromSharePointAsync(string siteUrl, string filePath)
    {
        var cacheKey = siteUrl + filePath;

        (byte[], DateTime) cachedValue;
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out cachedValue))
            {
                if (DateTime.UtcNow - cachedValue.Item2 < CACHE_EXPIRATION_TIME)
                {
                    // Item found in cache and is not expired, return it
                    return cachedValue.Item1;
                }
                else
                {
                    // Item found in cache but has expired, remove it
                    _cache.Remove(cacheKey);
                }
            }
        }

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
            byte[] fileBytes;

            var fileStream = file.OpenBinaryStream();

            await context.ExecuteQueryRetryAsync();

            using (var memoryStream = new MemoryStream())
            {
                await fileStream.Value.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            // Add the file bytes to the cache
            lock (_cacheLock)
            {
                if (_cache.Count >= MAX_CACHE_SIZE)
                {
                    // Cache is full, remove the least recently used item
                    var lruItem = _cache.OrderBy(x => x.Value.Item2).First();
                    _cache.Remove(lruItem.Key);
                }

                _cache[cacheKey] = (fileBytes, DateTime.UtcNow);
            }

            return fileBytes;
        }
    }

    public async Task<List<string>> GetFilesByItemIdsFromFolder(string siteUrl, string folderUrl, List<int> itemIds)
    {
        List<string> fileNames = new List<string>();

        using (var clientContext = GetContext(siteUrl))
        {
            var web = clientContext.Web;
            var folder = web.GetFolderByServerRelativeUrl(folderUrl);
            clientContext.Load(folder, a => a.Name);
            await clientContext.ExecuteQueryRetryAsync();
            var list = web.Lists.GetByTitle(folder.Name);

            var camlQuery = new CamlQuery
            {
                ViewXml = $@"<View>
                                    <Query>
                                        <Where>
                                            <In>
                                                <FieldRef Name='ID' />
                                                <Values>
                                                    {string.Join("", itemIds.ConvertAll(id => $"<Value Type='Number'>{id}</Value>"))}
                                                </Values>
                                            </In>
                                        </Where>
                                    </Query>
                                  </View>"
            };

            var filteredFiles = list.GetItems(camlQuery);
            clientContext.Load(filteredFiles);
            await clientContext.ExecuteQueryRetryAsync();

            foreach (var item in filteredFiles)
            {
                string fileName = item["FileLeafRef"].ToString();
                fileNames.Add(fileName);
            }
        }

        return fileNames;

    }

    public async Task<List<byte[]>> GetFilesByExtensionFromFolder(string siteUrl, string folderUrl, string extension, string startsWith = "")
    {
        string cacheKey = $"GetFilesByExtensionFromFolder:{siteUrl}:{folderUrl}:{extension}:{startsWith}";

        (List<byte[]>, DateTime) cachedValue;
        lock (_verctorCacheLock)
        {
            if (_vectorCache.TryGetValue(cacheKey, out cachedValue))
            {
                if (DateTime.UtcNow - cachedValue.Item2 < CACHE_EXPIRATION_TIME)
                {
                    // Item found in cache and is not expired, return it
                    return cachedValue.Item1;
                }
                else
                {
                    // Item found in cache but has expired, remove it
                    _cache.Remove(cacheKey);
                }
            }
        }

        List<byte[]> byteArrays = new List<byte[]>();

        using (var clientContext = GetContext(siteUrl))
        {
            var web = clientContext.Web;
            var folder = web.GetFolderByServerRelativeUrl(folderUrl);
            clientContext.Load(folder, a => a.Name);
            await clientContext.ExecuteQueryRetryAsync();

            var list = web.Lists.GetByTitle(folder.Name);

            var camlQuery = new CamlQuery
            {
                ViewXml = $@"<View Scope='RecursiveAll'>
                        <Query>
                            <Where>
                                <And>
                                <BeginsWith>
                                        <FieldRef Name='FileLeafRef'/>
                                        <Value Type='File'>{startsWith}</Value>
                                    </BeginsWith>
                                    <Eq>
                                        <FieldRef Name='File_x0020_Type'/>
                                        <Value Type='Text'>{extension}</Value>
                                    </Eq>
                                </And>
                            </Where>
                        </Query>
                     </View>",
                FolderServerRelativeUrl = folderUrl
            };

            var filteredFiles = list.GetItems(camlQuery);
            clientContext.Load(filteredFiles, items => items.Include(item => item.File));
            await clientContext.ExecuteQueryRetryAsync();

            foreach (var item in filteredFiles)
            {
                var file = item.File;
                var fileStream = file.OpenBinaryStream();
                await clientContext.ExecuteQueryRetryAsync();

                using (var memoryStream = new MemoryStream())
                {
                    fileStream.Value.CopyTo(memoryStream);
                    byteArrays.Add(memoryStream.ToArray());
                }
            }
        }

        // Add the file bytes to the cache
        lock (_verctorCacheLock)
        {
            if (_vectorCache.Count >= MAX_CACHE_SIZE)
            {
                // Cache is full, remove the least recently used item
                var lruItem = _cache.OrderBy(x => x.Value.Item2).First();
                _cache.Remove(lruItem.Key);
            }

            _vectorCache[cacheKey] = (byteArrays, DateTime.UtcNow);
        }

        return byteArrays;
    }

    public async Task UploadFileToSharePointAsync(byte[] fileBytes, string siteUrl, string folderUrl, string fileName)
    {

        int blockSize = 2097152;

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


}
