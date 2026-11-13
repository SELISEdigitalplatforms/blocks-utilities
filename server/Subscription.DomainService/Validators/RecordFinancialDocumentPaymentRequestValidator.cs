using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

/// <summary>
/// The limits on a payment recorded by hand. The reference is free text from the caller and lands on
/// the invoice, and a future paid date would be a payment nobody has made.
/// </summary>
public sealed class RecordFinancialDocumentPaymentRequestValidator
    : AbstractValidator<RecordFinancialDocumentPaymentRequest>
{
    public const int MaximumReferenceLength = 200;

    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    public RecordFinancialDocumentPaymentRequestValidator(TimeProvider? time = null)
    {
        var clock = time ?? TimeProvider.System;

        RuleFor(request => request.Reference)
            .MaximumLength(MaximumReferenceLength)
            .WithMessage($"The payment reference is at most {MaximumReferenceLength} characters.");

        RuleFor(request => request.PaidAtUtc)
            .Must(paidAt => AsUtc(paidAt!.Value) <= clock.GetUtcNow().UtcDateTime + AllowedClockSkew)
            .When(request => request.PaidAtUtc.HasValue)
            .WithMessage("The payment date cannot be in the future.");
    }

    /// <summary>An unspecified kind is read as UTC, as the field's name says, not as server-local.</summary>
    public static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
