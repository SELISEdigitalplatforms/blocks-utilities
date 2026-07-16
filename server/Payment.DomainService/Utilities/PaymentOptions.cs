namespace Payment.DomainService.Utilities;

public sealed class PaymentOptions
{
    public const string SectionName = "Payment";
    public int ProviderTimeoutSeconds { get; set; } = 15;
    public int ProcessingLeaseSeconds { get; set; } = 30;
    public int DistributedLockSeconds { get; set; } = 20;
    public int DistributedLockWaitMilliseconds { get; set; } = 750;
    public int TenantRequestsPerMinute { get; set; } = 300;
    public int ActorRequestsPerMinute { get; set; } = 30;
    public int OrderRequestsPerMinute { get; set; } = 10;
    public int ProviderCacheSeconds { get; set; } = 120;
    public int OutboxBatchSize { get; set; } = 50;
    public int OutboxPollSeconds { get; set; } = 5;
    public int OutboxLeaseSeconds { get; set; } = 30;
    public int OutboxMaxAttempts { get; set; } = 10;
    public int RecoveryPollSeconds { get; set; } = 30;
    public int CheckoutCallbackStateLifetimeMinutes { get; set; } = 60;
    public int WebhookBatchSize { get; set; } = 50;
    public int WebhookLeaseSeconds { get; set; } = 30;
    public int WebhookMaxAttempts { get; set; } = 10;
    public int MaximumWebhookBodyBytes { get; set; } = 262_144;
    public int MaximumReturnParameterLength { get; set; } = 8_192;
    public int ReturnRequestsPerClientPerMinute { get; set; } = 60;
    public int ReturnRequestsPerStatePerMinute { get; set; } = 12;
    public string[] TenantIds { get; set; } = [];
    public Dictionary<string, int> CurrencyMinorUnits { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BDT"] = 2,
        ["USD"] = 2,
        ["EUR"] = 2,
        ["GBP"] = 2,
        ["CHF"] = 2,
        ["JPY"] = 0,
        ["BHD"] = 3,
        ["KWD"] = 3
    };
}
