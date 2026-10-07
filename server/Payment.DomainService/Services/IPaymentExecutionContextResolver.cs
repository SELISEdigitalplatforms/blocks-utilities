using Blocks.Genesis;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentExecutionContextResolver
{
    PaymentContextResolution Resolve(string correlationId);

    /// <summary>
    /// The context for a charge made on an account's behalf — see
    /// <see cref="PaymentExecutionContext.ChargesOnBehalfOfAccount"/>. Needs a tenant, not a user.
    /// </summary>
    PaymentContextResolution ResolveForAccount(
        string payerId,
        string? organizationId,
        string correlationId);
}
