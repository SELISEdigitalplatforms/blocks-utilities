using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

public sealed class StoredPaymentMethodProviderClient : IStoredPaymentMethodProviderClient
{
    private readonly IHttpService _httpService;
    private readonly ICheckoutUrlPolicy _urlPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StoredPaymentMethodProviderClient> _logger;

    public StoredPaymentMethodProviderClient(
        IHttpService httpService,
        ICheckoutUrlPolicy urlPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StoredPaymentMethodProviderClient> logger)
    {
        _httpService = httpService;
        _urlPolicy = urlPolicy;
        _options = options;
        _logger = logger;
    }

    public async Task<ProviderClientOutcome> DeleteAsync(PaymentProvider provider, StoredPaymentMethod method, CancellationToken cancellationToken)
    {
        if (!_urlPolicy.IsAllowedProviderEndpoint(provider.ApiBaseUrl)) return ProviderClientOutcome.Unavailable;
        var baseUri = new Uri(provider.ApiBaseUrl.EndsWith('/') ? provider.ApiBaseUrl : provider.ApiBaseUrl + "/");
        var path = $"storedPaymentMethods/{Uri.EscapeDataString(method.StoredPaymentMethodToken)}";
        var url = new Uri(baseUri, path).AbsoluteUri +
                  $"?merchantAccount={Uri.EscapeDataString(provider.MerchantId)}&shopperReference={Uri.EscapeDataString(method.ShopperReference)}";
        try
        {
            var (_, error) = await _httpService.Delete<object>(url,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["x-api-key"] = provider.ApiKey },
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));
            if (string.IsNullOrWhiteSpace(error) || IsAlreadyDeleted(error)) return ProviderClientOutcome.Success;
            _logger.LogWarning("Stored payment method deletion returned a package error Provider={Provider}", provider.ProviderName);
            return error.Contains("circuit", StringComparison.OrdinalIgnoreCase) || error.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                ? ProviderClientOutcome.Unavailable
                : ProviderClientOutcome.Failure;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return ProviderClientOutcome.Timeout; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning("Stored payment method deletion failed Provider={Provider} ExceptionType={ExceptionType}", provider.ProviderName, ex.GetType().Name);
            return ProviderClientOutcome.Failure;
        }
    }

    private static bool IsAlreadyDeleted(string error) =>
        error.Contains("404", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
}
