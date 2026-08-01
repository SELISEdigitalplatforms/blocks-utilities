using FluentValidation;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Validators;

public sealed class GetPaymentsRequestValidator :
    AbstractValidator<GetPaymentsRequest>
{
    private const int MaximumFilterValues = 20;
    private const int MaximumCursorLength = 4_096;

    public GetPaymentsRequestValidator()
    {
        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(request => request.ProviderNames)
            .NotNull()
            .Must(values => values.Length <= MaximumFilterValues)
            .WithMessage(
                $"A maximum of {MaximumFilterValues} provider names is allowed.");

        RuleForEach(request => request.ProviderNames)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(request => request.PaymentStatuses)
            .NotNull()
            .Must(values => values.Length <= MaximumFilterValues)
            .WithMessage(
                $"A maximum of {MaximumFilterValues} payment statuses is allowed.");

        RuleForEach(request => request.PaymentStatuses)
            .NotEmpty()
            .Must(IsKnownPaymentStatus)
            .WithMessage("An unsupported payment status was supplied.");

        RuleFor(request => request.MinAmount)
            .InclusiveBetween(0m, 999_999_999m)
            .When(request => request.MinAmount.HasValue);

        RuleFor(request => request.MaxAmount)
            .InclusiveBetween(0m, 999_999_999m)
            .When(request => request.MaxAmount.HasValue);

        RuleFor(request => request)
            .Must(request =>
                !request.MinAmount.HasValue ||
                !request.MaxAmount.HasValue ||
                request.MinAmount <= request.MaxAmount)
            .WithName(nameof(GetPaymentsRequest.MaxAmount))
            .WithMessage(
                "MaxAmount must be greater than or equal to MinAmount.");

        RuleFor(request => request)
            .Must(request =>
                !request.PaymentDateFromUtc.HasValue ||
                !request.PaymentDateToUtc.HasValue ||
                request.PaymentDateFromUtc < request.PaymentDateToUtc)
            .WithName(nameof(GetPaymentsRequest.PaymentDateToUtc))
            .WithMessage(
                "PaymentDateToUtc must be later than PaymentDateFromUtc.");

        RuleFor(request => request.CurrencyCode)
            .Length(3)
            .Matches("^[A-Za-z]{3}$")
            .When(request =>
                !string.IsNullOrWhiteSpace(request.CurrencyCode));

        RuleFor(request => request.OrderId)
            .MaximumLength(80);

        RuleFor(request => request.PaymentDetailId)
            .MaximumLength(128);

        RuleFor(request => request.PaymentFlow)
            .Must(IsKnownPaymentFlow)
            .WithMessage("An unsupported payment flow was supplied.")
            .When(request =>
                !string.IsNullOrWhiteSpace(request.PaymentFlow));

        RuleFor(request => request.SortBy)
            .NotEmpty()
            .Must(IsKnownSortField)
            .WithMessage("An unsupported payment sort field was supplied.");

        RuleFor(request => request.SortDirection)
            .NotEmpty()
            .Must(IsKnownSortDirection)
            .WithMessage("An unsupported payment sort direction was supplied.");

        RuleFor(request => request.After)
            .MaximumLength(MaximumCursorLength);

        RuleFor(request => request.Before)
            .MaximumLength(MaximumCursorLength);

        RuleFor(request => request)
            .Must(request =>
                string.IsNullOrWhiteSpace(request.After) ||
                string.IsNullOrWhiteSpace(request.Before))
            .WithName(nameof(GetPaymentsRequest.After))
            .WithMessage("After and Before cannot be used together.");
    }

    private static bool IsKnownPaymentStatus(string value) =>
        PaymentStatuses.All.Contains(
            value,
            StringComparer.OrdinalIgnoreCase);

    private static bool IsKnownPaymentFlow(string? value) =>
        value != null &&
        PaymentFlows.All.Contains(
            value,
            StringComparer.OrdinalIgnoreCase);

    private static bool IsKnownSortField(string value) =>
        PaymentQuerySortFields.All.Contains(
            value,
            StringComparer.OrdinalIgnoreCase);

    private static bool IsKnownSortDirection(string value) =>
        PaymentQuerySortDirections.All.Contains(
            value,
            StringComparer.OrdinalIgnoreCase);
}
