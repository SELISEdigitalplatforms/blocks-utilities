using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

public sealed class ProviderSessionCreationResult
{
    public ProviderClientOutcome Outcome { get; init; }
    public HostedCheckoutSessionResponse? Response { get; init; }
    public string? ProviderErrorCode { get; init; }
}
