using System.Text.Json;
using Microsoft.Extensions.Options;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.PdfBox;

public sealed class PdfBoxGeometryNormalizer : IPdfGeometryNormalizer
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<PdfToolingOptions> _options;
    private readonly PdfBoxCommandFactory _commandFactory;
    private readonly PdfBoxGeometryReportParser _reportParser;

    public PdfBoxGeometryNormalizer(
        IProcessRunner processRunner,
        IOptions<PdfToolingOptions> options,
        PdfBoxCommandFactory commandFactory,
        PdfBoxGeometryReportParser reportParser)
    {
        _processRunner = processRunner;
        _options = options;
        _commandFactory = commandFactory;
        _reportParser = reportParser;
    }

    public async Task<PdfGeometryNormalizeResult> NormalizeGeometryAsync(
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var processResult = await _processRunner.RunAsync(
            _commandFactory.CreateNormalizeGeometryCommand(
                inputPath,
                outputPath,
                timeoutSeconds,
                _options.Value),
            cancellationToken);

        if (processResult.TimedOut)
        {
            return Failure(PdfToolErrorCodes.ProcessTimeout);
        }

        // The tool exits 3 with SIGNED_DOCUMENT on stderr when the file already carries a
        // signature: a full re-save would invalidate it, so the caller keeps its own path.
        if (!processResult.Success)
        {
            return Failure(
                PdfBoxProcessOutput.FirstLine(processResult.StandardError) ?? PdfToolErrorCodes.PdfBoxGeometryFailed);
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            return Failure(PdfToolErrorCodes.OutputNotCreated);
        }

        var payload = PdfBoxGeometryReportParser.ExtractPayload(processResult.StandardOutput);
        if (payload is null)
        {
            return Failure(PdfToolErrorCodes.GeometryReportUnreadable);
        }

        try
        {
            // The per-page report is part of this operation's contract - the caller needs it to
            // verify placement did not shift - so an unreadable report fails the operation even
            // though the output file itself is fine.
            return _reportParser.Parse(payload, CollectProcessWarnings(processResult));
        }
        catch (JsonException ex)
        {
            return Failure(PdfToolErrorCodes.GeometryReportUnreadable, ex.Message);
        }
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
                Version = PdfBoxProcessOutput.FirstLine(result.StandardOutput),
                Error = result.Success ? null : PdfBoxProcessOutput.FirstLine(result.StandardError)
            };
        }
        catch (Exception ex)
        {
            return new ToolHealthResult { Available = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Drops the report line before scanning for warnings: it carries a "warnings" key, so the
    /// generic substring match would otherwise report the whole JSON blob as a warning.
    /// </summary>
    private static IReadOnlyList<string> CollectProcessWarnings(ProcessResult result)
    {
        return
        [
            .. PdfBoxProcessOutput.CollectWarnings(result)
                .Where(line => !line.StartsWith(PdfBoxGeometryReportParser.Marker, StringComparison.Ordinal))
        ];
    }

    private static PdfGeometryNormalizeResult Failure(params string[] errors)
    {
        return new PdfGeometryNormalizeResult
        {
            Success = false,
            Errors = errors
        };
    }
}
