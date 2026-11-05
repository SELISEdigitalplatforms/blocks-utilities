using FluentValidation;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

/// <summary>The shape of a catalogue meter. Scope conflicts are the repository's unique index's job.</summary>
public sealed class CreateMeterCatalogueEntryRequestValidator : AbstractValidator<CreateMeterCatalogueEntryRequest>
{
    public CreateMeterCatalogueEntryRequestValidator()
    {
        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.UnitLabel).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Aggregation).IsInEnum();
        RuleFor(request => request.QuantityScale).InclusiveBetween(0, 6);
    }
}

public sealed class UpdateMeterCatalogueEntryRequestValidator : AbstractValidator<UpdateMeterCatalogueEntryRequest>
{
    public UpdateMeterCatalogueEntryRequestValidator()
    {
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.UnitLabel).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Aggregation).IsInEnum();
        RuleFor(request => request.QuantityScale).InclusiveBetween(0, 6);
        // Draft is a plan's state, not a meter's: a meter either counts or is retired.
        RuleFor(request => request.Status)
            .Must(status => status is CatalogueStatus.Active or CatalogueStatus.Archived)
            .WithMessage("A catalogue meter is Active or Archived.");
    }
}

/// <summary>
/// The terms of a standalone credit offer: the same rules a plan's top-up terms meet, less the
/// expiry policy (an offer has no usage window) plus the spend order.
/// </summary>
public sealed class CreditOfferRequestValidator : AbstractValidator<CreditOfferRequest>
{
    public CreditOfferRequestValidator()
    {
        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);

        RuleFor(request => request.UnitPrices)
            .NotEmpty()
            .WithMessage("An offer needs a price in at least one currency.");

        RuleForEach(request => request.UnitPrices)
            .ChildRules(price =>
            {
                price.RuleFor(entry => entry.CurrencyCode).NotEmpty().Length(3);
                price.RuleFor(entry => entry.UnitAmountMinor)
                    .GreaterThan(0)
                    .WithMessage("A credit must cost something. Grant credits instead of selling them for nothing.");
            });

        RuleFor(request => request.UnitPrices)
            .Must(prices => prices
                .Select(price => price.CurrencyCode.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count() == prices.Count)
            .WithMessage("Each currency may have only one credit price.");

        RuleFor(request => request.MinQuantity)
            .GreaterThanOrEqualTo(1)
            .Must(BeWhole)
            .WithMessage("The smallest purchase is a whole number of credits, at least one.");

        RuleFor(request => request.MaxQuantity)
            .Must(BeWhole)
            .GreaterThanOrEqualTo(request => request.MinQuantity)
            .WithMessage("Set the largest purchase allowed, as a whole number of credits no smaller than the minimum.");

        RuleFor(request => request.ExpiresAfter!.Count)
            .GreaterThanOrEqualTo(1)
            .When(request => request.ExpiresAfter is not null)
            .WithMessage("A fixed credit lifetime is at least one hour, day, week, month or year.");

        RuleFor(request => request.ExpiresAfter!.Unit)
            .IsInEnum()
            .When(request => request.ExpiresAfter is not null);

        RuleFor(request => request.TaxRateBasisPoints!.Value)
            .InclusiveBetween(0, 10_000)
            .When(request => request.TaxRateBasisPoints.HasValue);

        RuleFor(request => request.TaxMode)
            .NotNull()
            .When(request => request.TaxRateBasisPoints > 0)
            .WithMessage("Say whether the tax rate is added to the price (exclusive) or already included in it (inclusive).");

        RuleFor(request => request.TaxMode!.Value)
            .IsInEnum()
            .When(request => request.TaxMode.HasValue);

        RuleFor(request => request.SpendOrder).IsInEnum();
    }

    private static bool BeWhole(decimal quantity) => decimal.Truncate(quantity) == quantity;
}
