using System.Text.Json;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface IPaymentWebhookIntakeService
{
    Task<WebhookIntakeOutcome> AcceptStandardAsync(string tenantId, StandardWebhookRequest request, CancellationToken cancellationToken);
    Task<WebhookIntakeOutcome> AcceptTokenAsync(string tenantId, string rawBody, string signature, CancellationToken cancellationToken);
}
