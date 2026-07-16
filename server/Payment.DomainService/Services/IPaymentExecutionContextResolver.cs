using Blocks.Genesis;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentExecutionContextResolver
{
    PaymentContextResolution Resolve(string correlationId);
}
