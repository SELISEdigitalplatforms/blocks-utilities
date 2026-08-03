namespace Payment.DomainService.Services;

public interface IPaymentWebhookIntakeService
{
    /// <summary>
    /// Admits an inbound webhook request for one provider. Validates and durably records it;
    /// payment state is only ever changed later, by the worker.
    /// </summary>
    Task<WebhookIntakeOutcome> AcceptAsync(
        string providerName,
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken shutdownToken);
}
