namespace Payment.DomainService.Enums;

/// <summary>
/// What took the payment: the console, or an application calling the API.
/// </summary>
/// <remarks>
/// Recorded because the two are not the same kind of money. The console exists to simulate,
/// and its payments are real charges against a real merchant account that nobody's customer
/// asked for. Reporting that cannot tell them apart counts test traffic as revenue, and an
/// investigation into an unexpected charge has no way to see it came from an operator's
/// browser.
/// </remarks>
public static class PaymentOrigins
{
    /// <summary>Taken through the platform console, which is a simulation.</summary>
    public const string BlocksConsole = "BLOCKS_CONSOLE";

    /// <summary>Taken by an application through the payment API.</summary>
    public const string Api = "API";

    public static readonly string[] All =
    [
        BlocksConsole,
        Api
    ];
}
