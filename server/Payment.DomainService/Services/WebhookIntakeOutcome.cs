using System.Text.Json;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public enum WebhookIntakeOutcome { Accepted, Unauthorized, Malformed, NotFound, StorageUnavailable }
