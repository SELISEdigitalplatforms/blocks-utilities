namespace Subscription.DomainService.Services;

/// <summary>
/// The palette a document renders under when nothing on the merchant profile named one.
/// </summary>
/// <remarks>
/// One shared pair, not per-tenant configuration. A tenant that has never opened the merchant
/// profile page still issues documents from the moment it goes live, and those documents need
/// colors before anybody has chosen any — this is what makes an unbranded invoice look designed
/// rather than broken, and it is deliberately the only palette this module invents on a tenant's
/// behalf.
/// </remarks>
public static class FinancialDocumentBrandingDefaults
{
    public const string PrimaryColor = "#17365D";

    public const string AccentColor = "#D9E7F5";
}
