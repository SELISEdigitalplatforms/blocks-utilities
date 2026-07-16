using Blocks.Genesis;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record PaymentExecutionContext(string TenantId, string ActorId, string? OrganizationId);
