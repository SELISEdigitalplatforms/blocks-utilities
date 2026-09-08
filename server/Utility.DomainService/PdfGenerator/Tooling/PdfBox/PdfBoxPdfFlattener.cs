using Microsoft.Extensions.Options;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.PdfBox;

public sealed class PdfBoxPdfFlattener : IPdfFlattener
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<PdfToolingOptions> _options;
    private readonly PdfBoxCommandFactory _commandFactory;

    public PdfBoxPdfFlattener(
        IProcessRunner processRunner,
        IOptions<PdfToolingOptions> options,
        PdfBoxCommandFactory commandFactory)
    {
        _processRunner = processRunner;
        _options = options;
        _commandFactory = commandFactory;
    }

    public async Task<PdfFlattenResult> FlattenAsync(
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var processResult = await _processRunner.RunAsync(
            _commandFactory.CreateFlattenCommand(
                inputPath,
                outputPath,
                timeoutSeconds,
                _options.Value),
            cancellationToken);

        if (processResult.TimedOut)
        {
            return Failure(PdfToolErrorCodes.ProcessTimeout);
        }

        if (!processResult.Success)
        {
            return Failure(FirstLine(processResult.StandardError) ?? PdfToolErrorCodes.PdfBoxFailed);
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            return Failure(PdfToolErrorCodes.OutputNotCreated);
        }

        return new PdfFlattenResult
        {
            Success = true,
            Warnings = CollectWarnings(processResult)
        };
    }

    public async Task<ToolHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                _commandFactory.CreateVersionCommand(_options.Value),
                cancellationToken);

            return new ToolHealthResult
            {
                Available = result.Success,
                Version = FirstLine(result.StandardOutput),
                Error = result.Success ? null : FirstLine(result.StandardError)
            };
        }
        catch (Exception ex)
        {
            return new ToolHealthResult { Available = false, Error = ex.Message };
        }
    }

    private static PdfFlattenResult Failure(string error)
    {
        return new PdfFlattenResult
        {
            Success = false,
            Errors = [error]
        };
    }

    private static string? FirstLine(string text)
    {
        return PdfBoxProcessOutput.FirstLine(text);
    }

    private static IReadOnlyList<string> CollectWarnings(ProcessResult result)
    {
        return PdfBoxProcessOutput.CollectWarnings(result);
    }
}
