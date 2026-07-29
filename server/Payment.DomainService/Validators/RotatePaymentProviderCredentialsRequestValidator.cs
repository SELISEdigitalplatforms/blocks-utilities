using FluentValidation;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Validators;

public sealed class RotatePaymentProviderCredentialsRequestValidator :
    AbstractValidator<RotatePaymentProviderCredentialsRequest>
{
    public RotatePaymentProviderCredentialsRequestValidator()
    {
        RuleFor(request => request.Version)
            .NotNull()
            .GreaterThanOrEqualTo(0);

        RuleFor(request => request.ApiKey)
            .NotEmpty()
            .MaximumLength(8_192)
            .When(request => request.ApiKey != null);

        RuleFor(request => request.WebhookHmacKey)
            .NotEmpty()
            .MaximumLength(8_192)
            .When(request => request.WebhookHmacKey != null);

        RuleFor(request => request.TokenHmacKey)
            .NotEmpty()
            .MaximumLength(8_192)
            .When(request => request.TokenHmacKey != null);

        RuleFor(request => request)
            .Must(ContainsCredential)
            .WithMessage(
                "At least one credential must be supplied for rotation.")
            .WithErrorCode("payment_provider_rotation_empty");

        RuleFor(request => request)
            .Must(NotContainShopperReferenceKey)
            .WithMessage(
                "ShopperReferenceHmacKey is an identity key and cannot be rotated through this endpoint.")
            .WithErrorCode(
                "payment_provider_shopper_identity_key_immutable");

        RuleFor(request => request.UnmappedFields)
            .Must(fields => fields == null || fields.Count == 0)
            .WithMessage(
                "The request contains fields that cannot be rotated.")
            .WithErrorCode("payment_provider_rotation_field_invalid");
    }

    private static bool ContainsCredential(
        RotatePaymentProviderCredentialsRequest request) =>
        request.ApiKey != null ||
        request.WebhookHmacKey != null ||
        request.TokenHmacKey != null;

    private static bool NotContainShopperReferenceKey(
        RotatePaymentProviderCredentialsRequest request) =>
        request.UnmappedFields?.Keys.All(key =>
            !string.Equals(
                key,
                "shopperReferenceHmacKey",
                StringComparison.OrdinalIgnoreCase)) != false;
}
