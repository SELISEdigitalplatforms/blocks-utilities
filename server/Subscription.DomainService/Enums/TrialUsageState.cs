namespace Subscription.DomainService.Enums;

/// <summary>
/// Where one subscriber's claim on a plan's trial stands.
/// </summary>
/// <remarks>
/// <see cref="Released"/> must stay the highest value. The one-trial index filters on
/// "less than Released" because a partial filter cannot say "not Released" — see
/// <see cref="Repositories.TrialUsageIndexDefinitions"/>. A state added after it would silently
/// fall outside the index and let a second trial through.
/// </remarks>
public enum TrialUsageState
{
    /// <summary>Claimed at signup; the subscription has not reached its trial yet.</summary>
    Claimed = 0,

    /// <summary>The subscription reached <see cref="SubscriptionStatus.Trialing"/>. Permanent.</summary>
    Used = 1,

    /// <summary>The signup ended without ever starting its trial, so the trial is available again.</summary>
    Released = 2
}
