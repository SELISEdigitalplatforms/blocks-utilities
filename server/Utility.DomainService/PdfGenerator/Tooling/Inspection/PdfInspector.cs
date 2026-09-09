using System.Xml.Linq;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;

namespace Utility.DomainService.PdfGenerator.Tooling.Inspection;

/// <summary>
/// Managed, in-process first look at an ingested file - runs before any external tool is invoked,
/// since most files need no remediation at all.
/// </summary>
public sealed class PdfInspector : IPdfInspector
{
    private static readonly XNamespace PdfAIdNamespace = "http://www.aiim.org/pdfa/ns/id/";

    public PdfInspectionResult Inspect(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        PdfPigDocument? pigDocument;
        try
        {
            pigDocument = PdfPigDocument.Open(pdfBytes);
        }
        catch (Exception ex)
        {
            return Unreadable($"PdfPig: {ex.Message}");
        }

        using (pigDocument)
        {
            PdfSharpDocument sharpDocument;
            try
            {
                using var stream = new MemoryStream(pdfBytes, writable: false);
                sharpDocument = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
            }
            catch (Exception ex)
            {
                return Unreadable($"PdfSharp: {ex.Message}");
            }

            using (sharpDocument)
            {
                var geometryReadable = TryReadGeometry(sharpDocument, out var geometryFailure);
                var (hasSignature, signatureCount) = InspectSignatures(pigDocument);
                var (claim, profile) = InspectPdfAClaim(pigDocument);

                return new PdfInspectionResult
                {
                    Readable = true,
                    GeometryReadable = geometryReadable,
                    PageCount = pigDocument.NumberOfPages,
                    HasSignature = hasSignature,
                    SignatureCount = signatureCount,
                    MetadataClaim = claim,
                    ClaimedProfile = profile,
                    FailureReason = geometryReadable ? null : geometryFailure
                };
            }
        }
    }

    private static PdfInspectionResult Unreadable(string reason)
    {
        return new PdfInspectionResult
        {
            Readable = false,
            GeometryReadable = false,
            FailureReason = reason
        };
    }

    /// <summary>
    /// The only geometry detection this inspector can perform: PdfSharp (unlike PdfSharpCore, which
    /// the e-signature reference this pipeline was ported from relies on) already resolves indirect
    /// page-box references - both directly and inherited from an ancestor /Pages node - before they
    /// ever reach a typed accessor, so there is no structural signal left to inspect. A guarded read
    /// is therefore the whole check, not a backstop for one.
    /// </summary>
    private static bool TryReadGeometry(PdfSharpDocument document, out string? failureReason)
    {
        try
        {
            for (var i = 0; i < document.PageCount; i++)
            {
                var page = document.Pages[i];
                _ = page.MediaBox;
                _ = page.CropBox;
                _ = page.Width;
                _ = page.Height;
            }

            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = $"Geometry: {ex.Message}";
            return false;
        }
    }

    private static (bool HasSignature, int SignatureCount) InspectSignatures(PdfPigDocument document)
    {
        if (!document.TryGetForm(out var form))
        {
            return (false, 0);
        }

        var signatureCount = form.Fields
            .OfType<AcroSignatureField>()
            .Count(field => field.Dictionary.TryGet(NameToken.V, out var value) && value is not NullToken);

        return (signatureCount > 0, signatureCount);
    }

    private static (PdfAMetadataClaim Claim, string? Profile) InspectPdfAClaim(PdfPigDocument document)
    {
        if (!document.TryGetXmpMetadata(out var xmp))
        {
            return (PdfAMetadataClaim.Missing, null);
        }

        XDocument xDocument;
        try
        {
            xDocument = xmp.GetXDocument();
        }
        catch (Exception)
        {
            return (PdfAMetadataClaim.Malformed, null);
        }

        var partValue = xDocument.Descendants(PdfAIdNamespace + "part").FirstOrDefault()?.Value;
        var conformanceValue = xDocument.Descendants(PdfAIdNamespace + "conformance").FirstOrDefault()?.Value;

        if (string.IsNullOrWhiteSpace(partValue) && string.IsNullOrWhiteSpace(conformanceValue))
        {
            return (PdfAMetadataClaim.Missing, null);
        }

        if (string.IsNullOrWhiteSpace(partValue) || !int.TryParse(partValue, out _))
        {
            return (PdfAMetadataClaim.Malformed, null);
        }

        var profile = string.IsNullOrWhiteSpace(conformanceValue)
            ? $"PDF/A-{partValue}"
            : $"PDF/A-{partValue}{conformanceValue.ToUpperInvariant()}";

        return (PdfAMetadataClaim.Claimed, profile);
    }
}
