using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class CurrencyMinorUnitResolver : ICurrencyMinorUnitResolver
{
    private readonly IReadOnlyDictionary<string, int> _minorUnits;

    public CurrencyMinorUnitResolver(IOptionsMonitor<PaymentOptions> options) =>
        _minorUnits = new Dictionary<string, int>(
            options.CurrentValue.CurrencyMinorUnits,
            StringComparer.OrdinalIgnoreCase);

    public bool TryConvert(decimal amount, string currencyCode, out long minorUnits)
    {
        minorUnits = 0;
        if (!_minorUnits.TryGetValue(currencyCode, out var precision) || precision is < 0 or > 3)
        {
            return false;
        }

        var scale = (decimal)Math.Pow(10, precision);
        var scaled = amount * scale;
        if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue)
        {
            return false;
        }

        minorUnits = decimal.ToInt64(scaled);
        return minorUnits > 0;
    }

    public bool TryConvertBack(long minorUnits, string currencyCode, out decimal amount)
    {
        amount = 0;

        if (minorUnits <= 0 ||
            !_minorUnits.TryGetValue(currencyCode, out var precision) ||
            precision is < 0 or > 3)
        {
            return false;
        }

        amount = minorUnits / (decimal)Math.Pow(10, precision);
        return true;
    }
}
