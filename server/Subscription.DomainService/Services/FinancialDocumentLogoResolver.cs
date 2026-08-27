using Microsoft.Extensions.Logging;
using Utility.DomainService.PdfGenerator.service;

namespace Subscription.DomainService.Services;

/// <summary>
/// Reads a snapshotted logo from storage and turns it into a data URI, or explains why it could not.
/// </summary>
/// <remarks>
/// Deliberately thin: fetching is the only thing here that needs storage, and it is wrapped in one
/// try/catch that turns every failure mode — missing, unreachable, unreadable — into the same
/// non-blocking warning. Reading the bytes safely and validating them is
/// <see cref="FinancialDocumentLogoBytesEmbedder"/>'s job, split out precisely so that logic can be
/// tested without a storage backend at all.
/// </remarks>
public sealed class FinancialDocumentLogoResolver : IFinancialDocumentLogoResolver
{
    /// <summary>
    /// The largest logo this will embed.
    /// </summary>
    /// <remarks>
    /// Base64 costs a third again in size, and the bytes go straight into the HTML string handed to
    /// the renderer -- there is no streaming path for an inline image. Half a megabyte is generous
    /// for a letterhead mark and small enough that a document stays a document rather than becoming
    /// a container for whatever somebody uploaded.
    /// </remarks>
    public const int MaxLogoBytes = 512 * 1024;

    private const string UnavailableWarningCode = "document_logo_unavailable";

    private readonly PdfStorageHelper _storage;
    private readonly ILogger<FinancialDocumentLogoResolver> _logger;

    public FinancialDocumentLogoResolver(
        PdfStorageHelper storage,
        ILogger<FinancialDocumentLogoResolver> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<FinancialDocumentLogoResolution> ResolveAsync(
        string? logoFileId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logoFileId))
        {
            // No logo was ever uploaded. Not a failure of anything -- most merchants render from
            // their name alone, by design, and that is not worth a warning line every time.
            return FinancialDocumentLogoResolution.None;
        }

        try
        {
            var stream = await _storage.GetImageStream(logoFileId).ConfigureAwait(false);

            if (stream is null)
            {
                return Fallback(logoFileId, reason: "the file could not be found in storage");
            }

            byte[]? bytes;

            await using (stream.ConfigureAwait(false))
            {
                bytes = await FinancialDocumentLogoBytesEmbedder
                    .ReadCappedAsync(stream, MaxLogoBytes, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (bytes is null)
            {
                return Fallback(
                    logoFileId, reason: $"it exceeds the {MaxLogoBytes} byte limit");
            }

            var dataUri = FinancialDocumentLogoBytesEmbedder.TryEmbed(bytes);

            return dataUri is null
                ? Fallback(logoFileId, reason: "it is not a supported image format")
                : FinancialDocumentLogoResolution.Embedded(dataUri);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Storage being unreachable is exactly as recoverable, from the document's point of
            // view, as the file never having existed: render from the name and say why in the log,
            // never let a branding asset take the whole document down with it.
            _logger.LogWarning(
                exception,
                "Financial document logo could not be read, falling back to text " +
                "LogoFileId={LogoFileId} WarningCode={WarningCode}",
                logoFileId,
                UnavailableWarningCode);

            return FinancialDocumentLogoResolution.Warning(UnavailableWarningCode);
        }
    }

    private FinancialDocumentLogoResolution Fallback(string logoFileId, string reason)
    {
        _logger.LogWarning(
            "Financial document logo could not be embedded because {Reason}, falling back to " +
            "text LogoFileId={LogoFileId} WarningCode={WarningCode}",
            reason,
            logoFileId,
            UnavailableWarningCode);

        return FinancialDocumentLogoResolution.Warning(UnavailableWarningCode);
    }
}
