using Aspose.Words.Loading;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Makes Aspose.Words load a document's external images and style sheets through
    /// <see cref="PublicNetwork.FetchAsync"/> instead of fetching them itself.
    /// </summary>
    /// <remarks>
    /// Documents come from callers. Left to itself, Aspose follows any URL or path in them, so a
    /// document could pull the cloud metadata endpoint, a cluster-internal service or a local file
    /// into the rendered PDF (SSRF). Anything that is not an http(s) resource on a public host is
    /// skipped; inline <c>data:</c> URIs are left to Aspose.
    /// </remarks>
    public sealed class PublicResourceLoadingCallback : IResourceLoadingCallback
    {
        public static readonly PublicResourceLoadingCallback Instance = new();

        private PublicResourceLoadingCallback()
        {
        }

        public ResourceLoadingAction ResourceLoading(ResourceLoadingArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);

            if (args.OriginalUri?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true)
            {
                return ResourceLoadingAction.Default;
            }

            // Aspose's callback is synchronous; it already runs on a worker thread
            var resource = PublicNetwork.FetchAsync(args.Uri, CancellationToken.None).GetAwaiter().GetResult();
            if (resource == null)
            {
                return ResourceLoadingAction.Skip;
            }

            args.SetData(resource.Body);
            return ResourceLoadingAction.UserProvided;
        }
    }
}
