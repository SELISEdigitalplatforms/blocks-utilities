using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentProviderQueryService :
    IPaymentProviderQueryService
{
    private readonly IPaymentExecutionContextResolver _contextResolver;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentProviderResponseMapper _responseMapper;
    private readonly ILogger<PaymentProviderQueryService> _logger;

    public PaymentProviderQueryService(
        IPaymentExecutionContextResolver contextResolver,
        IPaymentRepository repository,
        IPaymentProviderResponseMapper responseMapper,
        ILogger<PaymentProviderQueryService> logger)
    {
        _contextResolver = contextResolver;
        _repository = repository;
        _responseMapper = responseMapper;
        _logger = logger;
    }

    public async Task<PaymentProviderListResult> GetProvidersAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var contextResolution = _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            var failure = contextResolution.Failure!;

            return PaymentProviderListResult.Failure(
                failure.FailureKind,
                failure.ErrorCode,
                failure.ErrorMessage,
                correlationId);
        }

        var tenantId = contextResolution.Context!.TenantId;
        IReadOnlyList<PaymentProvider> providers;

        try
        {
            providers = await _repository.GetProvidersAsync(
                tenantId,
                cancellationToken);
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
                "Payment provider listing failed TenantHash={TenantHash}",
                PaymentLogValue.Hash(tenantId));

            return PaymentProviderListResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_provider_store_unavailable",
                "Payment providers are temporarily unavailable.",
                correlationId);
        }

        var response = providers
            .OrderBy(provider => provider.ProviderName)
            .ThenBy(provider => provider.MerchantId)
            .ThenBy(provider => provider.ItemId)
            .Select(_responseMapper.Map)
            .ToArray();

        return PaymentProviderListResult.Success(
            response,
            correlationId);
    }
}
