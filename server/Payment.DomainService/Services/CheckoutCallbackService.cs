using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class CheckoutCallbackService : ICheckoutCallbackService
{
    private readonly ICheckoutCallbackRequestValidator _requestValidator;
    private readonly ICheckoutCallbackRateLimiter _rateLimiter;
    private readonly ICheckoutCallbackContextResolver _contextResolver;
    private readonly ICheckoutObservationService _observationService;
    private readonly IPaymentRedirectBuilder _redirectBuilder;

    public CheckoutCallbackService(
        ICheckoutCallbackRequestValidator requestValidator,
        ICheckoutCallbackRateLimiter rateLimiter,
        ICheckoutCallbackContextResolver contextResolver,
        ICheckoutObservationService observationService,
        IPaymentRedirectBuilder redirectBuilder)
    {
        _requestValidator = requestValidator;
        _rateLimiter = rateLimiter;
        _contextResolver = contextResolver;
        _observationService = observationService;
        _redirectBuilder = redirectBuilder;
    }

    public async Task<CheckoutCallbackResult> ProcessAsync(
        CheckoutCallbackRequest request,
        string clientAddress,
        CancellationToken cancellationToken)
    {
        if (!_requestValidator.IsValid(request))
        {
            return CheckoutCallbackResult.Failure(
                PaymentFailureKind.Validation,
                "invalid_callback_request",
                "The checkout callback request is invalid.");
        }

        var rateLimit = await _rateLimiter.CheckAsync(
            clientAddress,
            request.State!,
            cancellationToken);

        var rateLimitFailure = MapRateLimitFailure(rateLimit);

        if (rateLimitFailure != null)
        {
            return rateLimitFailure;
        }

        var resolution = await _contextResolver.ResolveAsync(
            request.State!,
            request.SessionId!,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.Failure!;
        }

        var context = resolution.Context!;
        var finalStatus = GetFinalRedirectStatus(context.Payment.PaymentStatus);

        if (finalStatus != null)
        {
            return _redirectBuilder.Build(context.Payment, finalStatus);
        }

        var observation = await _observationService.ObserveAsync(
            context,
            request.SessionResult!,
            cancellationToken);

        return observation.Failure ??
               _redirectBuilder.Build(context.Payment, observation.RedirectStatus!);
    }

    private static CheckoutCallbackResult? MapRateLimitFailure(
        PaymentRateLimitResult rateLimit)
    {
        if (!rateLimit.IsAvailable)
        {
            return CheckoutCallbackResult.Failure(
                PaymentFailureKind.Unavailable,
                "callback_rate_limiter_unavailable",
                "The checkout callback service is temporarily unavailable.",
                rateLimit.RetryAfterSeconds);
        }

        return !rateLimit.IsAllowed
            ? CheckoutCallbackResult.Failure(
                PaymentFailureKind.RateLimited,
                "callback_rate_limit_exceeded",
                "Too many checkout callback requests.",
                rateLimit.RetryAfterSeconds)
            : null;
    }

    private static string? GetFinalRedirectStatus(string paymentStatus) => paymentStatus switch
    {
        PaymentStatuses.Authorized => PaymentRedirectStatuses.Success,
        PaymentStatuses.Refused => PaymentRedirectStatuses.Fail,
        _ => null
    };
}
