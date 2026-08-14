using FluentValidation;
using Microsoft.Extensions.Options;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Validators;

public sealed class RecordUsageRequestValidator : AbstractValidator<RecordUsageRequest>
{
    public RecordUsageRequestValidator(IOptionsMonitor<SubscriptionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);

        RuleFor(request => request.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(128)
            .WithMessage(
                "An idempotency key is required. Without one a retried call becomes a second " +
                "billable event, and callers do retry.")
            .WithErrorCode("subscription_usage_idempotency_key_required");

        RuleFor(request => request.Quantity)
            .GreaterThan(0)
            .WithMessage(
                "Usage must be positive. A correction is recorded as its own reversal so the " +
                "ledger can still explain the bill.");

        RuleFor(request => request.Metadata)
            .Must(metadata =>
                metadata.Count <= options.CurrentValue.MaximumUsageMetadataEntries)
            .WithMessage("Too many metadata entries.")
            .WithErrorCode("subscription_usage_metadata_too_large");

        RuleFor(request => request.Metadata)
            .Must(metadata => metadata.Values.All(value =>
                value.Length <= options.CurrentValue.MaximumUsageMetadataValueLength))
            .WithMessage("A metadata value is too long.")
            .WithErrorCode("subscription_usage_metadata_too_large");
    }
}
