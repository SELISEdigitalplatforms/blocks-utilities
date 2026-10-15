using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

/// <summary>
/// The shape of a credit quote or purchase. Whether the quantity is within the meter's bounds is
/// checked against the subscription's terms, which this validator cannot see.
/// </summary>
public sealed class CreditPurchaseRequestValidator : AbstractValidator<CreditPurchaseRequest>
{
    public CreditPurchaseRequestValidator()
    {
        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Quantity)
            .GreaterThan(0)
            .Must(CreditQuantities.BeWhole)
            .WithMessage("Credits are bought in whole numbers.");
        RuleFor(request => request.SeatNumber!.Value)
            .GreaterThan(0)
            .When(request => request.SeatNumber.HasValue);
        RuleFor(request => request.DiscountCode).MaximumLength(64);
        RuleFor(request => request.IdempotencyKey)
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9_-]+$")
            .When(request => request.IdempotencyKey is not null);
    }
}

public sealed class GrantCreditsRequestValidator : AbstractValidator<GrantCreditsRequest>
{
    public GrantCreditsRequestValidator()
    {
        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Quantity)
            .GreaterThan(0)
            .Must(CreditQuantities.BeWhole)
            .WithMessage("Credits are granted in whole numbers.");
        RuleFor(request => request.SeatNumber!.Value)
            .GreaterThan(0)
            .When(request => request.SeatNumber.HasValue);
        RuleFor(request => request.Reason)
            .NotEmpty()
            .MaximumLength(500)
            .WithMessage("Say why these credits are being given. It is kept with the grant.");
    }
}

public sealed class MoveCreditsRequestValidator : AbstractValidator<MoveCreditsRequest>
{
    public MoveCreditsRequestValidator()
    {
        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);
        // Not necessarily whole: a grant can be left holding a fraction once a meter that counts
        // in fractions has drawn on it, and that fraction is as movable as the rest.
        RuleFor(request => request.Quantity).GreaterThan(0);
        RuleFor(request => request.FromSeatNumber!.Value)
            .GreaterThan(0)
            .When(request => request.FromSeatNumber.HasValue);
        RuleFor(request => request.ToSeatNumber!.Value)
            .GreaterThan(0)
            .When(request => request.ToSeatNumber.HasValue);
        RuleFor(request => request)
            .Must(request => request.FromSeatNumber != request.ToSeatNumber)
            .WithName(nameof(MoveCreditsRequest.ToSeatNumber))
            .WithMessage("Credits can only be moved to a different seat, or between a seat and the pool.");
    }
}

internal static class CreditQuantities
{
    public static bool BeWhole(decimal quantity) => decimal.Truncate(quantity) == quantity;
}
