using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Messaging;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Rendering an issued document, storing it, and putting it in the post.
/// </summary>
/// <remarks>
/// The rule under test throughout: delivery may fail as often as it likes and must never touch the
/// document's money or its number. The second rule: once a PDF exists it is the document, so nothing
/// re-renders it — not a retry, not a redeployed template.
/// </remarks>
public sealed class SubscriptionFinancialDocumentDeliveryTests
{
    private const string TenantId = "tenant-1";

    private readonly FinancialDocumentLedgerFake _documents = new();
    private readonly Mock<IFinancialDocumentPdfRenderer> _renderer = new();
    private readonly Mock<IFinancialDocumentFileStore> _files = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _currency = new();
    private readonly Mock<IMessageClient> _messages = new();
    private readonly List<SendMail> _sent = [];

    public SubscriptionFinancialDocumentDeliveryTests()
    {
        _renderer
            .Setup(renderer => renderer.RenderAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("%PDF-1.7 pretend"));

        _files
            .Setup(files => files.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _currency
            .Setup(currency => currency.TryConvertBack(
                It.IsAny<long>(),
                It.IsAny<string>(),
                out It.Ref<decimal>.IsAny))
            .Returns((long minor, string _, out decimal amount) =>
            {
                amount = minor / 100m;

                return true;
            });

        _currency
            .Setup(currency => currency.TryConvert(
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                out It.Ref<long>.IsAny))
            .Returns((decimal amount, string _, out long minor) =>
            {
                minor = (long)(amount * 100);

                return true;
            });

        _messages
            .Setup(messages => messages.SendToConsumerAsync(
                It.IsAny<ConsumerMessage<SendMail>>()))
            .Callback((ConsumerMessage<SendMail> message) => _sent.Add(message.Payload))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Delivering_stores_the_pdf_with_its_hash_and_posts_the_mail()
    {
        var document = await IssuedAsync();

        (await Delivery().DeliverAsync(TenantId, document.ItemId, CancellationToken.None))
            .Should().BeTrue();

        var stored = _documents.Documents.Single();
        stored.Delivery.State.Should().Be(FinancialDocumentDeliveryState.Delivered);
        // Content-addressed: the document id, then enough of the hash to keep two renders of the same
        // document from overwriting each other.
        stored.Delivery.StorageId.Should().StartWith(document.ItemId + "-");
        stored.Delivery.StorageId.Should().Be(
            $"{document.ItemId}-{stored.Delivery.ContentHash![..16]}");

        // The hash is over exactly the bytes that were stored, which is the only thing that makes it
        // evidence about the file the subscriber holds.
        stored.Delivery.ContentHash.Should().Be(
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes("%PDF-1.7 pretend"))));
        stored.Delivery.ContentLength.Should().Be(16);

        var mail = _sent.Should().ContainSingle().Subject;
        mail.To.Should().ContainSingle().Which.Should().Be("ada@northwind.example");
        mail.Purpose.Should().Be(SubscriptionConstants.InvoiceMailPurpose);

        // A storage reference, never bytes: a large invoice must not travel through the bus, and the
        // message has to stay small enough to retry cheaply.
        mail.Attachments.Should().ContainSingle().Which.Should().Be(stored.Delivery.StorageId);
        mail.BodyDataContext["DocumentNumber"].Should().Be("INV-2026-000001");

        // The identity a consumer suppresses a repeat by. Derived from the document, so a republished
        // mail after a crash carries the same value rather than looking like a second invoice.
        mail.BodyDataContext["MessageId"].Should().Be($"document-mail:{document.ItemId}");
    }

