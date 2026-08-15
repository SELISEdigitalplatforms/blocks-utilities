namespace Subscription.DomainService.Enums;

/// <summary>
/// What a usage ledger entry represents. The ledger is append-only, so a mistake is corrected
/// by a <see cref="Reversal"/> rather than by editing what was written.
/// </summary>
public enum UsageEntryType
{
    /// <summary>Something was used. Raises the balance.</summary>
    Consumption = 0,

    /// <summary>Something used was undone or never happened. Lowers the balance.</summary>
    Reversal = 1,

    /// <summary>
    /// Allowance added to the period. Unused in phase 1, and the reason the ledger is a ledger:
    /// prepaid credits are the same drawdown with money attached, and retrofitting them onto a
    /// bare counter would mean rebuilding history nobody kept.
    /// </summary>
    Grant = 2
}
