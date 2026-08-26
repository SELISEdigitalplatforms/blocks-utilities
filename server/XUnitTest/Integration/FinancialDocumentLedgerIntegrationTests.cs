using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The two guarantees the document ledger buys from MongoDB rather than from our own code.
/// </summary>
/// <remarks>
/// Exactly-once issuing rests on a unique index, and gapless-enough numbering rests on an atomic
/// increment. Both would pass against an in-memory stand-in while the real collection cheerfully
/// allocated one number to two invoices — which is the failure that cannot be repaired afterwards,
/// because both documents have been sent.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class FinancialDocumentLedgerIntegrationTests
{
    private readonly SubscriptionFinancialDocumentRepository _documents;
    private readonly FinancialDocumentNumberAllocator _numbers;
    private readonly SubscriptionBillingProfileRepository _profiles;

    public FinancialDocumentLedgerIntegrationTests(MongoIntegrationFixture fixture)
    {
        _documents = new SubscriptionFinancialDocumentRepository(fixture.DbContextProvider);
        _numbers = new FinancialDocumentNumberAllocator(fixture.DbContextProvider);
        _profiles = new SubscriptionBillingProfileRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task One_source_can_only_ever_have_one_document()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var sourceKey = "payment:pay-1";

        var first = await _documents.InsertAsync(
            Document(tenantId, "INV-2026-000001", sourceKey),
            CancellationToken.None);
        var second = await _documents.InsertAsync(
            Document(tenantId, "INV-2026-000002", sourceKey),
            CancellationToken.None);

        first.Inserted.Should().BeTrue();

        // The loser is handed the winner's document rather than an error, so a caller that raced can
        // still deliver the one that exists instead of having to look it up again.
        second.Inserted.Should().BeFalse();
        second.Document.DocumentNumber.Should().Be("INV-2026-000001");
    }

    [Fact]
    public async Task Two_documents_can_never_share_a_number()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _documents.InsertAsync(
            Document(tenantId, "INV-2026-000001", "payment:pay-1"),
            CancellationToken.None);

        // A different source, so the source index does not catch it. Only the number index can, and
        // it has to: two documents claiming to be INV-2026-000001 is a state nothing can resolve.
        var collision = await _documents.InsertAsync(
            Document(tenantId, "INV-2026-000001", "payment:pay-2"),
            CancellationToken.None);

        collision.Inserted.Should().BeFalse();
    }

    [Fact]
    public async Task Numbers_are_unique_under_concurrency_and_start_at_one_each_year()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var allocated = await Task.WhenAll(
            Enumerable.Range(0, 40).Select(_ =>
                _numbers.AllocateAsync(
                    tenantId,
                    FinancialDocumentType.Invoice,
                    2026,
                    CancellationToken.None)));

        // Forty concurrent allocations, forty distinct numbers. This is the whole point of
        // findAndModify with $inc rather than read-then-write.
        allocated.Distinct(StringComparer.Ordinal).Should().HaveCount(40);
        allocated.Should().Contain("INV-2026-000001");
        allocated.Should().Contain("INV-2026-000040");
    }

    [Fact]
    public async Task Each_year_prefix_and_tenant_counts_separately()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var otherTenantId = MongoIntegrationFixture.NewTenantId();

        (await _numbers.AllocateAsync(
                tenantId, FinancialDocumentType.Invoice, 2026, CancellationToken.None))
            .Should().Be("INV-2026-000001");

        // A new year resets to one with nobody running a job, because the year is part of the
        // counter's identity rather than something filtered on.
        (await _numbers.AllocateAsync(
                tenantId, FinancialDocumentType.Invoice, 2027, CancellationToken.None))
            .Should().Be("INV-2027-000001");

        // Credit notes have their own series, so an invoice and a credit note never share a number.
        (await _numbers.AllocateAsync(
                tenantId, FinancialDocumentType.CreditNote, 2026, CancellationToken.None))
            .Should().Be("CRN-2026-000001");

        // And one tenant's numbering says nothing about another's.
        (await _numbers.AllocateAsync(
                otherTenantId, FinancialDocumentType.Invoice, 2026, CancellationToken.None))
            .Should().Be("INV-2026-000001");
    }

    [Fact]
    public async Task A_trial_invoice_is_numbered_in_the_invoice_series()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _numbers.AllocateAsync(
            tenantId, FinancialDocumentType.Invoice, 2026, CancellationToken.None);

        // A subscriber whose first document is INV-2026-000001 and whose second is INV-2026-000002
        // can see they have all of them. A third series would start their invoice numbering at 1 twice.
        (await _numbers.AllocateAsync(
                tenantId, FinancialDocumentType.TrialInvoice, 2026, CancellationToken.None))
            .Should().Be("INV-2026-000002");
    }

    [Fact]
    public async Task An_issued_pdf_is_never_replaced()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var document = Document(tenantId, "INV-2026-000001", "payment:pay-1");
        await _documents.InsertAsync(document, CancellationToken.None);

        (await _documents.TryRecordPdfAsync(
                tenantId, document.ItemId, "store-1", "hash-1", 100,
                DateTime.UtcNow, CancellationToken.None))
            .Should().BeTrue();

        // The second attempt loses, whether it came from a retry, a concurrent worker or a redeployed
        // template. The stored hash names the file the subscriber was sent, and replacing it would
        // break the one guarantee the hash exists to give.
        (await _documents.TryRecordPdfAsync(
                tenantId, document.ItemId, "store-2", "hash-2", 200,
                DateTime.UtcNow, CancellationToken.None))
            .Should().BeFalse();

        var stored = await _documents.GetAsync(tenantId, document.ItemId, CancellationToken.None);
        stored!.Delivery.StorageId.Should().Be("store-1");
        stored.Delivery.ContentHash.Should().Be("hash-1");
    }

    [Fact]
    public async Task Only_one_worker_records_the_email()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var document = Document(tenantId, "INV-2026-000001", "payment:pay-1");
        await _documents.InsertAsync(document, CancellationToken.None);
        await _documents.TryRecordPdfAsync(
            tenantId, document.ItemId, "store-1", "hash-1", 100,
            DateTime.UtcNow, CancellationToken.None);

        (await _documents.TryRecordEmailAsync(
                tenantId, document.ItemId, DateTime.UtcNow, CancellationToken.None))
            .Should().BeTrue();
        (await _documents.TryRecordEmailAsync(
                tenantId, document.ItemId, DateTime.UtcNow, CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_resends_collapse_onto_one_generation()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var document = Document(tenantId, "INV-2026-000001", "payment:pay-1");
        await _documents.InsertAsync(document, CancellationToken.None);
        await _documents.TryRecordPdfAsync(
            tenantId, document.ItemId, "store-1", "hash-1", 100,
            DateTime.UtcNow, CancellationToken.None);

        // A document whose mail was claimed and whose outcome was never established: the state a
        // resend exists for, and the only state a reopening is allowed to act on.
        await _documents.TryRecordMailRequestedAsync(
            tenantId, document.ItemId, $"document-mail:{document.ItemId}",
            DateTime.UtcNow, CancellationToken.None);
        await _documents.RecordDeliveryFailureAsync(
            tenantId, document.ItemId, "document_mail_outcome_unknown", 1,
            DateTime.UtcNow, CancellationToken.None);

        // Sixteen people clicking resend at once, or one person clicking it sixteen times.
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            _documents.TryReopenDeliveryAsync(tenantId, document.ItemId, CancellationToken.None)));

        var reopened = outcomes.Where(outcome => outcome is { JoinedPending: false }).ToList();

        // Exactly one reopening, and the rest join it. The filter is what makes that atomic: the first
        // write flips the document into the outstanding state, so every later one stops matching.
        reopened.Should().ContainSingle();
        reopened[0]!.Value.Generation.Should().Be(1);

        outcomes.Where(outcome => outcome is { JoinedPending: true })
            .Should().HaveCount(15)
            .And.OnlyContain(outcome => outcome!.Value.Generation == 1);

        // One generation on the document, so one queue key, so one send. Sixteen generations would be
        // sixteen queue items sharing one mail claim: the first would send and fifteen would find the
        // claim taken and send nothing.
        var reloaded = await _documents.GetAsync(tenantId, document.ItemId, CancellationToken.None);

        reloaded!.Delivery.ResendCount.Should().Be(1);
        reloaded.Delivery.MailRequestedAtUtc.Should().BeNull();
        reloaded.Delivery.State.Should().Be(FinancialDocumentDeliveryState.Generated);
    }

    [Fact]
    public async Task A_resend_of_a_document_still_waiting_to_be_sent_mints_no_generation()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var document = Document(tenantId, "INV-2026-000001", "payment:pay-1");
        await _documents.InsertAsync(document, CancellationToken.None);

        // Issued and never sent, so a send is already coming. Asking to resend is asking for something
        // already about to happen.
        var outcome = await _documents.TryReopenDeliveryAsync(
            tenantId, document.ItemId, CancellationToken.None);

        outcome.Should().NotBeNull();
        outcome!.Value.JoinedPending.Should().BeTrue();

        // Generation zero: the first delivery's own occurrence, already queued under the bare key.
        outcome.Value.Generation.Should().Be(0);

        (await _documents.GetAsync(tenantId, document.ItemId, CancellationToken.None))!
            .Delivery.ResendCount.Should().Be(0);
    }

    [Fact]
    public async Task A_resend_after_a_send_completed_gets_its_own_generation()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var document = Document(tenantId, "INV-2026-000001", "payment:pay-1");
        await _documents.InsertAsync(document, CancellationToken.None);
        await _documents.TryRecordPdfAsync(
            tenantId, document.ItemId, "store-1", "hash-1", 100,
            DateTime.UtcNow, CancellationToken.None);
        await _documents.TryRecordEmailAsync(
            tenantId, document.ItemId, DateTime.UtcNow, CancellationToken.None);

        var first = await _documents.TryReopenDeliveryAsync(
            tenantId, document.ItemId, CancellationToken.None);

        first!.Value.JoinedPending.Should().BeFalse();
        first.Value.Generation.Should().Be(1);

        // And straight away again, before that one has been sent: joined, not a second generation.
        var second = await _documents.TryReopenDeliveryAsync(
            tenantId, document.ItemId, CancellationToken.None);

        second!.Value.JoinedPending.Should().BeTrue();
        second.Value.Generation.Should().Be(1);
    }

    [Fact]
    public async Task The_delivery_sweep_finds_only_what_is_outstanding()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var pending = Document(tenantId, "INV-2026-000001", "payment:pay-1");
        var delivered = Document(tenantId, "INV-2026-000002", "payment:pay-2");
        var exhausted = Document(tenantId, "INV-2026-000003", "payment:pay-3");
        exhausted.Delivery.AttemptCount = 8;

        foreach (var document in new[] { pending, delivered, exhausted })
        {
            await _documents.InsertAsync(document, CancellationToken.None);
        }

        await _documents.TryRecordPdfAsync(
            tenantId, delivered.ItemId, "store-2", "hash", 10,
            DateTime.UtcNow, CancellationToken.None);
        await _documents.TryRecordEmailAsync(
            tenantId, delivered.ItemId, DateTime.UtcNow, CancellationToken.None);

        var outstanding = await _documents.ListUndeliveredAsync(
            tenantId, 8, 50, CancellationToken.None);

        outstanding.Select(document => document.DocumentNumber)
            .Should().BeEquivalentTo(["INV-2026-000001"]);
    }

    [Fact]
    public async Task A_full_refund_status_is_never_walked_back_to_a_partial_one()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var document = Document(tenantId, "INV-2026-000001", "payment:pay-1");
        await _documents.InsertAsync(document, CancellationToken.None);

        await _documents.TrySetRefundStatusAsync(
            tenantId, document.ItemId, FinancialDocumentStatus.Refunded, CancellationToken.None);

        // A late-arriving partial refund notification must not undo the record of a full one.
        await _documents.TrySetRefundStatusAsync(
            tenantId,
            document.ItemId,
            FinancialDocumentStatus.PartiallyRefunded,
            CancellationToken.None);

        (await _documents.GetAsync(tenantId, document.ItemId, CancellationToken.None))!
            .Status.Should().Be(FinancialDocumentStatus.Refunded);
    }

    [Fact]
    public async Task A_listing_is_scoped_to_one_organization_and_paged_by_its_cursor()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        for (var day = 1; day <= 3; day++)
        {
            var mine = Document(tenantId, $"INV-2026-{day:D6}", $"payment:mine-{day}");
            mine.IssuedAtUtc = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);
            await _documents.InsertAsync(mine, CancellationToken.None);

            var theirs = Document(tenantId, $"INV-2026-1{day:D5}", $"payment:theirs-{day}");
            theirs.OrganizationId = "org-2";
            theirs.IssuedAtUtc = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);
            await _documents.InsertAsync(theirs, CancellationToken.None);
        }

        var page = await _documents.ListAsync(
            tenantId, "org-1", null, null, null, null, null, 2, null, CancellationToken.None);

        page.Items.Should().HaveCount(2);
        page.HasMore.Should().BeTrue();
        page.Items.Should().AllSatisfy(item => item.OrganizationId.Should().Be("org-1"));

        var last = page.Items[^1];
        var next = await _documents.ListAsync(
            tenantId, "org-1", null, null, null, null, null, 2,
            new FinancialDocumentCursor(last.IssuedAtUtc, last.ItemId),
            CancellationToken.None);

        next.Items.Should().ContainSingle();
        next.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task A_billing_profile_write_keeps_the_contacts_the_money_path_recorded()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _profiles.UpsertAsync(
            new SubscriptionBillingProfile
            {
                TenantId = tenantId,
                OrganizationId = "org-1",
                LegalName = "Northwind Trading AG",
                BillingContactName = "Ada Byron",
                BillingContactEmail = "ada@northwind.example"
            },
            CancellationToken.None);

        await _profiles.RecordContactAsync(
            tenantId,
            "org-1",
            new BillingContact { UserId = "user-7", Name = "Grace Hopper", Email = "grace@x.test" },
            CancellationToken.None);

        // An authoring request knows nothing about the contacts, so a whole-document replace would
        // silently delete the names documents use to say who initiated a change.
        await _profiles.UpsertAsync(
            new SubscriptionBillingProfile
            {
                TenantId = tenantId,
                OrganizationId = "org-1",
                LegalName = "Renamed Holdings SA",
                BillingContactName = "Ada Byron",
                BillingContactEmail = "ada@northwind.example"
            },
            CancellationToken.None);

        var stored = await _profiles.GetAsync(tenantId, "org-1", CancellationToken.None);

        stored!.LegalName.Should().Be("Renamed Holdings SA");
        stored.Contacts.Should().ContainSingle()
            .Which.Name.Should().Be("Grace Hopper");

        // Recording the same user again replaces rather than duplicates: their name may have changed.
        await _profiles.RecordContactAsync(
            tenantId,
            "org-1",
            new BillingContact { UserId = "user-7", Name = "G. Hopper", Email = "grace@x.test" },
            CancellationToken.None);

        (await _profiles.GetAsync(tenantId, "org-1", CancellationToken.None))!
            .Contacts.Should().ContainSingle().Which.Name.Should().Be("G. Hopper");
    }

    [Fact]
    public async Task One_organization_can_only_have_one_profile()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = await _profiles.UpsertAsync(
            new SubscriptionBillingProfile
            {
                TenantId = tenantId,
                OrganizationId = "org-1",
                LegalName = "First",
                BillingContactName = "Ada",
                BillingContactEmail = "ada@x.test"
            },
            CancellationToken.None);

        var second = await _profiles.UpsertAsync(
            new SubscriptionBillingProfile
            {
                TenantId = tenantId,
                OrganizationId = "org-1",
                LegalName = "Second",
                BillingContactName = "Ada",
                BillingContactEmail = "ada@x.test"
            },
            CancellationToken.None);

        second.ItemId.Should().Be(first.ItemId);
        second.LegalName.Should().Be("Second");
        second.Version.Should().BeGreaterThan(first.Version);
    }

    private static SubscriptionFinancialDocument Document(
        string tenantId,
        string documentNumber,
        string sourceKey) =>
        new()
        {
            DocumentNumber = documentNumber,
            DocumentType = FinancialDocumentType.Invoice,
            IssuedAtUtc = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc),
            TenantId = tenantId,
            OrganizationId = "org-1",
            SubscriptionId = "sub-1",
            SourceKey = sourceKey,
            CurrencyCode = "CHF",
            Amounts = new FinancialDocumentAmounts { TotalMinor = 100_000 }
        };
}
