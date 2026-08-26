using System.Globalization;
using System.Security.Cryptography;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Messaging;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionFinancialDocumentDeliveryService :
    ISubscriptionFinancialDocumentDeliveryService
{
    private readonly ISubscriptionFinancialDocumentRepository _documents;
    private readonly IFinancialDocumentPdfRenderer _renderer;
    private readonly IFinancialDocumentFileStore _files;
    private readonly ICurrencyMinorUnitResolver _currency;
    private readonly IMessageClient _messages;
    private readonly IOptions<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionFinancialDocumentDeliveryService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionFinancialDocumentDeliveryService(
        ISubscriptionFinancialDocumentRepository documents,
        IFinancialDocumentPdfRenderer renderer,
        IFinancialDocumentFileStore files,
        ICurrencyMinorUnitResolver currency,
        IMessageClient messages,
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionFinancialDocumentDeliveryService> logger,
        TimeProvider? time = null)
    {
        _documents = documents;
        _renderer = renderer;
        _files = files;
        _currency = currency;
        _messages = messages;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<bool> DeliverAsync(
        string tenantId,
        string documentId,
        CancellationToken cancellationToken)
    {
        var document = await _documents.GetAsync(tenantId, documentId, cancellationToken);

        if (document is null ||
            document.Delivery.State is FinancialDocumentDeliveryState.Delivered
                or FinancialDocumentDeliveryState.Abandoned)
        {
            // Already done, already given up on, or gone. All three are finished as far as the queue
            // is concerned; only a document still waiting is work.
            return true;
        }

        try
        {
            var storageId = document.Delivery.StorageId
                ?? await RenderAndStoreAsync(document, cancellationToken);

            if (storageId is null)
            {
                await RecordFailureAsync(
                    tenantId,
                    documentId,
                    "document_pdf_unavailable",
                    cancellationToken);

                return false;
            }

            var outcome = await PublishMailAsync(document, storageId, cancellationToken);

            if (outcome is MailOutcome.NoRecipient or MailOutcome.OutcomeUnknown)
            {
                // Both stop here, and neither is recorded as delivered, because in neither case is a
                // mail known to have reached anybody. A budget of one attempt so the sweep lets go: no
                // number of retries conjures an email address, and retrying an unknown outcome is the
                // very thing that would send a second invoice. The PDF stays downloadable and the
                // reason is on the document for an operator to find.
                await _documents.RecordDeliveryFailureAsync(
                    tenantId,
                    documentId,
                    outcome == MailOutcome.NoRecipient
                        ? "document_no_recipient"
                        : "document_mail_outcome_unknown",
                    maximumAttempts: 1,
                    _time.GetUtcNow().UtcDateTime,
                    cancellationToken);

                return true;
            }

            await _documents.TryRecordEmailAsync(
                tenantId,
                documentId,
                _time.GetUtcNow().UtcDateTime,
                cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Caught rather than propagated so the attempt is counted and, eventually, abandoned. An
            // exception escaping here would leave the queue retrying a document with a template it
            // cannot render until its own attempt budget ran out, with nothing recorded on the
            // document to say why.
            _logger.LogError(
                exception,
                "A financial document could not be delivered DocumentNumber={DocumentNumber}",
                PaymentLogValue.Label(document.DocumentNumber));

            await RecordFailureAsync(
                tenantId,
                documentId,
                "document_delivery_failed",
                cancellationToken);

            return false;
        }
    }

    public async Task<int> DeliverPendingAsync(string tenantId, CancellationToken cancellationToken)
    {
        var options = _options.Value;

        var pending = await _documents.ListUndeliveredAsync(
            tenantId,
            options.DocumentDeliveryMaxAttempts,
            options.DocumentDeliveryBatchSize,
            cancellationToken);

        var delivered = 0;

        foreach (var document in pending)
        {
            if (await DeliverAsync(tenantId, document.ItemId, cancellationToken))
            {
                delivered++;
            }
        }

        return delivered;
    }

    /// <summary>
    /// Renders the PDF, stores it under its own hash, and records where it went.
    /// </summary>
    /// <returns>
    /// The storage id in force — this render's, or the one a concurrent worker stored first.
    /// </returns>
    /// <remarks>
    /// Content-addressed, and stored before it is recorded. Both halves matter.
    /// <para>
    /// The key includes the hash of the bytes, so two workers rendering the same document write to two
    /// different objects rather than overwriting each other. That is not paranoia about the template:
    /// a headless-browser PDF carries generation metadata, so two renders of one immutable document are
    /// not guaranteed to be byte-identical — and under a shared key the loser of the metadata race
    /// could replace the winner's file after the winner's hash had been recorded, leaving a document
    /// whose recorded hash described bytes that were no longer there.
    /// </para>
    /// <para>
    /// Recording happens after the bytes are written, so the key that is recorded always has its file.
    /// A crash between the two leaves an unreferenced object, which costs storage and nothing else; the
    /// reverse order would leave a document pointing at a file that does not exist.
    /// </para>
    /// <para>
    /// A loser re-reads and defers to the winner, because the recorded hash has to describe the file
    /// the subscriber was actually sent.
    /// </para>
    /// </remarks>
    private async Task<string?> RenderAndStoreAsync(
        SubscriptionFinancialDocument document,
        CancellationToken cancellationToken)
    {
        var html = FinancialDocumentHtmlTemplate.Render(
            document,
            new FinancialDocumentMoneyFormatter(_currency, document.CurrencyCode));

        var content = await _renderer.RenderAsync(html, cancellationToken);
        if (content is not { Length: > 0 })
        {
            return null;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var storageId = StorageIdFor(document, hash);

        var stored = await _files.SaveAsync(
            storageId,
            FileNameFor(document),
            content,
            cancellationToken);

        if (!stored)
        {
            return null;
        }

        var recorded = await _documents.TryRecordPdfAsync(
            document.TenantId,
            document.ItemId,
            storageId,
            hash,
            content.Length,
            _time.GetUtcNow().UtcDateTime,
            cancellationToken);

        if (recorded)
        {
            _logger.LogInformation(
                "Financial document rendered DocumentNumber={DocumentNumber} Bytes={Bytes}",
                PaymentLogValue.Label(document.DocumentNumber),
                content.Length);

            return storageId;
        }

        var current = await _documents.GetAsync(
            document.TenantId,
            document.ItemId,
            cancellationToken);

        return current?.Delivery.StorageId;
    }

    /// <summary>
    /// Where a render of this document goes: the document id, plus enough of its hash to tell two
    /// renders apart.
    /// </summary>
    /// <remarks>
    /// The document id leads so that everything belonging to one document sorts together in storage
    /// and stays traceable by eye. Sixty-four bits of hash after it is far more than enough to separate
    /// two renders of the same document, and the whole hash is recorded on the document anyway — this
    /// only has to be distinct, not self-describing.
    /// </remarks>
    private static string StorageIdFor(SubscriptionFinancialDocument document, string hash) =>
        $"{document.ItemId}-{hash[..Math.Min(hash.Length, 16)]}";

    /// <summary>
    /// What became of a document's mail on this attempt.
    /// </summary>
    /// <remarks>
    /// Three answers rather than two, because "did not send" and "may have sent" call for opposite
    /// responses and a boolean cannot tell them apart.
    /// </remarks>
    private enum MailOutcome
    {
        /// <summary>Handed to the bus by this attempt.</summary>
        Published,

        /// <summary>Nobody to send to. Retrying cannot invent an address.</summary>
        NoRecipient,

        /// <summary>
        /// Whether the mail went out is unknowable, so nothing further is sent.
        /// </summary>
        /// <remarks>
        /// Two ways to arrive here, and they are the same situation: an earlier attempt claimed the
        /// publish and never recorded the result, or this attempt's own publish threw. A throw is
        /// <em>not</em> evidence of non-delivery — a broker can accept and acknowledge a message and
        /// have the acknowledgement lost on the way back, so the client sees a timeout or a reset
        /// socket for a message that was delivered.
        /// </remarks>
        OutcomeUnknown
    }

    /// <summary>
    /// Publishes the mail command carrying the stored PDF.
    /// </summary>
    /// <remarks>
    /// The attachment is a storage reference, never bytes. The mail module fetches it, so a large
    /// invoice does not travel through the message bus and the message stays small enough to be
    /// retried cheaply.
    /// <para>
    /// A document with no recipient is not an error. The subscriber has no email recorded, the money
    /// has already moved, and the document is still issued, numbered and downloadable — mail is the
    /// one part of it that needs an address. Reported as false so the caller records why rather than
    /// recording a delivery that did not happen.
    /// </para>
    /// </remarks>
    private async Task<MailOutcome> PublishMailAsync(
        SubscriptionFinancialDocument document,
        string storageId,
        CancellationToken cancellationToken)
    {
        if (document.BillingContact.Email is not { Length: > 0 } recipient)
        {
            _logger.LogWarning(
                "A financial document has no billing contact to send to; it remains available for " +
                "download DocumentNumber={DocumentNumber}",
                PaymentLogValue.Label(document.DocumentNumber));

            return MailOutcome.NoRecipient;
        }

        var messageId = MailMessageIdFor(document);

        // The claim is the authorisation to send, and exactly one attempt ever wins it. Publishing to
        // the bus and recording that it happened are two writes with nothing joining them, so a crash
        // between them leaves a message that may or may not have gone out — and the only way to keep
        // that from becoming a second invoice in somebody's inbox is for the next attempt to find the
        // claim taken and refuse to send.
        if (!await _documents.TryRecordMailRequestedAsync(
                document.TenantId,
                document.ItemId,
                messageId,
                _time.GetUtcNow().UtcDateTime,
                cancellationToken))
        {
            // Deliberately at-most-once rather than at-least-once. The subscriber may not receive an
            // email for an invoice they can still see and download; the alternative is two identical
            // invoice emails, which reads as being billed twice and cannot be taken back. The state is
            // recorded and queryable so an operator can resend on purpose.
            _logger.LogError(
                "A financial document mail was claimed by an earlier attempt that never recorded its " +
                "outcome, so this attempt will not send in case that one did. The document is issued " +
                "and downloadable; resending is an operator decision " +
                "DocumentNumber={DocumentNumber} MessageId={MessageId}",
                PaymentLogValue.Label(document.DocumentNumber),
                PaymentLogValue.Label(messageId));

            return MailOutcome.OutcomeUnknown;
        }

        return await SendAsync(document, storageId, messageId, recipient, cancellationToken);
    }

    /// <summary>
    /// Hands the message to the bus, having already claimed the right to.
    /// </summary>
    /// <remarks>
    /// The claim is <strong>never</strong> given back here. It is tempting to release it when the
    /// publish throws and let a retry send — that was the first shape of this and it is unsound, because
    /// a throw does not mean the message was not delivered: a broker can accept and acknowledge one and
    /// have the acknowledgement lost on the way back, leaving the client holding a timeout for a message
    /// that went out. Releasing on that would put a second invoice in somebody's inbox, which is the
    /// failure this whole mechanism exists to prevent.
    /// <para>
    /// So a failed publish is recorded as an <em>unknown</em> outcome, exactly like losing the claim
    /// race, and nothing retries. Exactly-once is not available: the message envelope carries no
    /// identity the broker could deduplicate on, so at-most-once with a deliberate resend is the
    /// strongest honest guarantee.
    /// </para>
    /// </remarks>
    private async Task<MailOutcome> SendAsync(
        SubscriptionFinancialDocument document,
        string storageId,
        string messageId,
        string recipient,
        CancellationToken cancellationToken)
    {

        var money = new FinancialDocumentMoneyFormatter(_currency, document.CurrencyCode);

        var context = new Dictionary<string, string>
        {
            // Belt and braces. Sending is already at most once, so this is not what prevents a
            // duplicate — but the id costs nothing and lets a mail consumer that deduplicates catch
            // anything a future change here lets through.
            ["MessageId"] = messageId,
            ["DocumentNumber"] = document.DocumentNumber,
            ["DocumentType"] = document.DocumentType.ToString(),
            ["OrganizationName"] = document.Subscriber.LegalName,
            ["ContactName"] = document.BillingContact.Name,
            ["PlanName"] = document.Subject.PlanName,
            ["Total"] = money.Format(document.Amounts.TotalMinor),
            ["CurrencyCode"] = document.CurrencyCode,
            ["IssuedOn"] = document.IssuedAtUtc.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            ["PeriodStart"] = document.Period.LocalStart,
            ["PeriodEnd"] = document.Period.LocalEnd
        };

        try
        {
            await _messages.SendToConsumerAsync(
                new ConsumerMessage<SendMail>
                {
                    ConsumerName = SubscriptionConstants.MailQueue,
                    Payload = new SendMail
                    {
                        To = [recipient.Trim().ToLowerInvariant()],
                        Purpose = PurposeFor(document.DocumentType),
                        Language = SubscriptionConstants.DefaultMailLanguage,
                        Attachments = [storageId],
                        SubjectDataContext = new Dictionary<string, string>(context),
                        BodyDataContext = context
                    }
                });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Logged at error and reported as unknown, not as failed. The claim stays taken, so nothing
            // here or in a later sweep sends again. The document is issued, numbered and downloadable;
            // what an operator has lost is a notification, and the resend is theirs to make.
            _logger.LogError(
                exception,
                "A financial document mail could not be handed to the bus, and whether it was " +
                "delivered cannot be established, so nothing will be sent again automatically. " +
                "The document is issued and downloadable; resending is an operator decision " +
                "DocumentNumber={DocumentNumber} MessageId={MessageId}",
                PaymentLogValue.Label(document.DocumentNumber),
                PaymentLogValue.Label(messageId));

            return MailOutcome.OutcomeUnknown;
        }

        return MailOutcome.Published;
    }

    private async Task RecordFailureAsync(
        string tenantId,
        string documentId,
        string errorCode,
        CancellationToken cancellationToken) =>
        await _documents.RecordDeliveryFailureAsync(
            tenantId,
            documentId,
            errorCode,
            _options.Value.DocumentDeliveryMaxAttempts,
            _time.GetUtcNow().UtcDateTime,
            cancellationToken);

    /// <summary>
    /// The identity of this document's mail, derived rather than generated.
    /// </summary>
    /// <remarks>
    /// Derived so that every mention of this document's mail — a log line, a payload, an operator's
    /// deliberate resend — names the same thing. A generated id would make a duplicate untraceable
    /// even when one is discovered.
    /// </remarks>
    public static string MailMessageIdFor(SubscriptionFinancialDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Delivery.MailMessageId is { Length: > 0 } recorded
            ? recorded
            : $"document-mail:{document.ItemId}";
    }

    private static string PurposeFor(FinancialDocumentType documentType) =>
        documentType switch
        {
            FinancialDocumentType.TrialInvoice => SubscriptionConstants.TrialInvoiceMailPurpose,
            FinancialDocumentType.CreditNote => SubscriptionConstants.CreditNoteMailPurpose,
            _ => SubscriptionConstants.InvoiceMailPurpose
        };

    /// <summary>
    /// The filename the subscriber sees, built from the document number.
    /// </summary>
    /// <remarks>
    /// Sanitised even though the number is generated here rather than supplied, because it is echoed
    /// into a <c>Content-Disposition</c> header on download and a filename that trusts its input is a
    /// filename that will eventually be given somebody else's.
    /// </remarks>
    public static string FileNameFor(SubscriptionFinancialDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var safe = new string([.. document.DocumentNumber
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')]);

        return $"{(safe.Length == 0 ? "document" : safe[..Math.Min(safe.Length, 60)])}.pdf";
    }
}
