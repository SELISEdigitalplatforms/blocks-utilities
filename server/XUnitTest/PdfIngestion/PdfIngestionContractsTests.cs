using FluentAssertions;
using Utility.DomainService.PdfGenerator.Tooling.Inspection;
using Utility.DomainService.PdfIngestion;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Guards the "not found" shape callers actually depend on: a caller checks Found before reading
/// any verdict field, and every one of those fields must default to null so a poller cannot
/// mistake an empty status for a real (if falsy) verdict.
/// </remarks>
public sealed class PdfIngestionContractsTests
{
    [Fact]
    public void A_not_found_result_leaves_every_verdict_field_null()
    {
        var result = new PdfIngestionStatusResult { FileId = "f1", Found = false };

        result.Status.Should().BeNull();
        result.DownloadUrl.Should().BeNull();
        result.WasReadable.Should().BeNull();
        result.IsReadable.Should().BeNull();
        result.GeometryWasReadable.Should().BeNull();
        result.MetadataClaim.Should().BeNull();
        result.PdfAFailedChecks.Should().BeNull();
        result.Stages.Should().BeNull();
        result.ContentChanged.Should().BeNull();
    }

    [Fact]
    public void A_completed_result_carries_the_full_verdict()
    {
        var result = new PdfIngestionStatusResult
        {
            FileId = "f1",
            Found = true,
            Status = PdfIngestionStatus.Completed,
            IsComplete = true,
            DownloadUrl = "https://example/download",
            WasReadable = false,
            IsReadable = true,
            RepairedWithQpdf = true,
            GeometryWasReadable = true,
            GeometryIsReadable = true,
            HasSignature = true,
            SignatureCount = 1,
            MetadataClaim = PdfAMetadataClaim.Claimed,
            ClaimedProfile = "PDF/A-2B",
            IsPdfA = true,
            IsCompliant = true,
            ContentChanged = true
        };

        result.Status.Should().Be(PdfIngestionStatus.Completed);
        result.IsComplete.Should().BeTrue();
        result.RepairedWithQpdf.Should().BeTrue();
        result.ClaimedProfile.Should().Be("PDF/A-2B");
        result.ContentChanged.Should().BeTrue();
    }

    [Fact]
    public void A_batch_response_defaults_to_an_empty_result_list()
    {
        var response = new PdfIngestionStatusBatchResponse();

        response.Results.Should().BeEmpty();
    }
}
