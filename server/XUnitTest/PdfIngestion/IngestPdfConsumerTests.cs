using System.Net;
using DomainService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StorageDriver;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.PdfGenerator.Tooling;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfIngestion.Entities;
using Utility.DomainService.PdfIngestion.Events;
using Utility.DomainService.PdfIngestion.service;
using Worker.Consumers.PdfIngestion;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Closes the same test gap as <see cref="PdfIngestionServiceTests"/>, on the consume side: every
/// exit path must leave the job record in a state the status endpoint can answer truthfully from,
/// and a repaired file that fails to save back must be reported as failed rather than as a
/// completed verdict describing bytes nobody actually wrote.
/// </remarks>
public sealed class IngestPdfConsumerTests
{
    private sealed class RoutingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public RoutingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private readonly Mock<IPdfIngestionPipeline> _pipeline = new();
    private readonly Mock<IPdfIngestionRepository> _repository = new();
    private readonly Mock<IPdfGeneratorNotificationService> _notificationService = new();
    private readonly Mock<IStorageDriverService> _storageDriver = new();

    private IngestPdfConsumer CreateConsumer(HttpMessageHandler? handler = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler ?? new RoutingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), disposeHandler: false));

        var storageHelper = new PdfStorageHelper(
            new Mock<ILogger<PdfStorageHelper>>().Object,
            _storageDriver.Object,
            factory.Object);

        return new IngestPdfConsumer(
            new Mock<ILogger<IngestPdfConsumer>>().Object,
            storageHelper,
            _pipeline.Object,
            _repository.Object,
            _notificationService.Object);
    }

    private static PdfIngestionJob QueuedJob(bool dryRun = false) => new()
    {
        Id = "file-1",
        Status = PdfIngestionStatus.Queued,
        UserId = "user-1"
    };

    private void SetUpDownload(byte[] content)
    {
        _storageDriver
            .Setup(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
            .ReturnsAsync(new FileResponse { Url = "https://storage.example/file-1", Name = "file-1.pdf", ParentDirectoryID = "dir-1" });
    }

    private static PdfIngestionOutcome Outcome(bool contentChanged, bool isReadable = true) => new()
    {
        PageCount = 1,
        WasReadable = true,
        IsReadable = isReadable,
        ContentChanged = contentChanged,
        FinalBytes = contentChanged ? [9, 9, 9] : [1, 2, 3]
    };

    [Fact]
    public async Task No_job_record_means_the_pipeline_never_runs()
    {
        _repository.Setup(x => x.GetJobAsync("file-1", null)).ReturnsAsync((PdfIngestionJob?)null);
        var consumer = CreateConsumer();

        await consumer.Consume(new IngestPdfEvent { FileId = "file-1" });

        _pipeline.Verify(x => x.ProcessAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_missing_storage_record_fails_the_job_without_running_the_pipeline()
    {
        _repository.Setup(x => x.GetJobAsync("file-1", null)).ReturnsAsync(QueuedJob());
        _repository.Setup(x => x.UpdateJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(true);
        _storageDriver.Setup(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>())).ReturnsAsync((FileResponse?)null);
        var consumer = CreateConsumer();

        await consumer.Consume(new IngestPdfEvent { FileId = "file-1" });

        _pipeline.Verify(x => x.ProcessAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(x => x.UpdateJobAsync(
            It.Is<PdfIngestionJob>(j => j.Status == PdfIngestionStatus.Failed && j.ErrorCode == "input_file_not_found"),
            null), Times.AtLeastOnce);
    }

    [Fact]
    public async Task An_unchanged_file_completes_without_ever_uploading()
    {
        SetUpDownload([1, 2, 3]);
        _repository.Setup(x => x.GetJobAsync("file-1", null)).ReturnsAsync(QueuedJob());
        _repository.Setup(x => x.UpdateJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(true);
        _pipeline.Setup(x => x.ProcessAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(Outcome(contentChanged: false));

        var uploadAttempted = false;
        var handler = new RoutingHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                uploadAttempted = true;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            };
        });

        await CreateConsumer(handler).Consume(new IngestPdfEvent { FileId = "file-1" });

        uploadAttempted.Should().BeFalse();
        _repository.Verify(x => x.UpdateJobAsync(
            It.Is<PdfIngestionJob>(j => j.Status == PdfIngestionStatus.Completed && j.Verdict != null),
            null), Times.AtLeastOnce);
        _notificationService.Verify(x => x.NotifyIngestPdfEvent(true, "file-1", It.IsAny<string>(), "user-1", null), Times.Once);
    }

    [Fact]
    public async Task A_changed_file_is_uploaded_back_and_the_job_completes()
    {
        SetUpDownload([1, 2, 3]);
        _repository.Setup(x => x.GetJobAsync("file-1", null)).ReturnsAsync(QueuedJob());
        _repository.Setup(x => x.UpdateJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(true);
        _storageDriver
            .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
            .ReturnsAsync(new GetPreSignedUrlForUploadResponse { UploadUrl = "https://storage.example/upload" });
        _pipeline.Setup(x => x.ProcessAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(Outcome(contentChanged: true));

        var uploadAttempted = false;
        var handler = new RoutingHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                uploadAttempted = true;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            };
        });

        await CreateConsumer(handler).Consume(new IngestPdfEvent { FileId = "file-1" });

        uploadAttempted.Should().BeTrue();
        _repository.Verify(x => x.UpdateJobAsync(
            It.Is<PdfIngestionJob>(j => j.Status == PdfIngestionStatus.Completed),
            null), Times.AtLeastOnce);
    }

    [Fact]
    public async Task A_dry_run_never_uploads_even_when_content_changed()
    {
        SetUpDownload([1, 2, 3]);
        _repository.Setup(x => x.GetJobAsync("file-1", null)).ReturnsAsync(QueuedJob());
        _repository.Setup(x => x.UpdateJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(true);
        _pipeline.Setup(x => x.ProcessAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(Outcome(contentChanged: true));

        var uploadAttempted = false;
        var handler = new RoutingHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                uploadAttempted = true;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            };
        });

        await CreateConsumer(handler).Consume(new IngestPdfEvent { FileId = "file-1", DryRun = true });

        uploadAttempted.Should().BeFalse();
        _repository.Verify(x => x.UpdateJobAsync(
            It.Is<PdfIngestionJob>(j => j.Status == PdfIngestionStatus.Completed),
            null), Times.AtLeastOnce);
    }

    [Fact]
    public async Task A_failed_upload_of_changed_bytes_fails_the_job_rather_than_reporting_a_verdict_for_bytes_nobody_wrote()
    {
        SetUpDownload([1, 2, 3]);
        _repository.Setup(x => x.GetJobAsync("file-1", null)).ReturnsAsync(QueuedJob());
        _repository.Setup(x => x.UpdateJobAsync(It.IsAny<PdfIngestionJob>(), null)).ReturnsAsync(true);
        _storageDriver
            .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
            .ReturnsAsync((GetPreSignedUrlForUploadResponse?)null);
        _pipeline.Setup(x => x.ProcessAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(Outcome(contentChanged: true));

        await CreateConsumer().Consume(new IngestPdfEvent { FileId = "file-1" });

        _repository.Verify(x => x.UpdateJobAsync(
            It.Is<PdfIngestionJob>(j => j.Status == PdfIngestionStatus.Failed && j.ErrorCode == "output_not_saved"),
            null), Times.AtLeastOnce);
    }
}
