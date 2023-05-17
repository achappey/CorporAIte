
using Microsoft.SharePoint.Client;

namespace CorporAIte.Extensions
{
    public static class SharePointExtensions
    {


        public static async Task RetrieveSupportedFilesRecursively(this ClientContext clientContext, Folder folder, List<(string, DateTime)> supportedFiles, List<string> supportedExtensions, bool includeSubfolders = true)
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

        public static async Task<(string ServerRelativeUrl, DateTime LastModified)?> GetFileInfoAsync(this ClientContext ctx, string filePath)
        {
            // Assuming you have already set up the SharePoint ClientContext
            var file = ctx.Web.GetFileByServerRelativeUrl(filePath);
            ctx.Load(file, f => f.ServerRelativeUrl, f => f.TimeLastModified);
            await ctx.ExecuteQueryAsync();

            if (file != null)
            {
                return (file.ServerRelativeUrl, file.TimeLastModified);
            }

            return null;
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