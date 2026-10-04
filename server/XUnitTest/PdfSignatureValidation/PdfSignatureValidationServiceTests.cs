using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfSignatureValidation;
using Utility.DomainService.PdfSignatureValidation.Entities;
using Utility.DomainService.PdfSignatureValidation.Events;
using Utility.DomainService.PdfSignatureValidation.service;
using Utility.DomainService.PdfSignatureValidation.Utilities;

namespace XUnitTest.PdfSignatureValidation;

/// <remarks>
/// Guards the contract the SELISE signature app polls against (spec AC-1 to AC-5): a caller that
/// sends N entries must be able to match exactly N results to them, a repeated or blank entry must
/// not hide or double-queue anything, a re-request must start a fresh run rather than share the old
/// one, and a status answer must never invent or drop a file.
/// </remarks>
public sealed class PdfSignatureValidationServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IMessageClient> _messageClient = new();
    private readonly Mock<IPdfSignatureValidationRepository> _repository = new();
    private readonly List<ConsumerMessage<ValidatePdfSignaturesEvent>> _published = [];
    private readonly List<PdfSignatureValidationJob> _saved = [];

    public PdfSignatureValidationServiceTests()
    {
        _repository
            .Setup(x => x.SaveJobAsync(It.IsAny<PdfSignatureValidationJob>(), null))
            .Callback<PdfSignatureValidationJob, string?>((job, _) => _saved.Add(job))
            .ReturnsAsync(true);
        _messageClient
            .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<ValidatePdfSignaturesEvent>>()))
            .Callback<ConsumerMessage<ValidatePdfSignaturesEvent>>(_published.Add)
            .Returns(Task.CompletedTask);
    }

    private PdfSignatureValidationService CreateService() =>
        new(
            new Mock<ILogger<PdfSignatureValidationService>>().Object,
            _messageClient.Object,
            _repository.Object,
            new FakeTimeProvider(Now));

    private static ValidatePdfSignaturesRequest Request(params string[] fileIds) => new() { FileIds = [.. fileIds] };

    [Fact]
    public async Task A_batch_of_valid_ids_is_accepted_with_one_queued_result_per_id()
    {
        var result = await CreateService().RequestValidationsAsync(Request("file-1", "file-2"), "corr-1");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Results.Select(r => r.FileId).Should().Equal(
            ["file-1", "file-2"], "the caller matches results to its request by position and ID");
        result.Value.Results.Should().OnlyContain(
            r => r.Accepted && r.Status == PdfIngestionStatus.Queued,
            "an accepted file starts out waiting for a worker");
        result.Value.StatusUrl.Should().Be("/pdf-signature-validations/status");
        _published.Should().HaveCount(2, "every accepted file must reach the queue or it is never validated");
    }

    [Fact]
    public async Task An_empty_list_is_a_validation_failure_and_queues_nothing()
    {
        var result = await CreateService().RequestValidationsAsync(new ValidatePdfSignaturesRequest(), "corr-1");

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PdfIngestionFailureKind.Validation, "an empty request is the caller's error, a 400");
        result.ValidationErrors.Should().ContainKey("fileIds", "the field error tells the caller which field to fix");
        _published.Should().BeEmpty();
    }

    [Fact]
    public async Task Fifty_one_entries_are_rejected_even_when_only_fifty_are_distinct()
    {
        var ids = Enumerable.Range(0, 50).Select(i => $"file-{i}").Append("file-0").ToArray();

        var result = await CreateService().RequestValidationsAsync(Request(ids), "corr-1");

        result.IsSuccess.Should().BeFalse(
            "the limit is on entries sent, because each entry gets its own result; counting after "
            + "removing duplicates, as ingestion does, would let a response grow past 50 results");
        result.FailureKind.Should().Be(PdfIngestionFailureKind.Validation);
        _saved.Should().BeEmpty("nothing in a rejected request may be recorded");
        _published.Should().BeEmpty("nothing in a rejected request may be queued");
    }

    [Fact]
    public async Task A_blank_and_a_repeated_id_are_rejected_on_their_own_and_the_rest_is_still_queued()
    {
        var result = await CreateService().RequestValidationsAsync(Request("file-1", " ", "file-1", "file-2"), "corr-1");

        var results = result.Value!.Results;
        results.Should().HaveCount(4, "one result per entry sent, so the caller can match every entry");
        results[0].Accepted.Should().BeTrue("the first occurrence of a repeated ID is the one queued");
        results[1].ErrorCode.Should().Be("input_file_id_required");
        results[2].ErrorCode.Should().Be(
            "input_file_id_duplicate", "a repeat is reported, not silently dropped as ingestion does");
        results[3].Accepted.Should().BeTrue("one bad entry must not stop the rest of the batch");
        result.Value.AcceptedCount.Should().Be(2);
        result.Value.RejectedCount.Should().Be(2);
        _published.Select(m => m.Payload.FileId).Should().Equal(
            ["file-1", "file-2"], "queuing a repeated ID twice would race two runs of the same file");
    }

    [Fact]
    public async Task Requesting_a_file_again_starts_a_new_run_that_the_queued_event_carries()
    {
        var service = CreateService();

        await service.RequestValidationsAsync(Request("file-1"), "corr-1");
        await service.RequestValidationsAsync(Request("file-1"), "corr-2");

        _saved.Should().HaveCount(2);
        _saved[1].RunId.Should().NotBe(
            _saved[0].RunId, "the new run ID is what turns a run still in flight into a superseded one");
        _saved[1].Status.Should().Be(PdfIngestionStatus.Queued, "a re-requested file goes back to waiting");
        _published.Select(m => m.Payload.RunId).Should().Equal(
            [_saved[0].RunId, _saved[1].RunId],
            "the worker can only discard a superseded run if the event tells it which run it is");
    }

    [Fact]
    public async Task The_queued_event_goes_to_the_validation_queue_with_the_callers_tenant_and_correlation_id()
    {
        var request = Request("file-1");
        request.MessageCoRelationId = "notify-me";

        await CreateService().RequestValidationsAsync(request, "corr-1");

        var message = _published.Should().ContainSingle().Subject;
        message.ConsumerName.Should().Be(PdfSignatureValidationConstants.ValidatePdfSignaturesQueue);
        message.Payload.MessageCoRelationId.Should().Be(
            "notify-me", "without it the caller never receives the completion notification it asked for");
        _saved[0].CreateDate.Should().Be(Now, "the record is stamped from the injected clock");
    }

    [Fact]
    public async Task A_file_the_repository_cannot_record_is_rejected_and_never_queued()
    {
        _repository.Setup(x => x.SaveJobAsync(It.IsAny<PdfSignatureValidationJob>(), null)).ReturnsAsync(false);

        var result = await CreateService().RequestValidationsAsync(Request("file-1"), "corr-1");

        result.Value!.Results[0].ErrorCode.Should().Be("validation_not_recorded");
        _published.Should().BeEmpty("a worker picking up an unrecorded job would have no record or run ID to check");
    }

    [Fact]
    public async Task A_publish_failure_fails_the_run_it_recorded_rather_than_leaving_it_queued_forever()
    {
        _messageClient
            .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<ValidatePdfSignaturesEvent>>()))
            .ThrowsAsync(new InvalidOperationException("broker unreachable"));

        var result = await CreateService().RequestValidationsAsync(Request("file-1"), "corr-1");

        result.Value!.Results[0].ErrorCode.Should().Be("validation_not_queued");
        _repository.Verify(
            x => x.UpdateJobIfCurrentRunAsync(
                It.Is<PdfSignatureValidationJob>(j =>
                    j.Status == PdfIngestionStatus.Failed
                    && j.RunId == _saved[0].RunId
                    && j.CompletedDate == Now),
                null),
            Times.Once,
            "a poller deserves an answer; and the write is guarded so it cannot fail a newer request");
    }

    [Fact]
    public async Task Status_answers_every_distinct_id_once_ignoring_blanks_and_repeats()
    {
        _repository
            .Setup(x => x.GetJobsAsync(It.IsAny<IEnumerable<string>>(), null))
            .ReturnsAsync([new PdfSignatureValidationJob { Id = "file-1", Status = PdfIngestionStatus.Processing }]);

        var result = await CreateService().GetStatusAsync(
            new GetPdfSignatureValidationStatusRequest { FileIds = ["file-1", "", "never-sent", "file-1"] },
            "corr-1");

        var results = result.Value!.Results;
        results.Select(r => r.FileId).Should().Equal(
            ["file-1", "never-sent"], "one result per distinct ID, in the order first asked");
        results[0].Found.Should().BeTrue();
        results[0].IsComplete.Should().BeFalse("a running file must keep being polled");
        results[1].Found.Should().BeFalse(
            "a file never submitted, or another tenant's, answers found: false rather than dropping out");
        results[1].Status.Should().BeNull();
    }

    [Fact]
    public async Task Status_with_only_blank_ids_is_a_validation_failure()
    {
        var result = await CreateService().GetStatusAsync(
            new GetPdfSignatureValidationStatusRequest { FileIds = ["", "  "] }, "corr-1");

        result.FailureKind.Should().Be(PdfIngestionFailureKind.Validation, "there is nothing left to ask about");
        _repository.Verify(x => x.GetJobsAsync(It.IsAny<IEnumerable<string>>(), null), Times.Never);
    }

    [Fact]
    public async Task A_completed_job_reports_its_stored_verdict_as_complete()
    {
        var verdict = new PdfSignatureValidationVerdict
        {
            SignatureCount = 1,
            LowestLevel = "PAdES-BASELINE-LT",
            AllPassed = true,
            Signatures = [new PdfSignatureVerdict { Indication = "TOTAL_PASSED", RevocationOrigin = "DssDictionary" }]
        };
        _repository
            .Setup(x => x.GetJobsAsync(It.IsAny<IEnumerable<string>>(), null))
            .ReturnsAsync([new PdfSignatureValidationJob
            {
                Id = "file-1",
                Status = PdfIngestionStatus.Completed,
                CreateDate = Now,
                CompletedDate = Now.AddSeconds(20),
                Verdict = verdict
            }]);

        var result = await CreateService().GetStatusAsync(
            new GetPdfSignatureValidationStatusRequest { FileIds = ["file-1"] }, "corr-1");

        var status = result.Value!.Results[0];
        status.IsComplete.Should().BeTrue("a completed file needs no more polling");
        status.Verdict.Should().BeSameAs(verdict, "the stored verdict is exactly what the caller is shown");
        status.CompletedAtUtc.Should().Be(Now.AddSeconds(20));
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
