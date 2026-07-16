using Blocks.Genesis;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record PaymentContextResolution(PaymentExecutionContext? Context, PaymentOperationResult? Failure)
{
    public bool IsSuccess => Context != null;
}
