
using System.Web;
using Microsoft.SharePoint.Client;
using Newtonsoft.Json.Linq;

namespace CorporAIte.Extensions
{
    public static class SharePointExtensions
    {

        public static (string? notebookId, string? teamsId) ExtractOneNote(this string url)
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/');

            string notebookId = null;
            string teamId = null;

            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "notebooks" && i + 1 < segments.Length)
                {
                    notebookId = segments[i + 1];
                }
                else if (segments[i] == "teams" && i + 1 < segments.Length)
                {
                    teamId = segments[i + 1].Split(".").First();
                }
            }

            return (notebookId, teamId);
        }


        public static string ExtractUrlParameter(this string url, string parameterName)
        {
            try
            {
                var uri = new Uri(url);
                var queryParameters = HttpUtility.ParseQueryString(uri.Query);
                var parameterValue = queryParameters[parameterName];

                // Check if the parameter value is a JSON object
                if (parameterValue.StartsWith("{"))  // %7B = {
                {
                    // URL-decode and parse as JSON
                    //  var jsonStr = HttpUtility.UrlDecode(parameterValue);
                    var jsonObj = JObject.Parse(parameterValue);
                    // Extract objectUrl from the JSON object
                    var urlValue = jsonObj["objectUrl"]?.ToString();
                    return urlValue;
                }
                else
                {
                    // Directly return the URL-decoded parameter value
                    return HttpUtility.UrlDecode(parameterValue);
                }
            }
            catch (Exception)
            {
                // Handle any exceptions that occur during parsing, if necessary
                return null;
            }
        }

        public static string ExtractContentSource(this string url)
        {
            var notebookSelfUrl = url.ExtractUrlParameter("notebookSelfUrl");

            if (!string.IsNullOrEmpty(notebookSelfUrl))
            {
                return notebookSelfUrl;
            }

            var objectUrl = url.ExtractUrlParameter("subEntityId");

            if (!string.IsNullOrEmpty(objectUrl))
            {
                return objectUrl;
            }

            return null;

        }

        public static async Task UploadLargeFileAsync(this ClientContext context, Folder folder, byte[] fileBytes, string fileName, int blockSize, string folderUrl)
        {
            if (fileBytes == null || fileBytes.Length == 0) throw new ArgumentNullException(nameof(fileBytes));
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));
            if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));

            long fileOffset = 0;
            bool isFirstSlice = true;
            Microsoft.SharePoint.Client.File uploadFile = null;
            Guid uploadId = Guid.NewGuid();

            using (var stream = new MemoryStream(fileBytes))
            {
                byte[] buffer = new byte[blockSize];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    long totalBytesRead = stream.Position;
                    bool isLastSlice = totalBytesRead == fileBytes.Length;

                    using (var sliceStream = new MemoryStream(buffer, 0, bytesRead))
                    {
                        try
                        {
                            if (isFirstSlice)
                            {
                                uploadFile = await context.UploadFirstSliceAsync(folder, fileName, uploadId, sliceStream).ConfigureAwait(false);
                                isFirstSlice = false;
                            }
                            else if (isLastSlice)
                            {
                                await context.FinishUploadAsync(folderUrl, fileName, uploadId, fileOffset, sliceStream).ConfigureAwait(false);
                            }
                            else
                            {
                                fileOffset = await context.UploadSliceAsync(folderUrl, fileName, uploadId, fileOffset, sliceStream).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log the exception or re-throw
                            throw;
                        }
                    }
                }
            }
        }

        public static async Task<Microsoft.SharePoint.Client.File> UploadFirstSliceAsync(this ClientContext context, Folder folder, string fileName, Guid uploadId, MemoryStream sliceStream)
        {
            FileCreationInformation fileInfo = new FileCreationInformation
            {
                ContentStream = new MemoryStream(), // Add an empty file
                Url = fileName,
                Overwrite = true
            };

            var uploadFile = folder.Files.Add(fileInfo);
            uploadFile.StartUpload(uploadId, sliceStream);
            await context.ExecuteQueryRetryAsync().ConfigureAwait(false);

            return uploadFile;
        }

        public static async Task<long> UploadSliceAsync(this ClientContext context, string folderUrl, string fileName, Guid uploadId, long fileOffset, MemoryStream sliceStream)
        {
            var uploadFile = context.Web.GetFileByServerRelativeUrl(folderUrl + System.IO.Path.AltDirectorySeparatorChar + fileName);
            var bytesUploaded = uploadFile.ContinueUpload(uploadId, fileOffset, sliceStream);
            await context.ExecuteQueryRetryAsync().ConfigureAwait(false);

            return bytesUploaded.Value;
        }

        public static async Task FinishUploadAsync(this ClientContext context, string folderUrl, string fileName, Guid uploadId, long fileOffset, MemoryStream sliceStream)
        {
            var uploadFile = context.Web.GetFileByServerRelativeUrl(folderUrl + System.IO.Path.AltDirectorySeparatorChar + fileName);
            uploadFile.FinishUpload(uploadId, fileOffset, sliceStream);
            await context.ExecuteQueryRetryAsync().ConfigureAwait(false);
        }

        public static async Task UploadSmallFileAsync(this ClientContext context, Folder folder, byte[] fileBytes, string fileName)
        {
            FileCreationInformation fileInfo = new FileCreationInformation
            {
                Content = fileBytes,
                Url = fileName,
                Overwrite = true
            };

            folder.Files.Add(fileInfo);
            await context.ExecuteQueryRetryAsync().ConfigureAwait(false);
        }

        public static async Task RetrieveSupportedFilesRecursively(this ClientContext clientContext, Folder folder, List<(string, DateTime)> supportedFiles, List<string> supportedExtensions, bool includeSubfolders = true)
        {
            clientContext.Load(folder, a => a.Files, a => a.Folders);
            await clientContext.ExecuteQueryRetryAsync().ConfigureAwait(false);

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
                    await clientContext.RetrieveSupportedFilesRecursively(subfolder, supportedFiles, supportedExtensions, includeSubfolders);
                }
            }
        }

        public static async Task<List<(string ServerRelativeUrl, DateTime LastModified)>> GetSupportedFilesInFolderAsync(this ClientContext clientContext, string folderPath, List<string> supportedExtensions)
        {
            var web = clientContext.Web;
            var folder = web.GetFolderByServerRelativeUrl(folderPath);
            var supportedFiles = new List<(string, DateTime)>();

            await clientContext.RetrieveSupportedFilesRecursively(folder, supportedFiles, supportedExtensions);

            return supportedFiles.Where(a => !a.Item1.StartsWith(folderPath + "/Forms/")).ToList();
        }

        /// <summary>
        /// Retrieves file information asynchronously.
        /// </summary>
        public static async Task<(string ServerRelativeUrl, DateTime LastModified)?> GetFileInfoAsync(this ClientContext ctx, string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

            var file = ctx.Web.GetFileByServerRelativeUrl(filePath);
            ctx.Load(file, f => f.ServerRelativeUrl, f => f.TimeLastModified);
            await ctx.ExecuteQueryAsync().ConfigureAwait(false);

            return file.ServerObjectIsNull == false ? (file.ServerRelativeUrl, file.TimeLastModified) : null;
        }


        public static async Task<AttachmentCollection> GetAttachmentsFromListItem(this ClientContext context, string siteUrl, string listTitle, int itemId)
        {
            var web = context.Web;
            var list = web.Lists.GetByTitle(listTitle);
            var listItem = list.GetItemById(itemId);

            // Load the ListItem's attachments
            context.Load(listItem, item => item.AttachmentFiles);
            await context.ExecuteQueryRetryAsync().ConfigureAwait(false);

            return listItem.AttachmentFiles;
        }

        public static async Task<IEnumerable<ListItem>> GetListItemsFromList(this ClientContext context, string siteUrl, string listTitle, string caml)
        {
            var web = context.Web;
            var list = web.Lists.GetByTitle(listTitle);
            var listItems = list.GetItems(new CamlQuery()
            {
                ViewXml = caml
            });

            // Load the ListItem data
            context.Load(listItems);
            await context.ExecuteQueryRetryAsync().ConfigureAwait(false);

            return listItems;

        }


        public static async Task<ListItem> GetListItemFromList(this ClientContext context, string siteUrl, string listTitle, int itemId)
        {
            var web = context.Web;
            var list = web.Lists.GetByTitle(listTitle);
            var listItem = list.GetItemById(itemId);

            context.Load(listItem);
            await context.ExecuteQueryRetryAsync().ConfigureAwait(false);

            return listItem;
        }

        public static string RemoveBaseUrl(this string fullUrl)
        {
            var uri = new Uri(fullUrl);
            return uri.AbsolutePath;
        }

        public static string GetSiteUrlFromFullUrl(this string fullUrl)
        {
            var uri = new Uri(fullUrl);

            // Split the path by '/'
            var pathSegments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            // The site URL is typically the first three segments of the path
            var sitePath = string.Join("/", pathSegments.Take(2));

            // Combine the scheme, host, and site path to get the site URL
            var siteUrl = $"{uri.Scheme}://{uri.Host}/{sitePath}";

            return siteUrl;
        }



    }
}