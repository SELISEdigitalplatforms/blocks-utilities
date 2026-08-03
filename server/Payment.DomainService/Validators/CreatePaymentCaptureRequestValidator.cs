using FluentValidation;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Validators;

public sealed class CreatePaymentCaptureRequestValidator :
    AbstractValidator<CreatePaymentCaptureRequest>
{
    public CreatePaymentCaptureRequestValidator()
    {
        RuleFor(request => request.Amount)
            .GreaterThan(0)
            .WithMessage("Capture amount must be greater than zero.");
    }
}
