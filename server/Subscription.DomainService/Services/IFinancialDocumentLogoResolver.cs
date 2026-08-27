namespace Subscription.DomainService.Services;

/// <summary>
/// Turns a snapshotted logo file id into something safe to embed in a rendered document.
/// </summary>
/// <remarks>
/// The one place a document's own storage is read during rendering, and deliberately narrow: it
/// reads bytes, checks a signature against a short allow-list and a size limit, and returns either
/// a self-contained data URI or nothing. Nothing it returns is a URL, a stream held open across the
/// render, or a promise to fetch again later -- the template stays exactly as self-contained with a
/// logo as it was without one.
/// </remarks>
public interface IFinancialDocumentLogoResolver
{
    /// <summary>
    /// Resolves a logo, or explains why there is none.
    /// </summary>
    /// <remarks>
    /// Never throws for a missing, deleted, oversized or unsupported file -- those are the ordinary,
    /// expected outcomes of a file that was fine when it was snapshotted and is not fine now, and a
    /// financial document must still be produced. A null <paramref name="logoFileId"/> is not a
    /// failure at all: it is a merchant that never uploaded one, and is reported with no warning.
    /// </remarks>
    Task<FinancialDocumentLogoResolution> ResolveAsync(
        string? logoFileId,
        CancellationToken cancellationToken);
}

/// <summary>
/// What came of trying to embed a logo: the data URI, or the reason there isn't one.
/// </summary>
public sealed record FinancialDocumentLogoResolution
{
    /// <summary>A <c>data:</c> URI ready to embed in an <c>&lt;img&gt;</c> tag, or null.</summary>
    public string? DataUri { get; init; }

    /// <summary>
    /// A structured reason code, set exactly when <see cref="DataUri"/> is null for a logo that was
    /// actually named -- never for a merchant with no logo at all, which is not a warning.
    /// </summary>
    public string? WarningCode { get; init; }

    public static readonly FinancialDocumentLogoResolution None = new();

    public static FinancialDocumentLogoResolution Embedded(string dataUri) =>
        new() { DataUri = dataUri };

    public static FinancialDocumentLogoResolution Warning(string code) =>
        new() { WarningCode = code };
}
