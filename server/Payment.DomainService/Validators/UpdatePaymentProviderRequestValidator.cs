using FluentValidation;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;

namespace Payment.DomainService.Validators;

public sealed class UpdatePaymentProviderRequestValidator :
    AbstractValidator<UpdatePaymentProviderRequest>
{
    public UpdatePaymentProviderRequestValidator(
        ICheckoutUrlPolicy checkoutUrlPolicy)
    {
        ArgumentNullException.ThrowIfNull(checkoutUrlPolicy);

        RuleFor(request => request.Version)
            .NotNull()
            .GreaterThanOrEqualTo(0);

        RuleFor(request => request.FrontendResultUrl)
            .NotEmpty()
            .Must(checkoutUrlPolicy.IsAllowedFrontendResultUrl)
            .WithMessage(
                "FrontendResultUrl must be a public HTTPS address.")
            .WithErrorCode("payment_frontend_url_invalid");

        RuleFor(request => request.CountryCode)
            .Length(2)
            .When(request =>
                !string.IsNullOrWhiteSpace(request.CountryCode));

        RuleFor(request => request.MaxRefundDays)
            .InclusiveBetween(0, 3_650);

        RuleFor(request => request.StoreId)
            .MaximumLength(200);

        RuleFor(request => request)
            .Must(NotContainIdentityFields)
            .WithMessage(
                "ProviderName and MerchantId are immutable. Register a new provider to change its identity.")
            .WithErrorCode("payment_provider_identity_immutable");

        RuleFor(request => request.UnmappedFields)
            .Must(fields => fields == null || fields.Count == 0)
            .WithMessage(
                "The request contains fields that cannot be updated.")
            .WithErrorCode("payment_provider_update_field_invalid");
    }

    private static bool NotContainIdentityFields(
        UpdatePaymentProviderRequest request) =>
        request.UnmappedFields?.Keys.All(key =>
            !string.Equals(
                key,
                "providerName",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                key,
                "merchantId",
                StringComparison.OrdinalIgnoreCase)) != false;
}
