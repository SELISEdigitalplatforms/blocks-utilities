using System.Text;
using FluentAssertions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Reading the document ledger, and who is allowed to.
/// </summary>
/// <remarks>
/// Access control is the substance here. A document is a billing record naming an organization, its
/// address and its tax id, so the interesting tests are the ones where a caller asks for somebody
/// else's: by document id, by payment id, and by editing the cursor they were handed.
/// </remarks>
public sealed class SubscriptionFinancialDocumentHistoryServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string OtherOrganizationId = "org-2";
    private const string ConsoleOrganizationId = "console";

    private readonly FinancialDocumentLedgerFake _documents = new();
    private readonly Mock<ISubscriptionContextResolver> _context = new();
    private readonly Mock<IFinancialDocumentFileStore> _files = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();

    public SubscriptionFinancialDocumentHistoryServiceTests()
    {
        _context
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-7")));

        _files
            .Setup(files => files.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("%PDF"));
    }

    [Fact]
    public async Task The_list_returns_the_organizations_own_documents_newest_first()
    {
        await StoredAsync("INV-2026-000001", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await StoredAsync("INV-2026-000002", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await StoredAsync(
            "INV-2026-000003",
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            organizationId: OtherOrganizationId);

        var result = await Service().ListAsync(
            new GetFinancialDocumentsRequest(),
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Select(item => item.DocumentNumber)
            .Should().BeEquivalentTo(
                ["INV-2026-000002", "INV-2026-000001"],
                options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task A_type_filter_narrows_to_one_kind_of_document()
    {
        await StoredAsync("INV-2026-000001", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await StoredAsync(
            "CRN-2026-000001",
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            documentType: FinancialDocumentType.CreditNote);

        var result = await Service().ListAsync(
            new GetFinancialDocumentsRequest
            {
                DocumentType = FinancialDocumentType.CreditNote
            },
            "corr-1",
            CancellationToken.None);

        result.Value!.Items.Should().ContainSingle()
            .Which.DocumentNumber.Should().Be("CRN-2026-000001");
    }

    [Fact]
    public async Task An_issue_date_range_bounds_both_ends_inclusively()
    {
        await StoredAsync("INV-2026-000001", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await StoredAsync("INV-2026-000002", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await StoredAsync("INV-2027-000001", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await Service().ListAsync(
            new GetFinancialDocumentsRequest
            {
                IssuedFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IssuedToUtc = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc)
            },
            "corr-1",
            CancellationToken.None);

        // A tax year is the query this exists for, so both ends are inclusive — the first of January
        // has to be in it.
        result.Value!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_inverted_date_range_is_refused_rather_than_returning_nothing()
    {
        var result = await Service().ListAsync(
            new GetFinancialDocumentsRequest
            {
                IssuedFromUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IssuedToUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            "corr-1",
            CancellationToken.None);

        // An empty page would look like "you have no invoices", which is a worse answer than "your
        // dates are the wrong way round".
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_document_query_invalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task A_page_size_outside_the_bounds_is_refused(int pageSize)
    {
        var result = await Service().ListAsync(
            new GetFinancialDocumentsRequest { PageSize = pageSize },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Paging_follows_its_own_cursor_without_repeating_or_skipping()
    {
        for (var day = 1; day <= 5; day++)
        {
            await StoredAsync(
                $"INV-2026-{day:D6}",
                new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
        }

        var service = Service();
        var first = await service.ListAsync(
            new GetFinancialDocumentsRequest { PageSize = 2 },
            "corr-1",
            CancellationToken.None);

        first.Value!.PageInfo.HasNextPage.Should().BeTrue();

        var second = await service.ListAsync(
            new GetFinancialDocumentsRequest
            {
                PageSize = 2,
                After = first.Value.PageInfo.NextCursor
            },
            "corr-1",
            CancellationToken.None);

        second.Value!.Items.Select(item => item.DocumentNumber)
            .Should().NotIntersectWith(first.Value.Items.Select(item => item.DocumentNumber));
        second.Value.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_cursor_issued_to_another_organization_is_refused()
    {
        // The cursor is a value the client holds and can edit. Without the organization bound into it
        // this is an access-control bypass wearing a base64 costume.
        var foreign = FinancialDocumentCursorCodec.Encode(
            OtherOrganizationId,
            new FinancialDocumentCursor(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                "doc-1"));

        var result = await Service().ListAsync(
            new GetFinancialDocumentsRequest { After = foreign },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_document_query_invalid");
    }

    [Fact]
    public async Task A_document_belonging_to_another_organization_is_not_found_rather_than_forbidden()
    {
        var foreign = await StoredAsync(
            "INV-2026-000001",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            organizationId: OtherOrganizationId,
            storageId: "stored-1");

        var result = await Service().GetPdfAsync(
            foreign.ItemId,
            null,
            "corr-1",
            CancellationToken.None);

        // One answer for absent and for somebody else's, so the shape of the refusal cannot be used
        // to enumerate the tenant's documents.
        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should().Be("subscription_document_not_found");
    }

    [Fact]
    public async Task A_download_returns_the_stored_bytes_named_after_the_document()
    {
        var document = await StoredAsync(
            "INV-2026-000001",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            storageId: "stored-1");

        var result = await Service().GetPdfAsync(
            document.ItemId,
            null,
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("application/pdf");
        result.Value.FileName.Should().Be("INV-2026-000001.pdf");
        _files.Verify(
            files => files.ReadAsync("stored-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_payment_id_finds_the_application_document_issued_for_it()
    {
        // The compatibility path. A client holding a bookmarked payment id from the old
        // payment-derived history gets the application's own invoice rather than a 404.
        var document = await StoredAsync(
            "INV-2026-000001",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            storageId: "stored-1",
            sourceKey: FinancialDocumentSourceKey.ForPayment("pay-1"));

        var result = await Service().GetPdfAsync("pay-1", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FileName.Should().Be("INV-2026-000001.pdf");
        document.Delivery.StorageId.Should().Be("stored-1");
    }

    [Fact]
    public async Task A_document_whose_pdf_is_not_rendered_yet_says_so_rather_than_not_found()
    {
        var document = await StoredAsync(
            "INV-2026-000001",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await Service().GetPdfAsync(
            document.ItemId,
            null,
            "corr-1",
            CancellationToken.None);

        // A distinct code, because the answer to this one is "try again shortly" rather than "this
        // does not exist" — and the caller's retry policy depends on knowing which.
        result.ErrorCode.Should().Be("subscription_document_pdf_pending");
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
    }

    [Fact]
    public async Task A_listed_document_says_whether_its_pdf_can_be_downloaded()
    {
        await StoredAsync("INV-2026-000001", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await StoredAsync(
            "INV-2026-000002",
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            storageId: "stored-2");

        var result = await Service().ListAsync(
            new GetFinancialDocumentsRequest(),
            "corr-1",
            CancellationToken.None);

        // So a client can render a download control without probing each row for a 404.
        result.Value!.Items.Single(item => item.DocumentNumber == "INV-2026-000002")
            .IsPdfAvailable.Should().BeTrue();
        result.Value.Items.Single(item => item.DocumentNumber == "INV-2026-000001")
            .IsPdfAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task A_listed_document_reports_the_party_snapshots_it_was_issued_with()
    {
        await StoredAsync("INV-2026-000001", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await Service().ListAsync(
            new GetFinancialDocumentsRequest(),
            "corr-1",
            CancellationToken.None);

        // A page rendering "billed to" has to show what the document says, or the page and the PDF
        // will disagree the moment somebody edits the profile.
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.SubscriberLegalName.Should().Be("Northwind Trading AG");
        item.BillingContactName.Should().Be("Ada Byron");
        item.InitiatedByName.Should().Be("System renewal");
    }

    [Fact]
    public async Task Only_the_console_may_resend_a_document()
    {
        var document = await UnsentAsync();

        // The caller is the subscriber's own organization, which is what the fixture resolves to.
        var result = await Service().ResendAsync(
            document.ItemId, "corr-1", CancellationToken.None);

        // Whoever resends is accepting that the subscriber may receive the same invoice twice. Letting
        // a subscriber take that on would put the decision with the party that bears none of the
        // consequences of a duplicate landing in somebody else's finance mailbox.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_document_resend_forbidden");

        // And nothing was reopened, so the automatic path still refuses to send.
        _documents.Documents.Single().Delivery.MailRequestedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task A_console_resend_reopens_the_delivery_and_queues_it()
    {
        var document = await UnsentAsync();
        Console();

        var result = await Service().ResendAsync(
            document.ItemId, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DocumentNumber.Should().Be("INV-2026-000001");
        result.Value.Recipient.Should().Be("ada@northwind.example");

        // Unchanged by resending, which is what a support conversation about a duplicate would quote.
        result.Value.MessageId.Should().Be($"document-mail:{document.ItemId}");

        var reopened = _documents.Documents.Single();

        // The claim is back, which is the only thing that permits one more send.
        reopened.Delivery.MailRequestedAtUtc.Should().BeNull();
        reopened.Delivery.LastErrorCode.Should().BeNull();
        reopened.Delivery.AttemptCount.Should().Be(0);

        // Resumed at the mail rather than the render: the PDF exists, and an issued PDF is never
        // regenerated.
        reopened.Delivery.State.Should().Be(FinancialDocumentDeliveryState.Generated);
        reopened.Delivery.StorageId.Should().NotBeNull();

        // Queued on the same work type as every other delivery, so a resend cannot behave differently
        // from a first attempt.
        _scheduler.Verify(
            scheduler => scheduler.TryScheduleAsync(
                SubscriptionWorkType.FinancialDocumentDelivery,
                TenantId,
                $"document:{document.ItemId}",
                It.IsAny<DateTime>(),
                "corr-1",
                document.ItemId,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_document_with_no_recipient_is_refused_rather_than_queued()
    {
        var document = await UnsentAsync();
        document.BillingContact.Email = null;
        Console();

        var result = await Service().ResendAsync(
            document.ItemId, "corr-1", CancellationToken.None);

        // Queueing would spend an attempt discovering what is already known and then report it as a
        // delivery failure, which reads as an outage rather than as missing data.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_document_no_recipient");
    }

    [Fact]
    public async Task Resending_something_that_does_not_exist_says_so()
    {
        Console();

        var result = await Service().ResendAsync("nope", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
    }

    /// <summary>
    /// A document whose mail was claimed and whose outcome was never established.
    /// </summary>
    /// <remarks>
    /// The state a resend exists for. Nothing automatic will touch it again — a failed publish is not
    /// evidence of non-delivery — so this is exactly the document a person has to decide about.
    /// </remarks>
    private async Task<SubscriptionFinancialDocument> UnsentAsync()
    {
        var document = await StoredAsync(
            "INV-2026-000001",
            new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc),
            storageId: "stored-1");

        document.BillingContact.Email = "ada@northwind.example";
        document.Delivery.MailMessageId = $"document-mail:{document.ItemId}";
        document.Delivery.MailRequestedAtUtc = new DateTime(2026, 8, 25, 10, 1, 0, DateTimeKind.Utc);
        document.Delivery.State = FinancialDocumentDeliveryState.Abandoned;
        document.Delivery.LastErrorCode = "document_mail_outcome_unknown";
        document.Delivery.AttemptCount = 1;

        return document;
    }

    /// <summary>Puts the caller in the console organization, which is the only one that may resend.</summary>
    private void Console() =>
        _context
            .Setup(context => context.ResolveAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, ConsoleOrganizationId, "actor-1", "user-7")));

    private ISubscriptionFinancialDocumentHistoryService Service() =>
        new SubscriptionFinancialDocumentHistoryService(
            _context.Object,
            _documents,
            _files.Object,
            Options.Create(new PaymentOptions
            {
                ConsoleOrganizationId = ConsoleOrganizationId
            }),
            _scheduler.Object);

    private async Task<SubscriptionFinancialDocument> StoredAsync(
        string documentNumber,
        DateTime issuedAtUtc,
        string organizationId = OrganizationId,
        FinancialDocumentType documentType = FinancialDocumentType.Invoice,
        string? storageId = null,
        string? sourceKey = null)
    {
        var document = new SubscriptionFinancialDocument
        {
            DocumentNumber = documentNumber,
            DocumentType = documentType,
            IssuedAtUtc = issuedAtUtc,
            TenantId = TenantId,
            OrganizationId = organizationId,
            SubscriptionId = "sub-1",
            SourceKey = sourceKey ?? $"payment:{documentNumber}",
            CurrencyCode = "CHF",
            Subscriber = new FinancialDocumentParty { LegalName = "Northwind Trading AG" },
            BillingContact = new FinancialDocumentPerson { Name = "Ada Byron" },
            InitiatedBy = new FinancialDocumentPerson { Name = "System renewal" },
            Subject = new FinancialDocumentSubject { PlanCode = "pro", PlanName = "Pro" },
            Period = new FinancialDocumentPeriod { TimeZoneId = "Europe/Zurich" },
            Amounts = new FinancialDocumentAmounts { TotalMinor = 100_000 },
            Delivery = new FinancialDocumentDelivery
            {
                StorageId = storageId,
                ContentHash = storageId is null ? null : "abc",
                State = storageId is null
                    ? FinancialDocumentDeliveryState.Pending
                    : FinancialDocumentDeliveryState.Generated
            }
        };

        await _documents.InsertAsync(document, CancellationToken.None);

        return document;
    }
}
