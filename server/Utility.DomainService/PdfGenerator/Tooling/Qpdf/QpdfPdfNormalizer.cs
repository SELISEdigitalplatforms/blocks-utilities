using Microsoft.Extensions.Options;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.Qpdf;

public sealed class QpdfPdfNormalizer : IPdfNormalizer
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<PdfToolingOptions> _options;
    private readonly QpdfCommandFactory _commandFactory;

    public QpdfPdfNormalizer(
        IProcessRunner processRunner,
        IOptions<PdfToolingOptions> options,
        QpdfCommandFactory commandFactory)
    {
        _processRunner = processRunner;
        _options = options;
        _commandFactory = commandFactory;
    }

    public async Task<PdfNormalizeResult> NormalizeAsync(
        string inputPath,
        string outputPath,
        PdfNormalizeOptions options,
        CancellationToken cancellationToken)
    {
        var normalizeResult = await _processRunner.RunAsync(
            _commandFactory.CreateNormalizeCommand(inputPath, outputPath, options, _options.Value),
            cancellationToken);

        if (normalizeResult.TimedOut)
        {
            return Failure(
                PdfToolErrorCodes.ProcessTimeout,
                CollectErrors("normalize", normalizeResult),
                CollectWarnings(normalizeResult));
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            return Failure(
                PdfToolErrorCodes.OutputNotCreated,
                CollectErrors("normalize", normalizeResult),
                CollectWarnings(normalizeResult));
        }

        var checkResult = await _processRunner.RunAsync(
            _commandFactory.CreateCheckCommand(outputPath, _options.Value, options.TimeoutSeconds),
            cancellationToken);

        if (checkResult.TimedOut)
        {
            return Failure(
                PdfToolErrorCodes.ProcessTimeout,
                CollectErrors("check", checkResult),
                CollectWarnings(normalizeResult, checkResult));
        }

        if (!checkResult.Success)
        {
            return Failure(
                PdfToolErrorCodes.QpdfFailed,
                CollectErrors("check", checkResult),
                CollectWarnings(normalizeResult, checkResult));
        }

        return new PdfNormalizeResult
        {
            Success = true,
            Warnings = CollectWarnings(normalizeResult, checkResult)
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

    private static PdfNormalizeResult Failure(
        string errorCode,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? warnings = null)
    {
        return new PdfNormalizeResult
        {
            Success = false,
            Errors = errors is { Count: > 0 }
                ? [errorCode, .. errors.Where(error => !string.Equals(error, errorCode, StringComparison.OrdinalIgnoreCase))]
                : [errorCode],
            Warnings = warnings ?? []
        };
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        return text.Split(["\r\n", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? FirstLine(string text) => SplitLines(text).FirstOrDefault();

    private static IReadOnlyList<string> CollectWarnings(params ProcessResult[] results)
    {
        return results
            .SelectMany(result => SplitLines(result.StandardError).Concat(SplitLines(result.StandardOutput)))
            .Where(line => line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("warning", StringComparison.OrdinalIgnoreCase)
                || line.Contains("succeeded with warnings", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
    }

    private static IReadOnlyList<string> CollectErrors(string stage, ProcessResult result)
    {
        var diagnostics = SplitLines(result.StandardError)
            .Concat(SplitLines(result.StandardOutput))
            .Where(line => !IsWarning(line))
            .Distinct()
            .ToList();

        diagnostics.Insert(0, $"qpdf {stage} exited with code {result.ExitCode}.");
        return diagnostics;
    }

    private static bool IsWarning(string line)
    {
        return line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("warning", StringComparison.OrdinalIgnoreCase)
            || line.Contains("succeeded with warnings", StringComparison.OrdinalIgnoreCase);
    }
}
