using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

/// <summary>
/// The rules a meter's purchasable-credit terms must meet once they are enabled.
/// </summary>
/// <remarks>
/// Only applied to enabled terms. A disabled block is a paused offer the author may come back to,
/// and refusing to save a plan because a switched-off price is half-written would make pausing
/// sales harder than it needs to be.
/// <para>
/// Quantities are whole credits, whatever the meter's own scale: a subscriber buys 100 credits,
/// never 100.5. Consumption of them may still be fractional.
/// </para>
/// </remarks>
public sealed class MeterTopUpRequestValidator : AbstractValidator<MeterTopUpRequest>
{
    private const string ErrorCode = "subscription_meter_top_up_invalid";

    public MeterTopUpRequestValidator()
    {
        RuleFor(terms => terms.UnitPrices)
            .NotEmpty()
            .WithMessage("Purchasable credits need a price in at least one currency.")
            .WithErrorCode(ErrorCode);

        RuleForEach(terms => terms.UnitPrices)
            .ChildRules(price =>
            {
                price.RuleFor(entry => entry.CurrencyCode)
                    .NotEmpty()
                    .Length(3)
                    .WithMessage("A credit price needs a three-letter currency code.")
                    .WithErrorCode(ErrorCode);
                price.RuleFor(entry => entry.UnitAmountMinor)
                    .GreaterThan(0)
                    .WithMessage(
                        "A credit must cost something. Grant credits instead of selling them " +
                        "for nothing.")
                    .WithErrorCode(ErrorCode);
            });

        // Two prices in one currency would leave the quote depending on which was read first.
        RuleFor(terms => terms.UnitPrices)
            .Must(prices => prices
                .Select(price => price.CurrencyCode.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count() == prices.Count)
            .WithMessage("Each currency may have only one credit price.")
            .WithErrorCode(ErrorCode);

        RuleFor(terms => terms.MinQuantity)
            .GreaterThanOrEqualTo(1)
            .Must(BeWhole)
            .WithMessage("The smallest purchase is a whole number of credits, at least one.")
            .WithErrorCode(ErrorCode);

        // Required, not optional: the maximum is what bounds one charge to a stored card, and a
        // purchase endpoint with no ceiling is one typo away from a very large invoice.
        RuleFor(terms => terms.MaxQuantity)
            .Must(BeWhole)
            .GreaterThanOrEqualTo(terms => terms.MinQuantity)
            .WithMessage(
                "Set the largest purchase allowed, as a whole number of credits no smaller than " +
                "the minimum.")
            .WithErrorCode(ErrorCode);

        RuleFor(terms => terms.ExpiryPolicy)
            .IsInEnum()
            .WithMessage("Credits expire with the usage window, carry forward, or never expire.")
            .WithErrorCode(ErrorCode);

        RuleFor(terms => terms.ExpiresAfter!.Count)
            .GreaterThanOrEqualTo(1)
            .When(terms => terms.ExpiresAfter is not null)
            .WithMessage("A fixed credit lifetime is at least one hour, day, week, month or year.")
            .WithErrorCode(ErrorCode);

        RuleFor(terms => terms.ExpiresAfter!.Unit)
            .IsInEnum()
            .When(terms => terms.ExpiresAfter is not null)
            .WithMessage("A credit lifetime is counted in hours, days, weeks, months or years.")
            .WithErrorCode(ErrorCode);

        RuleFor(terms => terms.TaxRateBasisPoints!.Value)
            .InclusiveBetween(0, 10_000)
            .When(terms => terms.TaxRateBasisPoints.HasValue)
            .WithErrorCode(ErrorCode);

        // The same reasoning a price's tax applies: one number, two possible totals.
        RuleFor(terms => terms.TaxMode)
            .NotNull()
            .When(terms => terms.TaxRateBasisPoints > 0)
            .WithMessage(
                "Say whether the credit tax rate is added to the price (exclusive) or already " +
                "included in it (inclusive).")
            .WithErrorCode(ErrorCode);

        RuleFor(terms => terms.TaxMode!.Value)
            .IsInEnum()
            .When(terms => terms.TaxMode.HasValue)
            .WithErrorCode(ErrorCode);
    }

    private static bool BeWhole(decimal quantity) => decimal.Truncate(quantity) == quantity;
}
