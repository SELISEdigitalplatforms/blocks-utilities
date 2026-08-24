using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>
/// Whether a price's configured amount is before tax or already contains it.
/// </summary>
/// <remarks>
/// The distinction is what a merchant means by "145". In most of Europe a consumer price of CHF 145
/// is what the customer pays, tax included; a business price of CHF 145 plus 7.7% VAT is CHF 156.17.
/// Both are ordinary, and neither can be inferred from the number.
/// <para>
/// <see cref="Exclusive"/> is zero deliberately. Prices authored before this existed carry a tax
/// rate and no mode, and they were all calculated by adding tax to the configured amount — so the
/// absent value has to read back as the behaviour those subscriptions were sold on.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaxMode
{
    /// <summary>Tax is added to the configured amount. The legacy behaviour, and the default.</summary>
    Exclusive = 0,

    /// <summary>The configured amount already contains the tax, which is extracted from it.</summary>
    Inclusive = 1
}
