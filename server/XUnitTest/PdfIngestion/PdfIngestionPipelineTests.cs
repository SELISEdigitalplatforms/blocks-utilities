using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Utility.DomainService.PdfGenerator.Tooling;
using Utility.DomainService.PdfGenerator.Tooling.Inspection;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;
using Utility.DomainService.PdfGenerator.Tooling.Workspace;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Guards two things a customer would actually notice: that the pipeline runs only the remediation
/// stages a file's own inspection calls for (an unnecessary Ghostscript pass on a healthy file is a
/// silent cost regression), and that one file's failure - however it fails - never throws out of
/// ProcessAsync, since a caller processing a batch depends on that to keep every other file's
/// result.
/// </remarks>
public sealed class PdfIngestionPipelineTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IPdfInspector> _inspector = new();
    private readonly Mock<IPdfNormalizer> _qpdfNormalizer = new();
    private readonly Mock<IPdfGeometryNormalizer> _geometryNormalizer = new();
    private readonly Mock<IPdfAValidator> _pdfAValidator = new();
    private readonly Mock<IPdfFlattener> _flattener = new();
    private readonly Mock<IPdfAConverter> _pdfAConverter = new();
    private readonly Mock<IStandardPdfConverter> _standardConverter = new();
    private readonly IPdfWorkspaceService _workspaceService;

    public PdfIngestionPipelineTests()
    {
        _tempDirectory = Directory.CreateTempSubdirectory("pdf-ingestion-tests-").FullName;
        _workspaceService = new PdfWorkspaceService(
            Options.Create(new PdfToolingOptions { TempDirectory = _tempDirectory }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private PdfIngestionPipeline CreatePipeline()
    {
        return new PdfIngestionPipeline(
            _inspector.Object,
            _workspaceService,
            _qpdfNormalizer.Object,
            _geometryNormalizer.Object,
            _pdfAValidator.Object,
            _flattener.Object,
            _pdfAConverter.Object,
            _standardConverter.Object,
            Options.Create(new PdfToolingOptions()),
            new Mock<ILogger<PdfIngestionPipeline>>().Object);
    }

    private static PdfInspectionResult HealthyInspection(PdfAMetadataClaim claim = PdfAMetadataClaim.Missing, string? claimedProfile = null)
    {
        return new PdfInspectionResult
        {
            Readable = true,
            GeometryReadable = true,
            PageCount = 1,
            MetadataClaim = claim,
            ClaimedProfile = claimedProfile
        };
    }

    private static Task<PdfNormalizeResult> WriteFileAndSucceed(string outputPath, byte[] content)
    {
        File.WriteAllBytes(outputPath, content);
        return Task.FromResult(new PdfNormalizeResult { Success = true });
    }

    [Fact]
    public async Task A_healthy_file_runs_no_remediation_stage_at_all()
    {
        _inspector.Setup(x => x.Inspect(It.IsAny<byte[]>())).Returns(HealthyInspection());

        var outcome = await CreatePipeline().ProcessAsync([1, 2, 3], CancellationToken.None);

        outcome.IsReadable.Should().BeTrue();
        outcome.ContentChanged.Should().BeFalse();
        outcome.Stages.Should().BeEmpty(because: "nothing about this file called for a remediation stage");
        _qpdfNormalizer.Verify(x => x.NormalizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PdfNormalizeOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        _geometryNormalizer.Verify(x => x.NormalizeGeometryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _pdfAValidator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unreadable_file_that_qpdf_repairs_becomes_readable()
    {
        var repairedBytes = new byte[] { 9, 9, 9 };
        _inspector.SetupSequence(x => x.Inspect(It.IsAny<byte[]>()))
            .Returns(new PdfInspectionResult { Readable = false, GeometryReadable = false, FailureReason = "broken" })
            .Returns(HealthyInspection());

        _qpdfNormalizer
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PdfNormalizeOptions>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string output, PdfNormalizeOptions _, CancellationToken _) => WriteFileAndSucceed(output, repairedBytes));

        var outcome = await CreatePipeline().ProcessAsync([0xDE, 0xAD], CancellationToken.None);

        outcome.WasReadable.Should().BeFalse();
        outcome.IsReadable.Should().BeTrue();
        outcome.RepairedWithQpdf.Should().BeTrue();
        outcome.ContentChanged.Should().BeTrue();
        outcome.FinalBytes.Should().Equal(repairedBytes);
    }

    [Fact]
    public async Task A_file_qpdf_cannot_repair_is_reported_not_thrown()
    {
        _inspector.Setup(x => x.Inspect(It.IsAny<byte[]>()))
            .Returns(new PdfInspectionResult { Readable = false, GeometryReadable = false, FailureReason = "beyond repair" });

        _qpdfNormalizer
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PdfNormalizeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfNormalizeResult { Success = false, Errors = ["QPDF_FAILED"] });

        var outcome = await CreatePipeline().ProcessAsync([0xDE, 0xAD], CancellationToken.None);

        outcome.IsReadable.Should().BeFalse();
        outcome.ErrorCode.Should().NotBeNullOrEmpty();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Geometry_normalization_runs_only_when_the_inspector_reports_it_unreadable()
    {
        var normalizedBytes = new byte[] { 5, 5, 5 };
        _inspector.SetupSequence(x => x.Inspect(It.IsAny<byte[]>()))
            .Returns(new PdfInspectionResult { Readable = true, GeometryReadable = false, PageCount = 1 })
            .Returns(HealthyInspection());

        _geometryNormalizer
            .Setup(x => x.NormalizeGeometryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string output, int _, CancellationToken _) =>
            {
                File.WriteAllBytes(output, normalizedBytes);
                return Task.FromResult(new PdfGeometryNormalizeResult { Success = true, GeometryChanged = true });
            });

        var outcome = await CreatePipeline().ProcessAsync([1], CancellationToken.None);

        outcome.GeometryWasReadable.Should().BeFalse();
        outcome.GeometryIsReadable.Should().BeTrue();
        outcome.GeometryNormalized.Should().BeTrue();
        outcome.FinalBytes.Should().Equal(normalizedBytes);
    }

    [Fact]
    public async Task A_signed_document_that_pdfbox_refuses_to_touch_is_recorded_as_skipped_not_failed()
    {
        _inspector.Setup(x => x.Inspect(It.IsAny<byte[]>()))
            .Returns(new PdfInspectionResult { Readable = true, GeometryReadable = false, PageCount = 1, HasSignature = true, SignatureCount = 1 });

        _geometryNormalizer
            .Setup(x => x.NormalizeGeometryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfGeometryNormalizeResult { Success = false, Errors = [PdfToolErrorCodes.SignedDocument] });

        var outcome = await CreatePipeline().ProcessAsync([1], CancellationToken.None);

        outcome.GeometryNormalized.Should().BeFalse();
        outcome.GeometrySkippedReason.Should().Be(PdfToolErrorCodes.SignedDocument);
        outcome.ContentChanged.Should().BeFalse(because: "a skip must not be treated as a byte-changing repair");
    }

    [Fact]
    public async Task A_pdfa_claim_that_verapdf_confirms_compliant_needs_no_repair()
    {
        _inspector.Setup(x => x.Inspect(It.IsAny<byte[]>()))
            .Returns(HealthyInspection(PdfAMetadataClaim.Claimed, "PDF/A-2B"));

        _pdfAValidator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfAValidationResult { Success = true, IsPdfA = true, IsCompliant = true, ProfileName = "2B" });

        var outcome = await CreatePipeline().ProcessAsync([1], CancellationToken.None);

        outcome.IsCompliant.Should().BeTrue();
        outcome.PdfARepairAttempted.Should().BeFalse();
        _flattener.Verify(x => x.FlattenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_noncompliant_pdfa_claim_is_repaired_via_flatten_then_ghostscript_then_revalidated()
    {
        _inspector.Setup(x => x.Inspect(It.IsAny<byte[]>()))
            .Returns(HealthyInspection(PdfAMetadataClaim.Claimed, "PDF/A-2B"));

        _pdfAValidator
            .SetupSequence(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfAValidationResult { Success = true, IsPdfA = true, IsCompliant = false, ProfileName = "2B", Errors = ["some check failed"] })
            .ReturnsAsync(new PdfAValidationResult { Success = true, IsPdfA = true, IsCompliant = true, ProfileName = "2B" });

        _flattener
            .Setup(x => x.FlattenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string output, int _, CancellationToken _) =>
            {
                File.WriteAllBytes(output, [1]);
                return Task.FromResult(new PdfFlattenResult { Success = true });
            });

        _pdfAConverter
            .Setup(x => x.ConvertToPdfA2BAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string output, int _, CancellationToken _) =>
            {
                File.WriteAllBytes(output, [2]);
                return Task.FromResult(new PdfTransformationResult { Success = true });
            });

        var outcome = await CreatePipeline().ProcessAsync([1], CancellationToken.None);

        outcome.PdfARepairAttempted.Should().BeTrue();
        outcome.PdfARepairSucceeded.Should().BeTrue();
        outcome.IsCompliant.Should().BeTrue();
        outcome.FinalProfile.Should().Be("2B");
        outcome.ContentChanged.Should().BeTrue();
        _standardConverter.Verify(x => x.ConvertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_pdfa_repair_ghostscript_cannot_fix_falls_back_to_a_standard_pdf()
    {
        _inspector.Setup(x => x.Inspect(It.IsAny<byte[]>()))
            .Returns(HealthyInspection(PdfAMetadataClaim.Claimed, "PDF/A-1B"));

        _pdfAValidator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            // The real veraPDF shape, captured against an actual PDF via its Docker CLI: a full
            // descriptive string, not a short code - profileName="PDF/A-1b validation profile".
            .ReturnsAsync(new PdfAValidationResult { Success = true, IsPdfA = true, IsCompliant = true, ProfileName = "PDF/A-1b validation profile" });

        _flattener
            .Setup(x => x.FlattenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string output, int _, CancellationToken _) =>
            {
                File.WriteAllBytes(output, [1]);
                return Task.FromResult(new PdfFlattenResult { Success = true });
            });

        _pdfAConverter
            .Setup(x => x.ConvertToPdfA2BAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfTransformationResult { Success = false, Errors = ["GHOSTSCRIPT_FAILED"] });

        _standardConverter
            .Setup(x => x.ConvertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string output, int _, CancellationToken _) =>
            {
                File.WriteAllBytes(output, [3]);
                return Task.FromResult(new PdfTransformationResult { Success = true });
            });

        var outcome = await CreatePipeline().ProcessAsync([1], CancellationToken.None);

        outcome.PdfARepairAttempted.Should().BeTrue();
        outcome.PdfARepairSucceeded.Should().BeFalse();
        outcome.IsPdfA.Should().BeFalse(because: "a document that could not be made compliant must not still claim PDF/A");
        outcome.IsCompliant.Should().BeFalse();
        outcome.FinalBytes.Should().Equal(new byte[] { 3 });
    }

    [Fact]
    public async Task An_unexpected_exception_is_reported_on_the_outcome_never_thrown()
    {
        _inspector.Setup(x => x.Inspect(It.IsAny<byte[]>())).Throws(new InvalidOperationException("boom"));

        var act = async () => await CreatePipeline().ProcessAsync([1], CancellationToken.None);

        var outcome = await act.Should().NotThrowAsync(because: "one file's failure must never abort a batch");
        outcome.Subject.IsReadable.Should().BeFalse();
        outcome.Subject.ErrorCode.Should().NotBeNullOrEmpty();
    }
}
