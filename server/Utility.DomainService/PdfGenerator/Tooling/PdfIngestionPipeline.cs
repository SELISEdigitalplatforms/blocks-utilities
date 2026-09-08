using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Utility.DomainService.PdfGenerator.Tooling.Inspection;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfGenerator.Tooling;

/// <summary>
/// Mirrors l3-net-signature-pdfengine's <c>ProcessPdfIngestionAsync</c> ordering, but per-file and
/// non-throwing: that reference throws and abandons its whole batch on one bad file, which is
/// deliberately not carried over here (see <see cref="ProcessAsync"/>).
/// </summary>
public sealed class PdfIngestionPipeline : IPdfIngestionPipeline
{
    private readonly IPdfInspector _inspector;
    private readonly IPdfWorkspaceService _workspaceService;
    private readonly IPdfNormalizer _qpdfNormalizer;
    private readonly IPdfGeometryNormalizer _geometryNormalizer;
    private readonly IPdfAValidator _pdfAValidator;
    private readonly IPdfFlattener _flattener;
    private readonly IPdfAConverter _pdfAConverter;
    private readonly IStandardPdfConverter _standardConverter;
    private readonly IOptions<PdfToolingOptions> _options;
    private readonly ILogger<PdfIngestionPipeline> _logger;

    public PdfIngestionPipeline(
        IPdfInspector inspector,
        IPdfWorkspaceService workspaceService,
        IPdfNormalizer qpdfNormalizer,
        IPdfGeometryNormalizer geometryNormalizer,
        IPdfAValidator pdfAValidator,
        IPdfFlattener flattener,
        IPdfAConverter pdfAConverter,
        IStandardPdfConverter standardConverter,
        IOptions<PdfToolingOptions> options,
        ILogger<PdfIngestionPipeline> logger)
    {
        _inspector = inspector;
        _workspaceService = workspaceService;
        _qpdfNormalizer = qpdfNormalizer;
        _geometryNormalizer = geometryNormalizer;
        _pdfAValidator = pdfAValidator;
        _flattener = flattener;
        _pdfAConverter = pdfAConverter;
        _standardConverter = standardConverter;
        _options = options;
        _logger = logger;
    }

    public async Task<PdfIngestionOutcome> ProcessAsync(byte[] inputBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputBytes);

        var operationId = Guid.NewGuid().ToString("N");
        var timeoutSeconds = _options.Value.DefaultTimeoutSeconds;
        var stages = new List<PdfIngestionStageRecord>();

