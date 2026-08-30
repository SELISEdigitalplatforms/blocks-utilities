using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Messaging;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

public sealed class MailDeliveryReporterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 7, 12, 48, TimeSpan.Zero);

    private readonly Mock<IMailDeliveryReportRepository> _reports = new();

    [Fact]
    public async Task Keeps_the_payload_as_it_was_published()
    {
        var written = CaptureWrite();

        await Reporter().RecordAsync(Request(Payload()), CancellationToken.None);

        written.Value.Should().NotBeNull();

        // The point of the collection: the body context is recoverable verbatim. An invoice that
        // arrives with an empty plan name cannot be diagnosed from anything else after the fact.
        var round = JsonSerializer.Deserialize<SendMail>(written.Value!.PayloadJson);

        round.Should().NotBeNull();
        round!.To.Should().Equal("owner@example.com");
        round.Purpose.Should().Be("subscription-invoice");
        round.Attachments.Should().Equal("storage-1");
        round.BodyDataContext["Total"].Should().Be("USD 49.00");
        round.BodyDataContext["PlanName"].Should().Be("Professional");
    }

    [Fact]
    public async Task Copies_out_the_fields_worth_querying_without_parsing_the_payload()
    {
        var written = CaptureWrite();

        await Reporter().RecordAsync(Request(Payload()), CancellationToken.None);

        written.Value!.TenantId.Should().Be("tenant-1");
        written.Value.SubjectReference.Should().Be("INV-2026-000039");
        written.Value.MailMessageId.Should().Be("document-mail:doc-1");
        written.Value.Outcome.Should().Be(MailDeliveryReportOutcome.Published);
        written.Value.Source.Should().Be(MailDeliveryReportSource.FinancialDocument);
        written.Value.To.Should().Equal("owner@example.com");
        written.Value.Attachments.Should().Equal("storage-1");
        written.Value.PayloadLength.Should().Be(written.Value.PayloadJson.Length);
        written.Value.PayloadHash.Should().HaveLength(64);
        written.Value.CreatedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task Sets_a_purge_date_so_addresses_are_not_kept_indefinitely()
    {
        var written = CaptureWrite();

        await Reporter().RecordAsync(Request(Payload()), CancellationToken.None);

        written.Value!.PurgeAtUtc.Should().Be(
            Now.UtcDateTime.Add(MailDeliveryReporter.Retention));
    }

    [Fact]
    public async Task Records_a_refusal_that_never_built_a_payload()
    {
        var written = CaptureWrite();

        // "No billing contact" is exactly the row an operator is looking for, so a report with
        // nothing to serialize is still worth writing.
        await Reporter().RecordAsync(
            Request(payload: null) with
            {
                Outcome = MailDeliveryReportOutcome.NotAttempted,
                ErrorCode = "document_mail_no_recipient"
            },
            CancellationToken.None);

        written.Value!.Outcome.Should().Be(MailDeliveryReportOutcome.NotAttempted);
        written.Value.ErrorCode.Should().Be("document_mail_no_recipient");
        written.Value.PayloadJson.Should().BeEmpty();
        written.Value.PayloadHash.Should().BeEmpty();
        written.Value.To.Should().BeEmpty();
    }

    [Fact]
    public async Task Never_lets_a_failed_write_reach_the_caller()
    {
        _reports
            .Setup(r => r.AddAsync(It.IsAny<MailDeliveryReport>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("mongo is unreachable"));

        // The guarantee the whole feature depends on. The document mail path is at-most-once
        // behind a claim, so an exception escaping here would unwind a send that already happened
        // and put a second invoice in somebody's inbox.
        var recording = async () =>
            await Reporter().RecordAsync(Request(Payload()), CancellationToken.None);

        await recording.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Swallows_cancellation_too()
    {
        _reports
            .Setup(r => r.AddAsync(It.IsAny<MailDeliveryReport>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Cancellation is not special here: the mail's outcome was decided before this was called,
        // so there is nothing left to abandon and rethrowing would still risk the duplicate.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var recording = async () =>
            await Reporter().RecordAsync(Request(Payload()), cancelled.Token);

        await recording.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Hashes_identical_payloads_alike_and_different_ones_apart()
    {
        var hashes = new List<string>();
        _reports
            .Setup(r => r.AddAsync(It.IsAny<MailDeliveryReport>(), It.IsAny<CancellationToken>()))
            .Callback<MailDeliveryReport, CancellationToken>(
                (report, _) => hashes.Add(report.PayloadHash))
            .Returns(Task.CompletedTask);

        var reporter = Reporter();
        await reporter.RecordAsync(Request(Payload()), CancellationToken.None);
        await reporter.RecordAsync(Request(Payload()), CancellationToken.None);

        var different = Payload();
        different.BodyDataContext["Total"] = "USD 59.00";
        await reporter.RecordAsync(Request(different), CancellationToken.None);

        hashes[0].Should().Be(hashes[1]);
        hashes[2].Should().NotBe(hashes[0]);
    }

    private sealed class Captured
    {
        public MailDeliveryReport? Value { get; set; }
    }

    private Captured CaptureWrite()
    {
        var captured = new Captured();

        _reports
            .Setup(r => r.AddAsync(It.IsAny<MailDeliveryReport>(), It.IsAny<CancellationToken>()))
            .Callback<MailDeliveryReport, CancellationToken>(
                (report, _) => captured.Value = report)
            .Returns(Task.CompletedTask);

        return captured;
    }

    private MailDeliveryReporter Reporter() =>
        new(_reports.Object,
            new ControlledTimeProvider(Now),
            NullLogger<MailDeliveryReporter>.Instance);

    private static SendMail Payload()
    {
        var context = new Dictionary<string, string>
        {
            ["PlanName"] = "Professional",
            ["Total"] = "USD 49.00",
            ["DocumentNumber"] = "INV-2026-000039"
        };

        return new SendMail
        {
            To = ["owner@example.com"],
            Purpose = "subscription-invoice",
            Language = "en-US",
            Attachments = ["storage-1"],
            SubjectDataContext = new Dictionary<string, string>(context),
            BodyDataContext = context
        };
    }

    private static MailDeliveryReportRequest Request(SendMail? payload) =>
        new()
        {
            TenantId = "tenant-1",
            OrganizationId = "org-1",
            Source = MailDeliveryReportSource.FinancialDocument,
            Outcome = MailDeliveryReportOutcome.Published,
            SubjectId = "doc-1",
            SubjectReference = "INV-2026-000039",
            MailMessageId = "document-mail:doc-1",
            ConsumerName = "blocks_email_listener",
            Payload = payload,
            CorrelationId = "sweep-1"
        };
}
