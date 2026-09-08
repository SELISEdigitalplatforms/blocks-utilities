using FluentAssertions;
using Utility.DomainService.PdfGenerator.Tooling.PdfBox;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Parses reports genuinely produced by running the actual tools/pdfbox/PdfBoxNormalizeGeometry.java
/// tool (compiled for real, via the docker/pdfbox dev image) against real PDFs - not a hand-written
/// guess at the report shape.
/// </remarks>
public sealed class PdfBoxGeometryReportParserTests
{
    private readonly PdfBoxGeometryReportParser _parser = new();

    // Captured verbatim from:
    //   docker run --rm --network none -v <dir>:/work blocks-pdfbox:dev PdfBoxNormalizeGeometry /work/plain.pdf /work/out.pdf
    // against a one-page PDF written by PdfSharp with a direct (not indirect) /MediaBox.
    private const string RealReportOnADirectMediaBox =
        """##PDFBOX_GEOMETRY##{"engine":"pdfbox","version":"3.0.8","pageCount":1,"signed":false,"changed":true,"pages":[{"index":0,"rotate":0,"mediaBox":[0.0000,0.0000,595.0000,842.0000],"cropBox":[0.0000,0.0000,595.0000,842.0000],"width":595.0000,"height":842.0000,"rewrittenBoxes":["MediaBox"],"indirectBoxes":[]}],"warnings":[]}""";

    // Same tool, same run shape, against a hand-built raw PDF whose /MediaBox is a genuine indirect
    // reference (/MediaBox 4 0 R, with object 4 the array [0 0 595 842]) - the exact pattern the
    // e-signature reference's PdfSharpCore throws on.
    private const string RealReportOnAnIndirectMediaBox =
        """##PDFBOX_GEOMETRY##{"engine":"pdfbox","version":"3.0.8","pageCount":1,"signed":false,"changed":true,"pages":[{"index":0,"rotate":0,"mediaBox":[0.0000,0.0000,595.0000,842.0000],"cropBox":[0.0000,0.0000,595.0000,842.0000],"width":595.0000,"height":842.0000,"rewrittenBoxes":["MediaBox"],"indirectBoxes":["MediaBox"]}],"warnings":[]}""";

    [Fact]
    public void ExtractPayload_finds_the_marker_line_and_strips_the_marker_itself()
    {
        var output = "some noise\n" + RealReportOnADirectMediaBox + "\ntrailing noise";

        var payload = PdfBoxGeometryReportParser.ExtractPayload(output);

        payload.Should().NotBeNull();
        payload.Should().NotContain("##PDFBOX_GEOMETRY##");
        payload.Should().StartWith("{\"engine\"");
    }

    [Fact]
    public void ExtractPayload_returns_null_when_the_tool_emitted_no_report()
    {
        PdfBoxGeometryReportParser.ExtractPayload("just some log noise, no marker anywhere").Should().BeNull();
    }

    [Fact]
    public void A_direct_mediabox_reports_no_indirect_boxes_even_though_it_was_still_rewritten()
    {
        var payload = PdfBoxGeometryReportParser.ExtractPayload(RealReportOnADirectMediaBox)!;

        var result = _parser.Parse(payload, processWarnings: []);

        result.Success.Should().BeTrue();
        result.PageCount.Should().Be(1);
        result.Signed.Should().BeFalse();
        result.GeometryChanged.Should().BeTrue(because: "MediaBox is always materialised at page level regardless of whether it needed it");
        result.Pages.Should().ContainSingle();
        result.Pages[0].IndirectBoxes.Should().BeEmpty();
        result.Pages[0].RewrittenBoxes.Should().Contain("MediaBox");
    }

    [Fact]
    public void A_genuinely_indirect_mediabox_is_reported_as_such()
    {
        var payload = PdfBoxGeometryReportParser.ExtractPayload(RealReportOnAnIndirectMediaBox)!;

        var result = _parser.Parse(payload, processWarnings: []);

        result.Pages[0].IndirectBoxes.Should().Contain("MediaBox");
        result.Pages[0].Width.Should().Be(595.0);
        result.Pages[0].Height.Should().Be(842.0);
    }

    [Fact]
    public void Process_warnings_are_merged_in_and_deduplicated_against_the_reports_own_warnings()
    {
        var payload = PdfBoxGeometryReportParser.ExtractPayload(RealReportOnADirectMediaBox)!;

        var result = _parser.Parse(payload, processWarnings: ["a stray stderr warning"]);

        result.Warnings.Should().Contain("a stray stderr warning");
    }
}
