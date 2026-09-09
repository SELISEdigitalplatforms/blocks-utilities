using System.Text;
using FluentAssertions;
using Utility.DomainService.PdfGenerator.Tooling.Inspection;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Guards the per-file verdict a caller polls for: a wrong readable/geometry/signature/PDF-A
/// classification here means blocks-utilities either skips remediation a file actually needed, or
/// runs a remediation stage (qpdf/PDFBox/Ghostscript) against a file that never needed one.
/// </remarks>
public sealed class PdfInspectorTests
{
    private readonly PdfInspector _inspector = new();

    [Fact]
    public void A_pdf_produced_by_pdfsharp_reads_as_fully_healthy()
    {
        using var document = new PdfSharp.Pdf.PdfDocument();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);

        var result = _inspector.Inspect(stream.ToArray());

        result.Readable.Should().BeTrue(because: "PdfSharp writes documents PdfPig and PdfSharp can both re-open");
        result.GeometryReadable.Should().BeTrue();
        result.PageCount.Should().Be(1);
        result.HasSignature.Should().BeFalse();
        result.MetadataClaim.Should().Be(PdfAMetadataClaim.Missing);
    }

    [Fact]
    public void Garbage_bytes_are_reported_as_unreadable_with_a_reason()
    {
        var bytes = Encoding.ASCII.GetBytes("this is not a pdf at all, just garbage bytes");

        var result = _inspector.Inspect(bytes);

        result.Readable.Should().BeFalse();
        result.GeometryReadable.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrWhiteSpace(because: "a caller needs to know why, not just that it failed");
    }

    [Fact]
    public void An_indirect_mediabox_reference_still_reads_fine_under_pdfsharp()
    {
        // This is the exact failure mode the PDFBox geometry-normalize tool exists to fix on
        // PdfSharpCore (the e-signature reference this pipeline was ported from): a producer may
        // legally emit /MediaBox as an indirect reference, and PdfSharpCore throws
        // InvalidCastException reading it. The PdfSharp (not Core) package this repo actually
        // carries already resolves indirect page-box references - and inheritance from an
        // ancestor /Pages node - transparently. This test pins that behavior down: if a future
        // PdfSharp upgrade or downgrade regresses it, GeometryReadable flipping to false here is
        // the signal, not a customer-reported broken stamp placement.
        var pdf = RawPdfFixtures.BuildPdf(
            rootObjectNumber: 1,
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox 4 0 R /Resources << >> >>",
            "[0 0 200 300]");

        var result = _inspector.Inspect(pdf);

        result.Readable.Should().BeTrue();
        result.GeometryReadable.Should().BeTrue();
    }

    [Fact]
    public void A_signature_field_with_a_value_is_detected()
    {
        var pdf = RawPdfFixtures.BuildPdf(
            rootObjectNumber: 1,
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Annots [5 0 R] /Resources << >> >>",
            "<< /Fields [5 0 R] /SigFlags 3 >>",
            "<< /FT /Sig /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /T (Signature1) /V 6 0 R /P 3 0 R >>",
            "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 1 2 3] /Contents <00> >>");

        var result = _inspector.Inspect(pdf);

        result.HasSignature.Should().BeTrue();
        result.SignatureCount.Should().Be(1);
    }

    [Fact]
    public void Xmp_metadata_with_a_parseable_pdfaid_part_and_conformance_is_claimed()
    {
        var pdf = RawPdfFixtures.BuildPdf(
            rootObjectNumber: 1,
            "<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Resources << >> >>",
            RawPdfFixtures.MetadataStream(RawPdfFixtures.PdfAXmpPacket(part: "2", conformance: "B")));

        var result = _inspector.Inspect(pdf);

        result.MetadataClaim.Should().Be(PdfAMetadataClaim.Claimed);
        result.ClaimedProfile.Should().Be("PDF/A-2B");
    }

    [Fact]
    public void Xmp_metadata_with_an_unparseable_pdfaid_part_is_malformed_not_missing()
    {
        var pdf = RawPdfFixtures.BuildPdf(
            rootObjectNumber: 1,
            "<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Resources << >> >>",
            RawPdfFixtures.MetadataStream(RawPdfFixtures.PdfAXmpPacket(part: "notanumber", conformance: null)));

        var result = _inspector.Inspect(pdf);

        result.MetadataClaim.Should().Be(
            PdfAMetadataClaim.Malformed,
            because: "a document that names a pdfaid:part it cannot parse is claiming PDF/A badly, not declining to claim it");
    }

    [Fact]
    public void A_document_with_no_xmp_metadata_at_all_reports_missing()
    {
        using var document = new PdfSharp.Pdf.PdfDocument();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);

        var result = _inspector.Inspect(stream.ToArray());

        result.MetadataClaim.Should().Be(PdfAMetadataClaim.Missing);
        result.ClaimedProfile.Should().BeNull();
    }
}
