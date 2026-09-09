using Microsoft.Extensions.Options;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.PdfBox;

public sealed class PdfBoxStandardPdfConverter : IStandardPdfConverter
{
    private readonly IProcessRunner _runner;
    private readonly IOptions<PdfToolingOptions> _options;
    private readonly PdfBoxCommandFactory _commands;

    public PdfBoxStandardPdfConverter(IProcessRunner runner, IOptions<PdfToolingOptions> options, PdfBoxCommandFactory commands)
    {
        _runner = runner;
        _options = options;
        _commands = commands;
    }

    public async Task<PdfTransformationResult> ConvertAsync(string inputPath, string outputPath, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(_commands.CreateStandardizeCommand(inputPath, outputPath, timeoutSeconds, _options.Value), cancellationToken);
        if (result.TimedOut) return Failure(PdfToolErrorCodes.ProcessTimeout);
        if (!result.Success || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0) return Failure(FirstLine(result.StandardError) ?? PdfToolErrorCodes.StandardPdfConversionFailed);
        return new PdfTransformationResult { Success = true };
    }

    private static PdfTransformationResult Failure(string error) => new() { Success = false, Errors = [error] };
    private static string? FirstLine(string text) => text.Split(["\r\n", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
}
