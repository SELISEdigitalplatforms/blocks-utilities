using Blocks.Genesis;
using DomainService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StorageDriver;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfIngestion.Entities;
using Utility.DomainService.PdfIngestion.Events;
using Utility.DomainService.PdfIngestion.service;
using Utility.DomainService.PdfIngestion.Utilities;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Closes a gap the document-conversion feature this was modelled on left open: neither its
/// service nor its consumer had tests. Covers the batch validation rules, that one bad file id
/// does not stop the rest of a batch from being accepted, that a queue-publish failure marks the
/// job Failed rather than leaving it stuck Queued forever, and that a completed job's stored
/// verdict is the one the status endpoint actually reports back.
/// </remarks>
public sealed class PdfIngestionServiceTests
{
    private readonly Mock<ILogger<PdfIngestionService>> _logger = new();
    private readonly Mock<IMessageClient> _messageClient = new();
    private readonly Mock<IPdfIngestionRepository> _repository = new();
    private readonly Mock<IStorageDriverService> _storageDriver = new();

    private PdfIngestionService CreateService()
    {
        var storageHelper = new PdfStorageHelper(
            new Mock<ILogger<PdfStorageHelper>>().Object,
            _storageDriver.Object,
            new Mock<IHttpClientFactory>(MockBehavior.Strict).Object);

        return new PdfIngestionService(_logger.Object, _messageClient.Object, _repository.Object, storageHelper);
    }

    [Fact]
    public async Task An_empty_file_list_is_a_validation_failure()
    {
        var service = CreateService();

        var result = await service.RequestIngestionsAsync(new IngestPdfsRequest(), "corr-1");

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PdfIngestionFailureKind.Validation);
    }

    [Fact]
    public async Task More_files_than_the_batch_limit_is_a_validation_failure()
    {
        var request = new IngestPdfsRequest
        {
            FileIds = [.. Enumerable.Range(0, 51).Select(i => $"file-{i}")]
        };
        var service = CreateService();

        var result = await service.RequestIngestionsAsync(request, "corr-1");

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PdfIngestionFailureKind.Validation);
    }

    [Fact]
    public async Task A_blank_id_is_rejected_without_stopping_the_rest_of_the_batch()
    {
        _repository.Setup(x => x.SaveJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(true);
        var request = new IngestPdfsRequest { FileIds = ["", "file-1"] };
        var service = CreateService();

        var result = await service.RequestIngestionsAsync(request, "corr-1");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Results.Should().HaveCount(2);
        result.Value.Results[0].Accepted.Should().BeFalse();
        result.Value.Results[1].Accepted.Should().BeTrue();
        result.Value.AcceptedCount.Should().Be(1);
        result.Value.RejectedCount.Should().Be(1);
    }

    [Fact]
    public async Task A_duplicate_id_is_queued_only_once()
    {
        _repository.Setup(x => x.SaveJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(true);
        var request = new IngestPdfsRequest { FileIds = ["file-1", "file-1"] };
        var service = CreateService();

        var result = await service.RequestIngestionsAsync(request, "corr-1");

        result.Value!.Results.Should().ContainSingle();
        _messageClient.Verify(
            x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<IngestPdfEvent>>()),
            Times.Once);
    }

    [Fact]
    public async Task A_file_the_repository_cannot_record_is_never_queued()
    {
        _repository.Setup(x => x.SaveJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(false);
        var request = new IngestPdfsRequest { FileIds = ["file-1"] };
        var service = CreateService();

        var result = await service.RequestIngestionsAsync(request, "corr-1");

        result.Value!.Results[0].Accepted.Should().BeFalse();
        _messageClient.Verify(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<IngestPdfEvent>>()), Times.Never);
    }

    [Fact]
    public async Task A_publish_failure_marks_the_already_recorded_job_failed_rather_than_leaving_it_queued_forever()
    {
        _repository.Setup(x => x.SaveJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(true);
        _messageClient
            .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<IngestPdfEvent>>()))
            .ThrowsAsync(new InvalidOperationException("broker unreachable"));
        var request = new IngestPdfsRequest { FileIds = ["file-1"] };
        var service = CreateService();

        var result = await service.RequestIngestionsAsync(request, "corr-1");

        result.Value!.Results[0].Accepted.Should().BeFalse();
        _repository.Verify(
            x => x.UpdateJobAsync(
                It.Is<PdfIngestionJob>(j => j.Status == PdfIngestionStatus.Failed),
                null),
            Times.Once);
    }

    [Fact]
    public async Task The_published_event_carries_the_dry_run_flag_and_project_key()
    {
        _repository.Setup(x => x.SaveJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(true);
        ConsumerMessage<IngestPdfEvent>? captured = null;
        _messageClient
            .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<IngestPdfEvent>>()))
            .Callback<ConsumerMessage<IngestPdfEvent>>(m => captured = m)
            .Returns(Task.CompletedTask);

        var request = new IngestPdfsRequest { FileIds = ["file-1"], DryRun = true };
        var service = CreateService();

        await service.RequestIngestionsAsync(request, "corr-1");

        captured.Should().NotBeNull();
        captured!.Payload.FileId.Should().Be("file-1");
        captured.Payload.DryRun.Should().BeTrue();
        captured.ConsumerName.Should().Be(PdfIngestionConstants.IngestPdfQueue);
    }

    [Fact]
    public async Task GetStatus_reports_not_found_for_a_file_never_submitted()
    {
        _repository.Setup(x => x.GetJobsAsync(It.IsAny<IEnumerable<string>>(), null)).ReturnsAsync([]);
        var service = CreateService();

        var result = await service.GetStatusAsync(new GetPdfIngestionStatusRequest { FileIds = ["missing"] }, "corr-1");

        result.Value!.Results.Should().ContainSingle();
        result.Value.Results[0].Found.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_reports_the_stored_verdict_and_resolves_a_fresh_download_url()
    {
        var job = new PdfIngestionJob
        {
            Id = "file-1",
            Status = PdfIngestionStatus.Completed,
            CompletedDate = DateTime.UtcNow,
            Verdict = new PdfIngestionVerdict
            {
                IsReadable = true,
                IsPdfA = true,
                IsCompliant = true,
                ContentChanged = true
            }
        };
        _repository.Setup(x => x.GetJobsAsync(It.IsAny<IEnumerable<string>>(), null)).ReturnsAsync([job]);
        _storageDriver
            .Setup(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
            .ReturnsAsync(new FileResponse { Url = "https://storage.example/file-1", Name = "file-1.pdf" });

        var service = CreateService();

        var result = await service.GetStatusAsync(new GetPdfIngestionStatusRequest { FileIds = ["file-1"] }, "corr-1");

        var status = result.Value!.Results[0];
        status.Found.Should().BeTrue();
        status.IsComplete.Should().BeTrue();
        status.DownloadUrl.Should().Be("https://storage.example/file-1");
        status.IsPdfA.Should().BeTrue();
        status.ContentChanged.Should().BeTrue();
    }
}
