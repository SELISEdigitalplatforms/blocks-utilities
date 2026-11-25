namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Work keys for pushing one meter's terms to a plan's subscribers:
/// <c>{prefix}/{planId}/{correlationId}/{meterKey}</c>.
/// </summary>
/// <remarks>
/// Unique per save, because the queue admits a key once even after it has run. The meter key is
/// last and taken whole, since a meter key may contain anything, the separator included.
/// </remarks>
public static class PlanMeterWorkKey
{
    private const char Separator = '/';

    public static string For(string prefix, string planId, string correlationId, string meterKey) =>
        string.Join(Separator, prefix, planId, correlationId, meterKey);

    /// <summary>The meter key a <see cref="For"/> key ends with, or null when the key is not one.</summary>
    public static string? MeterKeyOf(string prefix, string workKey)
    {
        var parts = workKey.Split(Separator, 4);

        return parts.Length == 4 && parts[0] == prefix && parts[3].Length > 0 ? parts[3] : null;
    }
}
