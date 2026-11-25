using System.Globalization;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// The work key of a <see cref="Enums.SubscriptionWorkType.ChargeSettlementWatch"/> item, which is
/// the only place the payment it watches and the check number are carried.
/// </summary>
public static class ChargeSettlementWatchKey
{
    private const string Prefix = "settlement-watch:";

    public static string For(string paymentDetailId, int checkNumber) =>
        $"{Prefix}{paymentDetailId}:{checkNumber.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string? workKey, out string paymentDetailId, out int checkNumber)
    {
        paymentDetailId = string.Empty;
        checkNumber = 0;

        if (workKey is null || !workKey.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var separator = workKey.LastIndexOf(':');

        if (separator <= Prefix.Length ||
            !int.TryParse(workKey[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out checkNumber))
        {
            return false;
        }

        paymentDetailId = workKey[Prefix.Length..separator];

        return paymentDetailId.Length > 0;
    }

    /// <summary>How long after the previous check (or the charge itself, for check 1) a check runs.</summary>
    public static TimeSpan DelayBefore(int checkNumber) => checkNumber switch
    {
        <= 1 => TimeSpan.FromHours(1),
        2 => TimeSpan.FromHours(4),
        _ => TimeSpan.FromDays(1)
    };
}
