using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface ICurrencyMinorUnitResolver
{
    bool TryConvert(decimal amount, string currencyCode, out long minorUnits);
}
