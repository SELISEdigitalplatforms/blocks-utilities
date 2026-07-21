using FluentValidation;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Validators;

public sealed class CreateRecurringPaymentRequestValidator :
    AbstractValidator<CreateRecurringPaymentRequest>
{
    private static readonly string[] SupportedModels =
    [
        "Subscription",
        "UnscheduledCardOnFile"
    ];

    public CreateRecurringPaymentRequestValidator()
    {
        RuleFor(request => request.ProviderName)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(request => request.StoredPaymentMethodId)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(request => request.Amount)
            .GreaterThan(0);

        RuleFor(request => request.CurrencyCode)
            .NotEmpty()
            .Length(3)
            .Matches("^[A-Za-z]{3}$");

        RuleFor(request => request.OrderId)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(request => request.RecurringProcessingModel)
            .Must(model => SupportedModels.Contains(
                model,
                StringComparer.OrdinalIgnoreCase))
            .WithMessage(
                "RecurringProcessingModel must be Subscription or UnscheduledCardOnFile.");

        RuleFor(request => request.Description)
            .MaximumLength(280);
    }
}
