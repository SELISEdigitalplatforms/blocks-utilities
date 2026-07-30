using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface ICurrencyMinorUnitResolver
{
    bool TryConvert(decimal amount, string currencyCode, out long minorUnits);

    /// <summary>
    /// Converts a provider's minor units back to an amount. Needed when the provider states an
    /// amount this service did not choose — a capture made in the provider's own dashboard, so
    /// there is no local record to take the amount from.
    /// </summary>
    bool TryConvertBack(long minorUnits, string currencyCode, out decimal amount);
}
