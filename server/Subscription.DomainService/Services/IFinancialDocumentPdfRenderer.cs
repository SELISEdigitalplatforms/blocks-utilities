namespace Subscription.DomainService.Services;

/// <summary>
/// Turns the document's HTML into PDF bytes.
/// </summary>
/// <remarks>
/// A port, so the delivery service can be tested without starting a browser — and so the engine
/// behind it can be replaced without the document code knowing. The platform's PDF module offers
/// four engines with different licences and capabilities, and which one renders invoices is an
/// operational decision, not a billing one.
/// </remarks>
public interface IFinancialDocumentPdfRenderer
{
    /// <returns>Null when the engine could not produce a document. The caller retries.</returns>
    Task<byte[]?> RenderAsync(string html, CancellationToken cancellationToken);
}

/// <summary>
/// Stores and reads back a document's rendered PDF.
/// </summary>
/// <remarks>
/// Deliberately narrow: put bytes under an id, get bytes back by id. No listing, no deletion, no
/// metadata queries. An issued document's PDF is written once and read many times, and the storage
/// contract should not offer operations that would break that.
/// </remarks>
public interface IFinancialDocumentFileStore
{
    Task<bool> SaveAsync(
        string storageId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken);

    Task<byte[]?> ReadAsync(string storageId, CancellationToken cancellationToken);
}
