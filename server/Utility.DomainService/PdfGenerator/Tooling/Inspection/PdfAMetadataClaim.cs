namespace Utility.DomainService.PdfGenerator.Tooling.Inspection;

/// <summary>
/// What the document's own XMP metadata says about PDF/A conformance. This is a claim, not a
/// verdict - <see cref="PdfInspectionResult.ClaimedProfile"/> is only ever confirmed or refuted by
/// veraPDF later in the pipeline.
/// </summary>
public enum PdfAMetadataClaim
{
    /// <summary>No XMP metadata, or XMP with no pdfaid:part/pdfaid:conformance entry.</summary>
    Missing,

    /// <summary>A parseable pdfaid:part/pdfaid:conformance pair was found.</summary>
    Claimed,

    /// <summary>XMP metadata is present but is not well-formed XML, or its pdfaid values are not parseable.</summary>
    Malformed
}
