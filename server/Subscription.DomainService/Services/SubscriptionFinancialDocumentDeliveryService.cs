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

            if (!await PublishMailAsync(document, storageId, cancellationToken))
            {
                // No address to post to. Recorded against a budget of one attempt, because retrying
                // cannot conjure an email address — so this stops being swept and says why, while the
                // PDF stays downloadable. Deliberately not recorded as delivered: nothing was.
                await _documents.RecordDeliveryFailureAsync(
                    tenantId,
                    documentId,
                    "document_no_recipient",
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
    /// Renders the PDF, stores it, and records where it went.
    /// </summary>
    /// <returns>
    /// The storage id in force — this render's, or the one a concurrent worker stored first.
    /// </returns>
    /// <remarks>
    /// The storage id is the document id, so the write is idempotent by address: two workers racing
    /// overwrite the same object with bytes rendered from the same immutable document rather than
    /// leaving two files and a question about which one the hash describes.
    /// <para>
    /// The hash is taken over exactly the bytes that were stored, before storing them, and recorded
    /// only if this worker won the race to record. A loser re-reads and defers to the winner, because
    /// the recorded hash has to describe the file the subscriber was actually sent.
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

        var stored = await _files.SaveAsync(
            document.ItemId,
            FileNameFor(document),
            content,
            cancellationToken);

        if (!stored)
        {
            return null;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));

        var recorded = await _documents.TryRecordPdfAsync(
            document.TenantId,
            document.ItemId,
            document.ItemId,
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

            return document.ItemId;
        }

        var current = await _documents.GetAsync(
            document.TenantId,
            document.ItemId,
            cancellationToken);

        return current?.Delivery.StorageId;
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
    /// <returns>False when there was nobody to send to.</returns>
    private async Task<bool> PublishMailAsync(
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

            return false;
        }

        var money = new FinancialDocumentMoneyFormatter(_currency, document.CurrencyCode);

        var context = new Dictionary<string, string>
        {
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

        return true;
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
