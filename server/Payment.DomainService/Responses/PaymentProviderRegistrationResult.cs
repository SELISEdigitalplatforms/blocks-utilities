namespace Payment.DomainService.Responses;

/// <summary>
/// The result of a registration request, which may have covered several organizations.
/// </summary>
/// <param name="Failure">
/// A failure of the request itself — bad credentials, an unresolvable caller, no organization to
/// act on. Nothing was attempted, so there are no per-organization outcomes to report.
/// </param>
/// <param name="Organizations">
/// One entry per organization the request named, in the order they were attempted. Empty only
/// when <paramref name="Failure"/> is set.
/// </param>
public readonly record struct PaymentProviderRegistrationResult(
    PaymentOperationResult? Failure,
    IReadOnlyList<PaymentProviderRegistrationOutcome> Organizations)
{
    public static PaymentProviderRegistrationResult Rejected(
        PaymentOperationResult failure) => new(failure, []);

    public static PaymentProviderRegistrationResult Attempted(
        IReadOnlyList<PaymentProviderRegistrationOutcome> organizations) =>
        new(null, organizations);

    public bool AllSucceeded =>
        Failure == null &&
        Organizations.Count > 0 &&
        Organizations.All(outcome => outcome.IsSuccess);
}