    [Fact]
    public async Task A_delivered_document_is_never_rendered_or_posted_again()
    {
        var document = await IssuedAsync();
        var delivery = Delivery();

        await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None);
        await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None);

        _sent.Should().ContainSingle();
        _renderer.Verify(
            renderer => renderer.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_document_whose_pdf_exists_resumes_at_the_mail()
    {
        var document = await IssuedAsync();

        // The crash this covers: the PDF was stored and the mail publish never happened.
        await _documents.TryRecordPdfAsync(
            TenantId, document.ItemId, "already-stored", "hash", 10,
            DateTime.UtcNow, CancellationToken.None);

        await Delivery().DeliverAsync(TenantId, document.ItemId, CancellationToken.None);

        // Not re-rendered, because the stored file is the document — and the mail carries the file
        // that was actually stored rather than one this attempt made.
        _renderer.Verify(
            renderer => renderer.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _sent.Should().ContainSingle().Which.Attachments
            .Should().ContainSingle().Which.Should().Be("already-stored");
    }

    [Fact]
    public async Task A_render_that_fails_counts_an_attempt_and_asks_to_be_retried()
    {
        var document = await IssuedAsync();
        _renderer
            .Setup(renderer => renderer.RenderAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        (await Delivery().DeliverAsync(TenantId, document.ItemId, CancellationToken.None))
            .Should().BeFalse();

        var stored = _documents.Documents.Single();
        stored.Delivery.AttemptCount.Should().Be(1);
        stored.Delivery.LastErrorCode.Should().Be("document_pdf_unavailable");

        // The money is untouched. A template that cannot render is an operational problem, not
        // unbilled revenue.
        stored.Amounts.TotalMinor.Should().Be(95_930);
        stored.DocumentNumber.Should().Be("INV-2026-000001");
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_renderer_that_throws_is_recorded_rather_than_allowed_to_escape()
    {
        var document = await IssuedAsync();
        _renderer
            .Setup(renderer => renderer.RenderAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no browser"));

        (await Delivery().DeliverAsync(TenantId, document.ItemId, CancellationToken.None))
            .Should().BeFalse();

        _documents.Documents.Single().Delivery.LastErrorCode
            .Should().Be("document_delivery_failed");
    }

    [Fact]
    public async Task A_document_is_abandoned_once_its_attempts_are_spent()
    {
        var document = await IssuedAsync();
        _renderer
            .Setup(renderer => renderer.RenderAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var delivery = Delivery(maximumAttempts: 2);
        await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None);
        await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None);

        // Said in the state, not only in a counter: an operator looking for documents that reached
        // nobody should not have to know what the configured limit is to find them.
        _documents.Documents.Single().Delivery.State
            .Should().Be(FinancialDocumentDeliveryState.Abandoned);

        // And it stops being work.
        (await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None))
            .Should().BeTrue();
        _documents.Documents.Single().Delivery.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task A_document_with_no_recipient_is_still_rendered_and_left_downloadable()
    {
        var document = await IssuedAsync(contactEmail: null);

        (await Delivery().DeliverAsync(TenantId, document.ItemId, CancellationToken.None))
            .Should().BeTrue();

        _sent.Should().BeEmpty();

        // Not delivered — nothing was — and not left to be swept forever either, because retrying
        // cannot conjure an email address. The reason is on the document and the PDF is still there.
        var stored = _documents.Documents.Single();
        stored.Delivery.State.Should().Be(FinancialDocumentDeliveryState.Abandoned);
        stored.Delivery.LastErrorCode.Should().Be("document_no_recipient");
        stored.Delivery.StorageId.Should().NotBeNull();
        stored.Delivery.ContentHash.Should().NotBeNull();
    }

    [Fact]
    public async Task A_credit_note_and_a_trial_invoice_are_posted_under_their_own_purposes()
    {
        var creditNote = await IssuedAsync(FinancialDocumentType.CreditNote);
        var trial = await IssuedAsync(FinancialDocumentType.TrialInvoice);

        var delivery = Delivery();
        await delivery.DeliverAsync(TenantId, creditNote.ItemId, CancellationToken.None);
        await delivery.DeliverAsync(TenantId, trial.ItemId, CancellationToken.None);

        _sent.Select(mail => mail.Purpose).Should().BeEquivalentTo(
        [
            SubscriptionConstants.CreditNoteMailPurpose,
            SubscriptionConstants.TrialInvoiceMailPurpose
        ]);
    }

    [Fact]
    public async Task The_sweep_delivers_everything_outstanding_and_leaves_the_rest_alone()
    {
        var first = await IssuedAsync();
        var second = await IssuedAsync();
        await Delivery().DeliverAsync(TenantId, first.ItemId, CancellationToken.None);

        _sent.Clear();

        (await Delivery().DeliverPendingAsync(TenantId, CancellationToken.None)).Should().Be(1);
        _sent.Should().ContainSingle().Which.BodyDataContext["DocumentNumber"]
            .Should().Be(second.DocumentNumber);
    }

    [Fact]
    public async Task A_missing_document_is_finished_work_rather_than_a_failure()
    {
        (await Delivery().DeliverAsync(TenantId, "gone", CancellationToken.None))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Two_workers_rendering_at_once_leave_the_recorded_hash_describing_the_stored_file()
    {
        var document = await IssuedAsync();

        // Two renders of one immutable document, byte-different. Not a contrived case: a
        // headless-browser PDF carries generation metadata, so this is what concurrent delivery
        // actually produces.
        var first = Encoding.UTF8.GetBytes("%PDF-1.7 rendered at 10:00:00");
        var second = Encoding.UTF8.GetBytes("%PDF-1.7 rendered at 10:00:01");
        var renders = new Queue<byte[]>([first, second]);

        _renderer
            .Setup(renderer => renderer.RenderAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => renders.Dequeue());

        var written = new Dictionary<string, byte[]>();
        _files
            .Setup(files => files.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string _, byte[] content, CancellationToken _) =>
            {
                written[id] = content;

                return true;
            });

        var delivery = Delivery();

        await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None);

        var firstKey = _documents.Documents.Single().Delivery.StorageId;
        var firstHash = _documents.Documents.Single().Delivery.ContentHash;

        // A second render of the same document reaching storage. In production this is the worker that
        // read the document before the first one recorded anything; here the recorded state is cleared
        // to put the service in the same position.
        document.Delivery.StorageId = null;
        document.Delivery.ContentHash = null;
        document.Delivery.State = FinancialDocumentDeliveryState.Pending;

        await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None);

        // Two objects, and the first is untouched. Under a shared key — the document id alone — the
        // second render would have overwritten it, and a hash recorded against the first would then
        // have described bytes that were no longer there.
        written.Should().HaveCount(2);
        written[firstKey!].Should().Equal(first);

        var stored = _documents.Documents.Single();
        stored.Delivery.StorageId.Should().NotBe(firstKey);

        // The invariant, for whichever render ends up recorded: the bytes at the recorded key hash to
        // the recorded hash.
        Convert.ToHexStringLower(SHA256.HashData(written[stored.Delivery.StorageId!]))
            .Should().Be(stored.Delivery.ContentHash);
        Convert.ToHexStringLower(SHA256.HashData(written[firstKey!]))
            .Should().Be(firstHash);
    }

    [Fact]
    public async Task A_mail_whose_outcome_is_unknown_is_never_sent_a_second_time()
    {
        var document = await IssuedAsync();
        var delivery = Delivery();

        await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None);

        var stored = _documents.Documents.Single();
        stored.Delivery.MailMessageId.Should().Be($"document-mail:{document.ItemId}");

        // A crash between publishing and recording that it was published. The claim survives, the
        // delivered state does not — so the next attempt cannot tell whether the first message went
        // out.
        stored.Delivery.State = FinancialDocumentDeliveryState.Generated;
        stored.Delivery.EmailedAtUtc = null;

        (await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None))
            .Should().BeTrue();

        // Nothing sent. At most once, deliberately: a subscriber may miss an email for an invoice they
        // can still see and download, and the alternative is two identical invoice emails, which reads
        // as being billed twice and cannot be taken back.
        _sent.Should().HaveCount(1);

        // And it stops being swept, with the reason on the document, because retrying is the very thing
        // that would send the second copy. Not recorded as delivered: nothing is known to have arrived.
        stored.Delivery.State.Should().Be(FinancialDocumentDeliveryState.Abandoned);
        stored.Delivery.LastErrorCode.Should().Be("document_mail_outcome_unknown");
        stored.Delivery.EmailedAtUtc.Should().BeNull();

        // The PDF is untouched by any of this, which is what makes the missed email recoverable.
        stored.Delivery.StorageId.Should().NotBeNull();
    }

    [Fact]
    public async Task A_publish_that_threw_gives_the_claim_back_so_the_retry_does_send()
    {
        var document = await IssuedAsync();

        var attempts = 0;
        _messages
            .Setup(messages => messages.SendToConsumerAsync(
                It.IsAny<ConsumerMessage<SendMail>>()))
            .Callback((ConsumerMessage<SendMail> message) =>
            {
                attempts++;

                if (attempts == 1)
                {
                    throw new InvalidOperationException("the bus is unreachable");
                }

                _sent.Add(message.Payload);
            })
            .Returns(Task.CompletedTask);

        var delivery = Delivery();

        (await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None))
            .Should().BeFalse();

        var stored = _documents.Documents.Single();

        // A publish that threw did not happen, so the claim is released rather than kept. Keeping it
        // would cost the subscriber their invoice email permanently over one unreachable bus — the
        // retry would find its own claim standing and refuse to send.
        stored.Delivery.MailRequestedAtUtc.Should().BeNull();
        _sent.Should().BeEmpty();

        (await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None))
            .Should().BeTrue();

        _sent.Should().ContainSingle();
        stored.Delivery.State.Should().Be(FinancialDocumentDeliveryState.Delivered);
    }

    [Fact]
    public async Task Only_one_of_two_workers_racing_the_same_document_sends_anything()
    {
        var document = await IssuedAsync();
        var delivery = Delivery();

        // Both read the document before either recorded a thing, which is the race. The claim is what
        // separates them: it is a compare-and-set, so exactly one wins it and only the winner sends.
        var first = await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None);

        var stored = _documents.Documents.Single();
        stored.Delivery.State = FinancialDocumentDeliveryState.Generated;
        stored.Delivery.EmailedAtUtc = null;

        var second = await delivery.DeliverAsync(TenantId, document.ItemId, CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeTrue();
        _sent.Should().ContainSingle();
    }

    private ISubscriptionFinancialDocumentDeliveryService Delivery(int maximumAttempts = 8) =>
        new SubscriptionFinancialDocumentDeliveryService(
            _documents,
            _renderer.Object,
            _files.Object,
            _currency.Object,
            _messages.Object,
            Options.Create(new SubscriptionOptions
            {
                DocumentDeliveryMaxAttempts = maximumAttempts,
                DocumentDeliveryBatchSize = 25
            }),
            NullLogger<SubscriptionFinancialDocumentDeliveryService>.Instance);

    private async Task<SubscriptionFinancialDocument> IssuedAsync(
        FinancialDocumentType documentType = FinancialDocumentType.Invoice,
        string? contactEmail = "ada@northwind.example")
    {
        var number = documentType == FinancialDocumentType.CreditNote
            ? $"CRN-2026-{_documents.Documents.Count + 1:D6}"
            : $"INV-2026-{_documents.Documents.Count + 1:D6}";

        var document = new SubscriptionFinancialDocument
        {
            DocumentNumber = number,
            DocumentType = documentType,
            IssuedAtUtc = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc),
            TenantId = TenantId,
            OrganizationId = "org-1",
            SubscriptionId = "sub-1",
            SourceKey = $"payment:{Guid.NewGuid()}",
            CurrencyCode = "CHF",
            Subscriber = new FinancialDocumentParty { LegalName = "Northwind Trading AG" },
            BillingContact = new FinancialDocumentPerson
            {
                Name = "Ada Byron",
                Email = contactEmail
            },
            InitiatedBy = new FinancialDocumentPerson { Name = "System renewal" },
            Subject = new FinancialDocumentSubject { PlanCode = "pro", PlanName = "Pro" },
            Period = new FinancialDocumentPeriod
            {
                LocalStart = "2026-01-01",
                LocalEnd = "2027-01-01",
                TimeZoneId = "Europe/Zurich"
            },
            Amounts = new FinancialDocumentAmounts
            {
                GrossSubtotalMinor = 100_000,
                NetSubtotalMinor = 90_000,
                TaxAmountMinor = 6_930,
                TotalMinor = 95_930
            }
        };

        await _documents.InsertAsync(document, CancellationToken.None);

        return document;
    }
}
