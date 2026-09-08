using System.Text;
using Microsoft.Extensions.Options;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.VeraPdf;

public sealed class VeraPdfAValidator : IPdfAValidator
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<PdfToolingOptions> _options;
    private readonly VeraPdfCommandFactory _commandFactory;
    private readonly VeraPdfXmlParser _xmlParser;

    public VeraPdfAValidator(
        IProcessRunner processRunner,
        IOptions<PdfToolingOptions> options,
        VeraPdfCommandFactory commandFactory,
        VeraPdfXmlParser xmlParser)
    {
        _processRunner = processRunner;
        _options = options;
        _commandFactory = commandFactory;
        _xmlParser = xmlParser;
    }

    public async Task<PdfAValidationResult> ValidateAsync(
        string inputPath,
        string reportPath,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            _commandFactory.CreateValidateCommand(inputPath, _options.Value),
            cancellationToken);

        if (result.TimedOut)
        {
            return Failure(PdfToolErrorCodes.ProcessTimeout, result.StandardOutput);
        }

        var rawReport = result.StandardOutput;
        if (!string.IsNullOrWhiteSpace(rawReport))
        {
            await File.WriteAllTextAsync(reportPath, rawReport, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(rawReport))
        {
            return Failure(PdfToolErrorCodes.VeraPdfFailed, rawReport, FirstLine(result.StandardError));
        }

        try
        {
            var parsedResult = _xmlParser.Parse(rawReport, result.Success, FirstLine(result.StandardError));
            return WithResolvedPdfAClaim(inputPath, parsedResult);
        }
        catch (Exception ex)
        {
            return Failure(PdfToolErrorCodes.VeraPdfFailed, rawReport, ex.Message);
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
                Version = FirstLine(result.StandardOutput),
                Error = result.Success ? null : FirstLine(result.StandardError)
            };
        }
        catch (Exception ex)
        {
            return new ToolHealthResult { Available = false, Error = ex.Message };
        }
    }

    private static PdfAValidationResult Failure(string error, string rawReport, string? detail = null)
    {
        return new PdfAValidationResult
        {
            Success = false,
            RawReport = rawReport,
            Errors = string.IsNullOrWhiteSpace(detail) ? [error] : [error, detail]
        };
    }

    private static string? FirstLine(string text)
    {
        return text.Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    private static PdfAValidationResult WithResolvedPdfAClaim(string inputPath, PdfAValidationResult result)
    {
        var isPdfA = result.IsCompliant || IsPdfAClaimed(inputPath);
        if (!File.Exists(inputPath))
        {
            isPdfA = result.IsPdfA;
        }

        return new PdfAValidationResult
        {
            Success = result.Success,
            Engine = result.Engine,
            IsPdfA = isPdfA,
            IsCompliant = result.IsCompliant,
            ProfileName = result.ProfileName,
            RawReport = result.RawReport,
            Errors = result.Errors,
            Warnings = result.Warnings
        };
    }

    private static bool IsPdfAClaimed(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            return false;
        }

        var content = Encoding.Latin1.GetString(File.ReadAllBytes(inputPath));
        return content.Contains("pdfaid:part", StringComparison.OrdinalIgnoreCase)
            || content.Contains("pdfaid:conformance", StringComparison.OrdinalIgnoreCase)
            || content.Contains("www.aiim.org/pdfa/ns/id", StringComparison.OrdinalIgnoreCase)
            || content.Contains("/GTS_PDFA", StringComparison.OrdinalIgnoreCase);
    }
}
