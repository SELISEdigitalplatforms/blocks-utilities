using System.Diagnostics;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentQueryService : IPaymentQueryService
{
    private readonly IValidator<GetPaymentsRequest> _validator;
    private readonly IPaymentExecutionContextResolver _contextResolver;
    private readonly IPaymentQueryRateLimiter _rateLimiter;
    private readonly IPaymentQueryCursorCodec _cursorCodec;
    private readonly IPaymentQueryRepository _repository;
    private readonly IPaymentQueryResponseMapper _responseMapper;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentQueryService> _logger;

    public PaymentQueryService(
        IValidator<GetPaymentsRequest> validator,
        IPaymentExecutionContextResolver contextResolver,
        IPaymentQueryRateLimiter rateLimiter,
        IPaymentQueryCursorCodec cursorCodec,
        IPaymentQueryRepository repository,
        IPaymentQueryResponseMapper responseMapper,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentQueryService> logger)
    {
        _options = options;
        _validator = validator;
        _contextResolver = contextResolver;
        _rateLimiter = rateLimiter;
        _cursorCodec = cursorCodec;
        _repository = repository;
        _responseMapper = responseMapper;
        _logger = logger;
    }

    public async Task<PaymentQueryOperationResult> GetPaymentsAsync(
        GetPaymentsRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validator.ValidateAsync(
            request,
            cancellationToken);

        if (!validation.IsValid)
        {
            return PaymentQueryOperationResult.Failure(
                PaymentFailureKind.Validation,
                "invalid_payment_query",
                "The payment query is invalid.",
                correlationId,
                validation.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .Select(error => error.ErrorMessage)
                            .Distinct()
                            .ToArray()));
        }

        var contextResolution = _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            var failure = contextResolution.Failure!;

            return PaymentQueryOperationResult.Failure(
                failure.FailureKind,
                failure.ErrorCode,
                failure.ErrorMessage,
                correlationId,
                failure.ValidationErrors);
        }

        var context = contextResolution.Context!;
        var rateLimit = await _rateLimiter.CheckAsync(
            context.TenantId,
            context.ActorId,
            cancellationToken);

        if (!rateLimit.IsAvailable)
        {
            return PaymentQueryOperationResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_query_rate_limiter_unavailable",
                "Payment queries are temporarily unavailable.",
                correlationId,
                rateLimit: rateLimit);
        }

        if (!rateLimit.IsAllowed)
        {
            return PaymentQueryOperationResult.Failure(
                PaymentFailureKind.RateLimited,
                "payment_query_rate_limit_exceeded",
                "Too many payment queries were requested.",
                correlationId,
                rateLimit: rateLimit);
        }

        var criteria = CreateCriteria(
            context.TenantId,
            context.OrganizationId,
            PaymentOrganizationScope.RequestMayNameOrganization(
                context.OrganizationId,
                _options.CurrentValue),
            request);
        var cursor = FirstNonEmpty(request.Before, request.After);

        if (cursor != null)
        {
            if (!_cursorCodec.TryDecode(
                    cursor,
                    criteria,
                    out var boundary))
            {
                return PaymentQueryOperationResult.Failure(
                    PaymentFailureKind.Validation,
                    "invalid_payment_cursor",
                    "The payment cursor is invalid for this query.",
                    correlationId,
                    new Dictionary<string, string[]>
                    {
                        [request.Before != null
                            ? nameof(request.Before)
                            : nameof(request.After)] =
                        ["The cursor is malformed or does not match the query."]
                    },
                    rateLimit);
            }

            criteria = criteria with
            {
                CursorBoundary = boundary
            };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var page = await _repository.QueryAsync(
                criteria,
                cancellationToken);
            var response = _responseMapper.Map(criteria, page);

            _logger.LogInformation(
                "Payment query completed TenantHash={TenantHash} ActorHash={ActorHash} SortBy={SortBy} SortDirection={SortDirection} PageSize={PageSize} FilterCount={FilterCount} ResultCount={ResultCount} DurationMs={DurationMs}",
                PaymentLogValue.Hash(context.TenantId),
                PaymentLogValue.Hash(context.ActorId),
                PaymentLogValue.Label(criteria.SortBy),
                PaymentLogValue.Label(criteria.SortDirection),
                criteria.PageSize,
                CountFilters(criteria),
                page.Items.Count,
                stopwatch.Elapsed.TotalMilliseconds);

            return PaymentQueryOperationResult.Success(
                response,
                correlationId,
                rateLimit);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Payment query failed TenantHash={TenantHash} ActorHash={ActorHash} SortBy={SortBy} PageSize={PageSize} FilterCount={FilterCount} DurationMs={DurationMs}",
                PaymentLogValue.Hash(context.TenantId),
                PaymentLogValue.Hash(context.ActorId),
                PaymentLogValue.Label(criteria.SortBy),
                criteria.PageSize,
                CountFilters(criteria),
                stopwatch.Elapsed.TotalMilliseconds);

            return PaymentQueryOperationResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_query_unavailable",
                "Payment queries are temporarily unavailable.",
                correlationId,
                rateLimit: rateLimit);
        }
    }

    private static PaymentQueryCriteria CreateCriteria(
        string tenantId,
        string? organizationId,
        bool requestMayNameOrganization,
        GetPaymentsRequest request) =>
        new()
        {
            TenantId = tenantId,
            // From the caller's context, never the request: a request-supplied organization
            // would let anyone list any organization's payments by naming it.
            OrganizationId = organizationId,
            // Replaces the scope above, but only for the console, which is the one caller whose
            // own organization is fixed and meaningless. Everyone else is read back under the
            // organization their token carries, so a filter cannot widen what they can see. The
            // tenant comes from the token in either case.
            RequestedOrganizationId = requestMayNameOrganization
                ? NormalizeOptional(request.OrganizationId)
                : null,
            PageSize = request.PageSize,
            ProviderNames = NormalizeValues(request.ProviderNames),
            PaymentStatuses = NormalizeValues(request.PaymentStatuses),
            MinAmount = request.MinAmount,
            MaxAmount = request.MaxAmount,
            PaymentDateFromUtc =
                request.PaymentDateFromUtc?.UtcDateTime,
            PaymentDateToUtc = request.PaymentDateToUtc?.UtcDateTime,
            CurrencyCode = NormalizeOptionalUpper(request.CurrencyCode),
            OrderId = NormalizeOptional(request.OrderId),
            PaymentDetailId = NormalizeOptional(request.PaymentDetailId),
            PaymentFlow = NormalizeOptionalUpper(request.PaymentFlow),
            SortBy = PaymentQuerySortFields.All.Single(value =>
                string.Equals(
                    value,
                    request.SortBy,
                    StringComparison.OrdinalIgnoreCase)),
            SortDirection = PaymentQuerySortDirections.All.Single(value =>
                string.Equals(
                    value,
                    request.SortDirection,
                    StringComparison.OrdinalIgnoreCase)),
            IsBackward = !string.IsNullOrWhiteSpace(request.Before)
        };

    private static string[] NormalizeValues(IEnumerable<string> values) =>
        values
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? NormalizeOptionalUpper(string? value) =>
        NormalizeOptional(value)?.ToUpperInvariant();

    private static string? FirstNonEmpty(
        string? first,
        string? second) =>
        !string.IsNullOrWhiteSpace(first)
            ? first
            : !string.IsNullOrWhiteSpace(second)
                ? second
                : null;

    private static int CountFilters(PaymentQueryCriteria criteria) =>
        (criteria.ProviderNames.Length > 0 ? 1 : 0) +
        (criteria.PaymentStatuses.Length > 0 ? 1 : 0) +
        (criteria.MinAmount.HasValue ? 1 : 0) +
        (criteria.MaxAmount.HasValue ? 1 : 0) +
        (criteria.PaymentDateFromUtc.HasValue ? 1 : 0) +
        (criteria.PaymentDateToUtc.HasValue ? 1 : 0) +
        (criteria.CurrencyCode != null ? 1 : 0) +
        (criteria.OrderId != null ? 1 : 0) +
        (criteria.PaymentDetailId != null ? 1 : 0) +
        (criteria.PaymentFlow != null ? 1 : 0);
}
