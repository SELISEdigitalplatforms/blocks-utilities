using FluentAssertions;
using Utility.DomainService.PdfGenerator.Tooling.Ghostscript;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Pins the fix this port made over the source it was ported from: the ICC profile path must come
/// from <see cref="PdfToolingOptions.GhostscriptIccProfilePath"/>, not a hardcoded Debian literal -
/// the source's own hardcoded path does not exist on the Alpine image this runs in and would
/// silently produce broken PDF/A output.
/// </remarks>
public sealed class GhostscriptCommandFactoryTests
{
    private readonly GhostscriptCommandFactory _factory = new();

    [Fact]
    public void The_permit_file_read_argument_follows_the_configured_icc_path()
    {
        var options = new PdfToolingOptions
        {
            GhostscriptIccProfilePath = "/usr/share/color/icc/ghostscript/srgb.icc"
        };

        var command = _factory.CreatePdfA2BCommand("/work/input.pdf", "/work/output.pdf", 60, options);

        command.Arguments.Should().Contain("--permit-file-read=/usr/share/color/icc/ghostscript/srgb.icc");
    }

    [Fact]
    public void A_different_configured_icc_path_changes_the_argument_rather_than_being_ignored()
    {
        var options = new PdfToolingOptions
        {
            GhostscriptIccProfilePath = "/some/other/profile.icc"
        };

        var command = _factory.CreatePdfA2BCommand("/work/input.pdf", "/work/output.pdf", 60, options);

        command.Arguments.Should().Contain("--permit-file-read=/some/other/profile.icc");
        command.Arguments.Should().NotContain(a => a.Contains("/usr/share/color/icc/ghostscript/srgb.icc"));
    }

    [Fact]
    public void The_command_targets_pdfa2_and_the_configured_definition_file()
    {
        var options = new PdfToolingOptions
        {
            GhostscriptPath = "/usr/bin/gs",
            GhostscriptPdfADefinitionPath = "/opt/pdfingestion/PDFA_def.ps"
        };

        var command = _factory.CreatePdfA2BCommand("/work/input.pdf", "/work/output.pdf", 60, options);

        command.FileName.Should().Be("/usr/bin/gs");
        command.Arguments.Should().Contain("-dPDFA=2");
        command.Arguments.Should().Contain("-sOutputFile=/work/output.pdf");
        command.Arguments.Should().ContainInOrder("/opt/pdfingestion/PDFA_def.ps", "/work/input.pdf");
    }
}
