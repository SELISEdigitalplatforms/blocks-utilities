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
            .NotEqual(0)
            .WithMessage(
                "Usage cannot be zero. Negative adjustments are accepted only by never-reset " +
                "capacity meters, where they release previously consumed capacity.");

        // Granularity is the meter's own business and is checked by UsageRecordingService, which
        // is the only place the subscription's snapshotted scale is known. Magnitude needs no
        // meter, so it is refused here before a subscription is even read.
        RuleFor(request => request.Quantity)
            .Must(MeterQuantity.IsWithinMagnitude)
            .WithMessage("The quantity is larger than a quantity may be.")
            .WithErrorCode("subscription_usage_quantity_scale_invalid");

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
