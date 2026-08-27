using System.Text;
using Microsoft.Extensions.Logging;
using Utility.DomainService.PdfGenerator.service;

namespace Subscription.DomainService.Services;

/// <summary>
/// Reads a snapshotted logo from storage and turns it into a data URI, or explains why it could not.
/// </summary>
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

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

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

        byte[] bytes;

        try
        {
            var stream = await _storage.GetImageStream(logoFileId);

            if (stream is null)
            {
                _logger.LogWarning(
                    "Financial document logo is unavailable in storage, falling back to text " +
                    "LogoFileId={LogoFileId} WarningCode={WarningCode}",
                    logoFileId,
                    "document_logo_unavailable");

                return FinancialDocumentLogoResolution.Warning("document_logo_unavailable");
            }

            await using (stream)
            {
                using var buffer = new MemoryStream();

                // Capped while copying, not after: a stream that never stops would otherwise be read
                // to exhaustion before the length check ever ran.
                var copyBudget = MaxLogoBytes + 1;
                var chunk = new byte[81_920];
                int read;

                while (copyBudget > 0 &&
                       (read = await stream.ReadAsync(
                           chunk.AsMemory(0, Math.Min(chunk.Length, copyBudget)),
                           cancellationToken)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    copyBudget -= read;
                }

                if (copyBudget <= 0)
                {
                    _logger.LogWarning(
                        "Financial document logo exceeds the {MaxBytes} byte limit, falling back to " +
                        "text LogoFileId={LogoFileId} WarningCode={WarningCode}",
                        MaxLogoBytes,
                        logoFileId,
                        "document_logo_unavailable");

                    return FinancialDocumentLogoResolution.Warning("document_logo_unavailable");
                }

                bytes = buffer.ToArray();
            }
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
                "document_logo_unavailable");

            return FinancialDocumentLogoResolution.Warning("document_logo_unavailable");
        }

        var mimeType = SniffMimeType(bytes);

        if (mimeType is null)
        {
            _logger.LogWarning(
                "Financial document logo is not a supported image format, falling back to text " +
                "LogoFileId={LogoFileId} WarningCode={WarningCode}",
                logoFileId,
                "document_logo_unavailable");

            return FinancialDocumentLogoResolution.Warning("document_logo_unavailable");
        }

        return FinancialDocumentLogoResolution.Embedded(
            $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}");
    }

    /// <summary>
    /// PNG and JPEG by their magic bytes; SVG by content, since a vector file has none.
    /// </summary>
    /// <remarks>
    /// An SVG can carry a <c>&lt;script&gt;</c>, but it is embedded here only as the source of an
    /// <c>&lt;img&gt;</c> element -- the one context in which every browser engine, Chromium
    /// included, refuses to execute scripts or fetch external references from SVG content. That is
    /// what makes accepting SVG at all safe in a renderer that otherwise fetches nothing.
    /// </remarks>
    private static string? SniffMimeType(byte[] bytes)
    {
        if (bytes.Length >= PngSignature.Length && bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return "image/png";
        }

        if (bytes.Length >= JpegSignature.Length &&
            bytes.AsSpan(0, JpegSignature.Length).SequenceEqual(JpegSignature))
        {
            return "image/jpeg";
        }

        if (LooksLikeSvg(bytes))
        {
            return "image/svg+xml";
        }

        return null;
    }

    private static bool LooksLikeSvg(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return false;
        }

        // Only the leading text matters, and only far enough to see past an XML prolog into the root
        // element -- an unrecognised binary format is never going to decode into something matching
        // this shape by accident.
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512)).TrimStart('﻿');
        var trimmed = head.TrimStart();

        return trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }
}
