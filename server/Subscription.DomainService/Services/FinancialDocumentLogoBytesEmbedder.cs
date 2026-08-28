using System.Text;

namespace Subscription.DomainService.Services;

/// <summary>
/// Reads and validates logo bytes, independent of where they came from.
/// </summary>
/// <remarks>
/// Split out of <see cref="FinancialDocumentLogoResolver"/> so the part worth testing — the size cap,
/// the signature allow-list, the data URI — can be exercised with a plain <see cref="MemoryStream"/>
/// or a byte array, never a real storage backend. <see cref="FinancialDocumentLogoResolver"/>'s own
/// job shrinks to "get a stream from storage, hand it here, translate the answer into a resolution";
/// nothing about reading or validating bytes safely lives there anymore.
/// </remarks>
public static class FinancialDocumentLogoBytesEmbedder
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> from <paramref name="stream"/>.
    /// </summary>
    /// <returns>
    /// The bytes read, or null if the stream held more than <paramref name="maxBytes"/>.
    /// </returns>
    /// <remarks>
    /// Checked while copying, not after: budgeting the read itself is what keeps an unbounded or
    /// hostile stream from being buffered in full just to be rejected one comparison later.
    /// </remarks>
    public static async Task<byte[]?> ReadCappedAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        var budget = maxBytes + 1;
        var chunk = new byte[81_920];
        int read;

        while (budget > 0 &&
               (read = await stream.ReadAsync(
                   chunk.AsMemory(0, Math.Min(chunk.Length, budget)),
                   cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            budget -= read;
        }

        return budget <= 0 ? null : buffer.ToArray();
    }

    /// <summary>
    /// Validates already-read bytes against a signature allow-list and turns them into a data URI.
    /// </summary>
    /// <remarks>
    /// An SVG can carry a <c>&lt;script&gt;</c>, but it is embedded here only as the source of an
    /// <c>&lt;img&gt;</c> element — the one context in which every browser engine, Chromium included,
    /// refuses to execute scripts or fetch external references from SVG content. That is what makes
    /// accepting SVG at all safe in a renderer that otherwise fetches nothing.
    /// </remarks>
    public static string? TryEmbed(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0)
        {
            return null;
        }

        var mimeType = SniffMimeType(bytes);

        return mimeType is null ? null : $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>PNG and JPEG by their magic bytes; SVG by content, since a vector file has none.</summary>
    private static string? SniffMimeType(byte[] bytes)
    {
        if (bytes.Length >= PngSignature.Length &&
            bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
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
        // Only the leading text matters, and only far enough to see past an XML prolog into the root
        // element — an unrecognised binary format is never going to decode into something matching
        // this shape by accident.
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512)).TrimStart('﻿');
        var trimmed = head.TrimStart();

        return trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }
}