        try
        {
            await using var workspace = await CreateWorkspaceAsync(operationId, inputBytes, cancellationToken);
            var currentPath = workspace.InputPath;
            var currentBytes = inputBytes;
            var contentChanged = false;

            var inspection = _inspector.Inspect(currentBytes);
            var wasReadable = inspection.Readable;
            var repairedWithQpdf = false;

            if (!inspection.Readable)
            {
                _logger.LogInformation("PdfIngestion[{OperationId}]: unreadable, attempting qpdf repair", operationId);

                var qpdfOutputPath = Path.Combine(workspace.DirectoryPath, "qpdf-repaired.pdf");
                var qpdfResult = await _qpdfNormalizer.NormalizeAsync(
                    currentPath,
                    qpdfOutputPath,
                    new PdfNormalizeOptions { TimeoutSeconds = timeoutSeconds },
                    cancellationToken);

                stages.Add(new PdfIngestionStageRecord
                {
                    Stage = PdfIngestionStageName.QpdfNormalize,
                    Success = qpdfResult.Success,
                    Warnings = qpdfResult.Warnings,
                    Errors = qpdfResult.Errors
                });

                if (qpdfResult.Success)
                {
                    currentPath = qpdfOutputPath;
                    currentBytes = await File.ReadAllBytesAsync(currentPath, cancellationToken);
                    contentChanged = true;
                    repairedWithQpdf = true;
                    inspection = _inspector.Inspect(currentBytes);
                }
            }

            if (!inspection.Readable)
            {
                _logger.LogWarning("PdfIngestion[{OperationId}]: still unreadable after qpdf repair", operationId);

                return new PdfIngestionOutcome
                {
                    PageCount = 0,
                    WasReadable = wasReadable,
                    IsReadable = false,
                    RepairedWithQpdf = repairedWithQpdf,
                    GeometryWasReadable = false,
                    GeometryIsReadable = false,
                    ContentChanged = contentChanged,
                    FinalBytes = currentBytes,
                    Stages = stages,
                    ErrorCode = PdfToolErrorCodes.InvalidRequest,
                    ErrorMessage = LogSanitizer.Scrub(inspection.FailureReason)
                };
            }

            var geometryWasReadable = inspection.GeometryReadable;
            var geometryNormalized = false;
            string? geometrySkippedReason = null;

            if (!inspection.GeometryReadable)
            {
                _logger.LogInformation("PdfIngestion[{OperationId}]: geometry unreadable, normalizing with PDFBox", operationId);

                var geometryOutputPath = Path.Combine(workspace.DirectoryPath, "geometry-normalized.pdf");
                var geometryResult = await _geometryNormalizer.NormalizeGeometryAsync(
                    currentPath, geometryOutputPath, timeoutSeconds, cancellationToken);

                var signed = geometryResult.Errors.Contains(PdfToolErrorCodes.SignedDocument);
                stages.Add(new PdfIngestionStageRecord
                {
                    Stage = PdfIngestionStageName.PdfBoxGeometryNormalize,
                    Success = geometryResult.Success,
                    Skipped = signed,
                    SkipReason = signed ? PdfToolErrorCodes.SignedDocument : null,
                    Warnings = geometryResult.Warnings,
                    Errors = signed ? [] : geometryResult.Errors
                });

                if (signed)
                {
                    // A full re-save would invalidate the existing signature - the reference this
                    // was ported from treats this as non-fatal, and so does this pipeline.
                    geometrySkippedReason = PdfToolErrorCodes.SignedDocument;
                }
                else if (geometryResult.Success)
                {
                    currentPath = geometryOutputPath;
                    currentBytes = await File.ReadAllBytesAsync(currentPath, cancellationToken);
                    contentChanged = true;
                    geometryNormalized = true;
                    inspection = _inspector.Inspect(currentBytes);
                }
            }

            var pdfARepairAttempted = false;
            bool? pdfARepairSucceeded = null;
            string? finalProfile = null;
            var validatedProfile = inspection.ClaimedProfile;
            var isPdfA = false;
            var isCompliant = false;
            IReadOnlyList<string> failedChecks = [];

            if (inspection.MetadataClaim != PdfAMetadataClaim.Missing)
            {
                var veraPdfResult = await _pdfAValidator.ValidateAsync(currentPath, workspace.ReportPath, cancellationToken);
                stages.Add(new PdfIngestionStageRecord
                {
                    Stage = PdfIngestionStageName.VeraPdfValidate,
                    Success = veraPdfResult.Success,
                    Warnings = veraPdfResult.Warnings,
                    Errors = veraPdfResult.Errors
                });

                validatedProfile = veraPdfResult.ProfileName;
                isPdfA = veraPdfResult.IsPdfA;
                isCompliant = veraPdfResult.IsCompliant;
                failedChecks = veraPdfResult.Errors;
                finalProfile = validatedProfile;

                // Real veraPDF output puts a full descriptive string here - "PDF/A-1b validation
                // profile" - not a short code, so a StartsWith('1') check (which would never match)
                // was verified wrong against an actual captured report before being fixed to this.
                var isPdfA1 = validatedProfile is not null && validatedProfile.Contains("PDF/A-1", StringComparison.OrdinalIgnoreCase);
                var needsRepair = veraPdfResult.Success && (!isCompliant || isPdfA1);

                if (needsRepair)
                {
                    pdfARepairAttempted = true;
                    (pdfARepairSucceeded, finalProfile, isPdfA, isCompliant, failedChecks) = await RepairPdfAAsync(
                        workspace.DirectoryPath,
                        workspace.ReportPath,
                        currentPath,
                        timeoutSeconds,
                        stages,
                        operationId,
                        cancellationToken,
                        UpdateCurrent);

                    void UpdateCurrent(string path, byte[] bytes)
                    {
                        currentPath = path;
                        currentBytes = bytes;
                        contentChanged = true;
                    }
                }
            }

            return new PdfIngestionOutcome
            {
                PageCount = inspection.PageCount,
                WasReadable = wasReadable,
                IsReadable = true,
                RepairedWithQpdf = repairedWithQpdf,
                GeometryWasReadable = geometryWasReadable,
                GeometryIsReadable = inspection.GeometryReadable,
                GeometryNormalized = geometryNormalized,
                GeometrySkippedReason = geometrySkippedReason,
                HasSignature = inspection.HasSignature,
                SignatureCount = inspection.SignatureCount,
                MetadataClaim = inspection.MetadataClaim,
                ClaimedProfile = inspection.ClaimedProfile,
                ValidatedProfile = validatedProfile,
                IsPdfA = isPdfA,
                IsCompliant = isCompliant,
                PdfAFailedChecks = failedChecks,
                PdfARepairAttempted = pdfARepairAttempted,
                PdfARepairSucceeded = pdfARepairSucceeded,
                FinalProfile = finalProfile,
                ContentChanged = contentChanged,
                FinalBytes = currentBytes,
                Stages = stages
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A tool crashing, a workspace directory failing to create, or any other unexpected
            // failure must not abort the batch - this file is reported as failed, siblings still run.
            _logger.LogError(ex, "PdfIngestion[{OperationId}]: unhandled failure", operationId);

            return new PdfIngestionOutcome
            {
                PageCount = 0,
                WasReadable = false,
                IsReadable = false,
                ContentChanged = false,
                FinalBytes = inputBytes,
                Stages = stages,
                ErrorCode = PdfToolErrorCodes.InternalError,
                ErrorMessage = LogSanitizer.Scrub(ex.Message)
            };
        }
    }

