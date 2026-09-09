using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.Ghostscript;

/// <summary>
/// Direct-only: unlike qpdf, veraPDF and PdfBox, Ghostscript has no Docker execution mode here (the
/// source it was ported from never gave it one). A developer using Docker mode for the other three
/// still needs <c>gs</c> installed locally to exercise PDF/A repair.
/// </summary>
public sealed class GhostscriptCommandFactory
{
    public ProcessCommand CreatePdfA2BCommand(string inputPath, string outputPath, int timeoutSeconds, PdfToolingOptions options) => new()
    {
        FileName = options.GhostscriptPath,
        Arguments =
        [
            "-dPDFA=2",
            "-dBATCH",
            "-dNOPAUSE",
            "-dSAFER",
            $"--permit-file-read={options.GhostscriptIccProfilePath}",
            "-sDEVICE=pdfwrite",
            "-dPDFACompatibilityPolicy=1",
            "-sColorConversionStrategy=RGB",
            $"-sOutputFile={outputPath}",
            options.GhostscriptPdfADefinitionPath,
            inputPath
        ],
        WorkingDirectory = Path.GetDirectoryName(inputPath),
        TimeoutSeconds = timeoutSeconds
    };
}
