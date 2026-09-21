using Microsoft.Extensions.Logging;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.Storage;

namespace Subscription.DomainService.Services;

/// <summary>
/// Renders documents with the platform's headless-browser engine.
/// </summary>
/// <remarks>
/// The one HTML-to-PDF engine in the platform that is both free and handles modern CSS, which the
/// document template relies on for its flex layout. Held as the concrete type rather than resolved
/// through <c>IPdfEngineProvider</c> by number, so a change to the provider's numbering cannot
/// silently start rendering invoices with an engine that has no HTML support at all.
/// </remarks>
public sealed class PuppeteerFinancialDocumentPdfRenderer : IFinancialDocumentPdfRenderer
{
    private readonly PuppeteerSharpEngine _engine;
    private readonly ILogger<PuppeteerFinancialDocumentPdfRenderer> _logger;

    public PuppeteerFinancialDocumentPdfRenderer(
        PuppeteerSharpEngine engine,
        ILogger<PuppeteerFinancialDocumentPdfRenderer> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task<byte[]?> RenderAsync(string html, CancellationToken cancellationToken)
    {
        // No header, footer or page numbering: the template is one self-contained page of its own
        // design, and asking the engine to add furniture would put it outside the bytes we hashed.
        var stream = await _engine.ConvertHtmlToPdfAsync(html, new PdfGenerationOptions());

        if (stream is null)
        {
            _logger.LogWarning("A financial document could not be rendered to PDF");

            return null;
        }

        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            return buffer.ToArray();
        }
    }
}

/// <summary>
/// Stores document PDFs through the platform's storage driver.
/// </summary>
/// <remarks>
/// Uses the same helper the PDF module's own consumers use, so invoices land in the same storage with
/// the same credentials and the same lifecycle as every other generated document — rather than this
/// module inventing a second place for files to live.
/// </remarks>
public sealed class StorageDriverFinancialDocumentFileStore : IFinancialDocumentFileStore
{
    /// <summary>
    /// Where document PDFs live. Its own directory so retention and access can be set for financial
    /// records without touching everything else the PDF module generates.
    /// </summary>
    private const string Directory = "Blocks-Subscription-Financial-Documents";

    /// <summary>
    /// The storage principal that owns every document PDF. Files are created
    /// <see cref="CreatorOnly"/> under it, so only this service can read them back through the storage
    /// driver: invoices are not browsable or downloadable by other tenant users through the storage
    /// API, and a subscriber's download goes through this service's own authorization first.
    /// </summary>
    /// <remarks>
    /// A fixed id rather than whoever is in context, because that differs between the write (the
    /// user-less worker) and the read (a subscriber's request) -- see
    /// <see cref="Utility.DomainService.Storage.StorageServiceIdentity"/>. Changing it orphans every
    /// document written under the old one: they stay readable only by that id.
    /// </remarks>
    public const string StoragePrincipal = "blocks-utilities-financial-documents";

    private const string CreatorOnly = "Creator";

    private readonly PdfStorageHelper _storage;

    public StorageDriverFinancialDocumentFileStore(PdfStorageHelper storage) => _storage = storage;

    public async Task<bool> SaveAsync(
        string storageId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var stream = new MemoryStream(content, writable: false);
        using var identity = StorageServiceIdentity.Enter(StoragePrincipal);

        return await _storage.SavePdfToStorage(
            stream,
            storageId,
            fileName,
            parentDirectoryId: Directory,
            objectAccessLevel: CreatorOnly);
    }

    public async Task<byte[]?> ReadAsync(string storageId, CancellationToken cancellationToken)
    {
        // Read as the owner. Documents written before this principal existed carry no access level,
        // so the driver's default rule still lets it read them too.
        using var identity = StorageServiceIdentity.Enter(StoragePrincipal);

        var stream = await _storage.GetPdfStream(storageId);
        if (stream is null)
        {
            return null;
        }

        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            return buffer.ToArray();
        }
    }
}
