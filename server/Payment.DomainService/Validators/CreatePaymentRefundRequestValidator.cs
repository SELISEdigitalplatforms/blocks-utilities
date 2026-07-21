using FluentValidation;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Validators;

public sealed class CreatePaymentRefundRequestValidator :
    AbstractValidator<CreatePaymentRefundRequest>
{
    public CreatePaymentRefundRequestValidator()
    {
        RuleFor(request => request.Amount)
            .GreaterThan(0);

        RuleFor(request => request.Reason)
            .MaximumLength(280);
    }
}
