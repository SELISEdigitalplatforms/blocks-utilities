using System.Net;
using System.Text.Json;
using Blocks.Genesis;
using DomainService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StorageDriver;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfSignatureValidation.Entities;
using Utility.DomainService.PdfSignatureValidation.Events;
using Utility.DomainService.PdfSignatureValidation.service;
using Utility.DomainService.PdfSignatureValidation.Utilities;
using Utility.DomainService.PdfSignatureValidation.Validator;
using Worker.Consumers.PdfSignatureValidation;

namespace XUnitTest.PdfSignatureValidation;

/// <remarks>
/// Guards the consume side of the spec (AC-4, AC-11 to AC-16): every exit path must leave the job in
/// a state the status endpoint can answer truthfully from; a run that was superseded by a newer
/// request must change nothing and tell nobody; and a validator that is merely not ready yet must
/// never turn into a failed job until the file has genuinely waited too long. Uses an in-memory
/// repository that applies the real run-ID rule, so the superseded-run tests exercise the rule
/// itself rather than a mock's canned answer.
/// </remarks>
public sealed class ValidatePdfSignaturesConsumerTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryRepository _repository = new();
    private readonly Mock<IPdfSignatureValidator> _validator = new();
    private readonly Mock<IPdfGeneratorNotificationService> _notifications = new();
    private readonly Mock<IMessageClient> _messageClient = new();
    private readonly Mock<IStorageDriverService> _storageDriver = new();
    private readonly List<ConsumerMessage<ValidatePdfSignaturesEvent>> _republished = [];
    private readonly FakeTimeProvider _time = new(Now);

    public ValidatePdfSignaturesConsumerTests()
    {
        _validator.SetupGet(x => x.IsReady).Returns(true);
        _storageDriver
            .Setup(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
            .ReturnsAsync(new FileResponse { Url = "https://storage.example/file-1", Name = "file-1.pdf" });
        _messageClient
            .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<ValidatePdfSignaturesEvent>>()))
            .Callback<ConsumerMessage<ValidatePdfSignaturesEvent>>(_republished.Add)
            .Returns(Task.CompletedTask);
        _repository.Jobs["file-1"] = QueuedJob();
    }

    private ValidatePdfSignaturesConsumer CreateConsumer(Func<HttpRequestMessage, HttpResponseMessage>? download = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(
                new RoutingHandler(download ?? (_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF-1.7"u8.ToArray()) })),
                disposeHandler: false));

        var storageHelper = new PdfStorageHelper(
            NullLogger<PdfStorageHelper>.Instance, _storageDriver.Object, factory.Object);

        return new ValidatePdfSignaturesConsumer(
            NullLogger<ValidatePdfSignaturesConsumer>.Instance,
            storageHelper,
            _validator.Object,
            _repository,
            _notifications.Object,
            _messageClient.Object,
            Options.Create(new PdfSignatureValidatorOptions()),
            _time);
    }

    private static PdfSignatureValidationJob QueuedJob(string runId = "run-1") => new()
    {
        Id = "file-1",
        RunId = runId,
        Status = PdfIngestionStatus.Queued,
        MessageCoRelationId = "corr-1",
        UserId = "user-1",
        TenantId = "tenant-1",
        CreateDate = Now
    };

    private static ValidatePdfSignaturesEvent Event(string runId = "run-1") => new()
    {
        FileId = "file-1",
        RunId = runId,
        MessageCoRelationId = "corr-1",
        ProjectKey = "tenant-1",
        UserId = "user-1"
    };

    private static PdfSignatureValidationVerdict Verdict() => new()
    {
        SignatureCount = 2,
        LowestLevel = "PAdES-BASELINE-LT",
        AllPassed = true,
        Signatures = [new PdfSignatureVerdict { Indication = "TOTAL_PASSED", RevocationOrigin = "DssDictionary" }]
    };

    private PdfSignatureValidationJob Stored => _repository.Jobs["file-1"];

    private void ValidatorReturns(PdfSignatureValidatorResult result) =>
        _validator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task A_validated_file_completes_the_job_with_its_verdict_and_notifies_once()
    {
        string? stagedPath = null;
        var existedWhileValidating = false;
        PdfIngestionStatus? statusWhileValidating = null;
        _validator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((path, _) =>
            {
                stagedPath = path;
                existedWhileValidating = File.Exists(path);
                statusWhileValidating = Stored.Status;
            })
            .ReturnsAsync(PdfSignatureValidatorResult.Validated(Verdict()));

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Completed);
        Stored.Verdict!.SignatureCount.Should().Be(2, "the stored verdict is what the status endpoint shows");
        Stored.CompletedDate.Should().Be(Now);
        Stored.FileName.Should().Be("file-1.pdf");
        statusWhileValidating.Should().Be(PdfIngestionStatus.Processing, "a poller should see that a worker has picked the file up");
        existedWhileValidating.Should().BeTrue("the validator reads the file by path, so it must be staged before the call");
        File.Exists(stagedPath!).Should().BeFalse("the staged copy of a customer's document must not be left in /tmp");
        _notifications.Verify(x => x.NotifyValidatePdfSignaturesEvent(true, "file-1", "corr-1", "user-1", "tenant-1"), Times.Once);
    }

    [Fact]
    public async Task Without_a_correlation_id_the_outcome_is_recorded_but_nobody_is_notified()
    {
        _repository.Jobs["file-1"].MessageCoRelationId = null;
        ValidatorReturns(PdfSignatureValidatorResult.Validated(Verdict()));

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Completed);
        _notifications.Verify(
            x => x.NotifyValidatePdfSignaturesEvent(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never, "the caller only asked to be notified by supplying a correlation id");
    }

    [Fact]
    public async Task A_notification_failure_does_not_change_the_recorded_outcome()
    {
        ValidatorReturns(PdfSignatureValidatorResult.Validated(Verdict()));
        _notifications
            .Setup(x => x.NotifyValidatePdfSignaturesEvent(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("notifier down"));

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Completed, "the status endpoint exists because notifications cannot be relied on");
    }

    [Fact]
    public async Task No_job_record_means_the_validator_is_never_asked()
    {
        _repository.Jobs.Clear();

        await CreateConsumer().Consume(Event());

        _validator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_event_for_a_superseded_run_does_nothing()
    {
        _repository.Jobs["file-1"] = QueuedJob(runId: "run-2");

        await CreateConsumer().Consume(Event(runId: "run-1"));

        _validator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "validating a file for a request that has been replaced is wasted work");
        Stored.Status.Should().Be(PdfIngestionStatus.Queued, "the newer request's record must be untouched");
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_run_superseded_while_validating_discards_its_verdict_and_tells_nobody()
    {
        _validator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _repository.Jobs["file-1"] = QueuedJob(runId: "run-2"))
            .ReturnsAsync(PdfSignatureValidatorResult.Validated(Verdict()));

        await CreateConsumer().Consume(Event(runId: "run-1"));

        Stored.RunId.Should().Be("run-2");
        Stored.Status.Should().Be(PdfIngestionStatus.Queued, "a poller must see the newer request, not the old run's verdict");
        Stored.Verdict.Should().BeNull();
        _notifications.Verify(
            x => x.NotifyValidatePdfSignaturesEvent(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never, "only the latest request's run may notify");
    }

    [Fact]
    public async Task A_redelivery_of_a_run_that_already_finished_is_skipped()
    {
        Stored.Status = PdfIngestionStatus.Completed;
        Stored.Verdict = Verdict();

        await CreateConsumer().Consume(Event());

        _validator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "brokers deliver at least once, and a second delivery must not re-run or re-notify");
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_file_missing_from_storage_fails_the_job_without_asking_the_validator()
    {
        _storageDriver.Setup(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>())).ReturnsAsync((FileResponse?)null);

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Failed);
        Stored.ErrorCode.Should().Be("input_file_not_found");
        _validator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(x => x.NotifyValidatePdfSignaturesEvent(false, "file-1", "corr-1", "user-1", "tenant-1"), Times.Once);
    }

    [Fact]
    public async Task A_file_that_cannot_be_downloaded_fails_the_job()
    {
        var consumer = CreateConsumer(download: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await consumer.Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Failed);
        Stored.ErrorCode.Should().Be("input_file_unreadable");
        _validator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("input_not_pdf")]
    [InlineData("input_password_protected")]
    [InlineData("validator_error")]
    public async Task A_file_the_validator_rejects_fails_the_job_with_the_validators_own_code(string code)
    {
        ValidatorReturns(PdfSignatureValidatorResult.Rejected(code, "no"));

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Failed);
        Stored.ErrorCode.Should().Be(code, "the caller acts on the code, e.g. asking for an unprotected file");
        _notifications.Verify(x => x.NotifyValidatePdfSignaturesEvent(false, "file-1", "corr-1", "user-1", "tenant-1"), Times.Once);
    }

    [Fact]
    public async Task A_timeout_fails_the_job_with_validation_timeout()
    {
        ValidatorReturns(PdfSignatureValidatorResult.TimedOut());

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Failed);
        Stored.ErrorCode.Should().Be("validation_timeout");
        _republished.Should().BeEmpty("a file that ran to the timeout is not retried; the caller may re-request");
    }

    [Fact]
    public async Task A_crash_fails_the_job_with_validator_crashed_and_is_not_retried()
    {
        ValidatorReturns(PdfSignatureValidatorResult.Crashed());

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Failed);
        Stored.ErrorCode.Should().Be("validator_crashed");
        _republished.Should().BeEmpty("the file may be what crashed the validator, and retrying would crash it again");
    }

    [Fact]
    public async Task An_unexpected_exception_fails_the_job_and_still_removes_the_staged_file()
    {
        string? stagedPath = null;
        _validator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((path, _) => stagedPath = path)
            .ThrowsAsync(new InvalidOperationException("boom"));

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Failed);
        Stored.ErrorCode.Should().Be("validation_error", "a thrown exception must not leave the job Processing forever");
        File.Exists(stagedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task A_validator_that_is_not_ready_leaves_the_job_queued_and_tries_again_later()
    {
        _validator.SetupGet(x => x.IsReady).Returns(false);

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Queued, "not ready is a state, never a reason to fail a file");
        _validator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var retry = _republished.Should().ContainSingle().Subject;
        retry.ConsumerName.Should().Be(PdfSignatureValidationConstants.ValidatePdfSignaturesQueue);
        retry.Payload.RunId.Should().Be("run-1", "the retry is the same run, so a superseding request still wins");
        retry.ScheduledEnqueueTimeUtc.Should().Be(Now.AddSeconds(15), "the retry is delayed, not an immediate redelivery loop");
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_validator_that_goes_down_between_the_check_and_the_file_puts_the_job_back_to_waiting()
    {
        ValidatorReturns(PdfSignatureValidatorResult.NotReady());

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Queued, "the job was marked Processing and must not be left that way");
        _republished.Should().ContainSingle();
    }

    [Fact]
    public async Task A_job_that_has_waited_longer_than_the_limit_for_the_lists_fails_with_trust_lists_unavailable()
    {
        _validator.SetupGet(x => x.IsReady).Returns(false);
        _time.UtcNow = Now.AddMinutes(10).AddSeconds(1);

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Failed);
        Stored.ErrorCode.Should().Be("trust_lists_unavailable", "never a misleading INDETERMINATE verdict");
        _republished.Should().BeEmpty("waiting is over");
        _notifications.Verify(x => x.NotifyValidatePdfSignaturesEvent(false, "file-1", "corr-1", "user-1", "tenant-1"), Times.Once);
    }

    [Fact]
    public async Task A_retry_that_cannot_be_queued_fails_the_job_instead_of_leaving_it_waiting_forever()
    {
        _validator.SetupGet(x => x.IsReady).Returns(false);
        _messageClient
            .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<ValidatePdfSignaturesEvent>>()))
            .ThrowsAsync(new InvalidOperationException("broker unreachable"));

        await CreateConsumer().Consume(Event());

        Stored.Status.Should().Be(PdfIngestionStatus.Failed);
        Stored.ErrorCode.Should().Be("validation_not_queued", "with no message in flight nothing would ever pick the job up");
    }

    private sealed class InMemoryRepository : IPdfSignatureValidationRepository
    {
        public Dictionary<string, PdfSignatureValidationJob> Jobs { get; } = [];

        // Copies in and out, like a database, so the consumer's edits to its job are not visible
        // until it writes them back.
        private static PdfSignatureValidationJob Copy(PdfSignatureValidationJob job) =>
            JsonSerializer.Deserialize<PdfSignatureValidationJob>(JsonSerializer.Serialize(job))!;

        public Task<bool> SaveJobAsync(PdfSignatureValidationJob job, string? tenantId = null)
        {
            Jobs[job.Id] = Copy(job);
            return Task.FromResult(true);
        }

        public Task<List<PdfSignatureValidationJob>> GetJobsAsync(IEnumerable<string> fileIds, string? tenantId = null) =>
            Task.FromResult(fileIds.Where(Jobs.ContainsKey).Select(id => Copy(Jobs[id])).ToList());

        /// <summary>The real repository's rule: the write lands only if the stored run ID still matches.</summary>
        public Task<bool> UpdateJobIfCurrentRunAsync(PdfSignatureValidationJob job, string? tenantId = null)
        {
            if (!Jobs.TryGetValue(job.Id, out var stored) || stored.RunId != job.RunId)
            {
                return Task.FromResult(false);
            }

            Jobs[job.Id] = Copy(job);
            return Task.FromResult(true);
        }
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }
}