    private async Task<PdfWorkspace> CreateWorkspaceAsync(string operationId, byte[] inputBytes, CancellationToken cancellationToken)
    {
        await using var inputStream = new MemoryStream(inputBytes, writable: false);
        return await _workspaceService.CreateWorkspaceAsync(operationId, inputStream, cancellationToken);
    }

    /// <summary>
    /// Flatten -&gt; Ghostscript -&gt; re-validate -&gt; standard-PDF fallback. Runs only when veraPDF
    /// reported the file claims PDF/A but is not compliant, or is compliant only at PDF/A-1 (which
    /// this pipeline treats as needing an upgrade to PDF/A-2B, matching the reference's own target).
    /// </summary>
    private async Task<(bool Succeeded, string? Profile, bool IsPdfA, bool IsCompliant, IReadOnlyList<string> FailedChecks)> RepairPdfAAsync(
        string workspaceDirectory,
        string reportPath,
        string inputPath,
        int timeoutSeconds,
        List<PdfIngestionStageRecord> stages,
        string operationId,
        CancellationToken cancellationToken,
        Action<string, byte[]> adoptOutput)
    {
        _logger.LogInformation("PdfIngestion[{OperationId}]: repairing non-compliant PDF/A", operationId);

        var flattenOutputPath = Path.Combine(workspaceDirectory, "flattened.pdf");
        var flattenResult = await _flattener.FlattenAsync(inputPath, flattenOutputPath, timeoutSeconds, cancellationToken);
        stages.Add(new PdfIngestionStageRecord
        {
            Stage = PdfIngestionStageName.PdfARepairFlatten,
            Success = flattenResult.Success,
            Warnings = flattenResult.Warnings,
            Errors = flattenResult.Errors
        });

        var flattenedPath = inputPath;
        if (flattenResult.Success)
        {
            flattenedPath = flattenOutputPath;
            adoptOutput(flattenedPath, await File.ReadAllBytesAsync(flattenedPath, cancellationToken));
        }

        var gsOutputPath = Path.Combine(workspaceDirectory, "pdfa-repaired.pdf");
        var gsResult = await _pdfAConverter.ConvertToPdfA2BAsync(flattenedPath, gsOutputPath, timeoutSeconds, cancellationToken);
        stages.Add(new PdfIngestionStageRecord
        {
            Stage = PdfIngestionStageName.PdfARepairGhostscript,
            Success = gsResult.Success,
            Warnings = gsResult.Warnings,
            Errors = gsResult.Errors
        });

        if (gsResult.Success)
        {
            var gsBytes = await File.ReadAllBytesAsync(gsOutputPath, cancellationToken);
            adoptOutput(gsOutputPath, gsBytes);

            var revalidateResult = await _pdfAValidator.ValidateAsync(gsOutputPath, reportPath, cancellationToken);
            stages.Add(new PdfIngestionStageRecord
            {
                Stage = PdfIngestionStageName.PdfARepairRevalidate,
                Success = revalidateResult.Success,
                Warnings = revalidateResult.Warnings,
                Errors = revalidateResult.Errors
            });

            if (revalidateResult.Success && revalidateResult.IsCompliant)
            {
                return (true, revalidateResult.ProfileName, true, true, revalidateResult.Errors);
            }
        }

        // Ghostscript repair did not produce a compliant PDF/A: fall back to a plain standard PDF
        // rather than shipping a file that still falsely claims PDF/A conformance.
        var standardOutputPath = Path.Combine(workspaceDirectory, "standard.pdf");
        var standardResult = await _standardConverter.ConvertAsync(flattenedPath, standardOutputPath, timeoutSeconds, cancellationToken);
        stages.Add(new PdfIngestionStageRecord
        {
            Stage = PdfIngestionStageName.PdfARepairStandardFallback,
            Success = standardResult.Success,
            Warnings = standardResult.Warnings,
            Errors = standardResult.Errors
        });

        if (standardResult.Success)
        {
            adoptOutput(standardOutputPath, await File.ReadAllBytesAsync(standardOutputPath, cancellationToken));
        }

        return (false, null, false, false, []);
    }
}
