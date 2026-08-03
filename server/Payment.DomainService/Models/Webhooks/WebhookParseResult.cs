namespace Payment.DomainService.Models.Webhooks;

/// <summary>Outcome of reading a raw webhook request into events, before any of them are trusted.</summary>
public sealed class WebhookParseResult
{
    private WebhookParseResult(
        IReadOnlyList<ParsedWebhookEvent> events,
        string? rejectionReason)
    {
        Events = events;
        RejectionReason = rejectionReason;
    }

    public IReadOnlyList<ParsedWebhookEvent> Events { get; }

    /// <summary>Safe, non-echoing reason the body was refused; <see langword="null"/> when it parsed.</summary>
    public string? RejectionReason { get; }

    public bool IsValid => RejectionReason == null;

    public static WebhookParseResult Parsed(IReadOnlyList<ParsedWebhookEvent> events) =>
        new(events, null);

    public static WebhookParseResult Malformed(string reason) =>
        new([], reason);
}
