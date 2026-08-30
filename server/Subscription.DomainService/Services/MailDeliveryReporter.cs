using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Messaging;
using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Services;

/// <summary>
/// Writes the mail delivery history, and never lets writing it break the mail.
/// </summary>
public sealed class MailDeliveryReporter : IMailDeliveryReporter
{
    /// <summary>
    /// How long a report is kept.
    /// </summary>
    /// <remarks>
    /// Long enough to answer "what did we send that customer last quarter" during a billing
    /// dispute, and short enough that recipient addresses and billing figures are not kept
    /// indefinitely for no stated purpose. The TTL index enforces it; nothing sweeps.
    /// </remarks>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    // Written for a human reading the collection during an incident, not for a machine: indented,
    // and without the escaping that turns every address in a payload into an unreadable string.
    private static readonly JsonSerializerOptions PayloadFormat = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IMailDeliveryReportRepository _reports;
    private readonly TimeProvider _time;
    private readonly ILogger<MailDeliveryReporter> _logger;

    public MailDeliveryReporter(
        IMailDeliveryReportRepository reports,
        TimeProvider time,
        ILogger<MailDeliveryReporter> logger)
    {
        _reports = reports;
        _time = time;
        _logger = logger;
    }

    public async Task RecordAsync(
        MailDeliveryReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var payloadJson = request.Payload is null
                ? string.Empty
                : JsonSerializer.Serialize(request.Payload, PayloadFormat);

            await _reports.AddAsync(
                new MailDeliveryReport
                {
                    TenantId = request.TenantId,
                    OrganizationId = request.OrganizationId,
                    Source = request.Source,
                    Outcome = request.Outcome,
                    SubjectId = request.SubjectId,
                    SubjectReference = request.SubjectReference,
                    MailMessageId = request.MailMessageId,
                    ConsumerName = request.ConsumerName,
                    Purpose = request.Payload?.Purpose ?? string.Empty,
                    Language = request.Payload?.Language ?? string.Empty,
                    To = [.. request.Payload?.To ?? []],
                    Cc = [.. request.Payload?.Cc ?? []],
                    Bcc = [.. request.Payload?.Bcc ?? []],
                    Attachments = [.. request.Payload?.Attachments ?? []],
                    PayloadJson = payloadJson,
                    PayloadHash = Hash(payloadJson),
                    PayloadLength = payloadJson.Length,
                    ErrorCode = request.ErrorCode,
                    ErrorMessage = request.ErrorMessage,
                    CorrelationId = request.CorrelationId,
                    CreatedAtUtc = now,
                    PurgeAtUtc = now.Add(Retention)
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            // Deliberately swallowed, including cancellation. The mail's outcome was decided before
            // this was called, and rethrowing would unwind a send that already happened -- on the
            // document path that means releasing a claim and risking a duplicate invoice. What is
            // lost is a row in a history collection, which is why this is logged loudly enough to
            // notice and quiet enough to ignore at three in the morning.
            _logger.LogWarning(
                exception,
                "A mail delivery report could not be written; the mail itself is unaffected " +
                "Source={Source} Outcome={Outcome} SubjectReference={SubjectReference}",
                request.Source,
                request.Outcome,
                PaymentLogValue.Label(request.SubjectReference ?? request.SubjectId));
        }
    }

    private static string Hash(string payloadJson)
    {
        if (payloadJson.Length == 0)
        {
            return string.Empty;
        }

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)))
            .ToLower(CultureInfo.InvariantCulture);
    }
}
