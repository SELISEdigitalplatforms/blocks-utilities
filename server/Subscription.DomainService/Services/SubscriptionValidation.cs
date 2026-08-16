using FluentValidation;
using FluentValidation.Results;
using Payment.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// Turns a validation run into a result the caller can act on.
/// </summary>
/// <remarks>
/// Written once. The payment module repeats this grouping in four preflight services, and each
/// copy is a chance for one of them to report errors in a shape the client cannot parse.
/// </remarks>
public static class SubscriptionValidation
{
    public static async Task<SubscriptionOperationResult<TValue>?> CheckAsync<TRequest, TValue>(
        IValidator<TRequest> validator,
        TRequest request,
        string errorCode,
        string errorMessage,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var validation = await validator.ValidateAsync(request, cancellationToken);

        return validation.IsValid
            ? null
            : SubscriptionOperationResult<TValue>.Failure(
                PaymentFailureKind.Validation,
                errorCode,
                errorMessage,
                correlationId,
                ToFieldErrors(validation));
    }

    private static Dictionary<string, string[]> ToFieldErrors(ValidationResult validation) =>
        validation.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(failure => failure.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
}
