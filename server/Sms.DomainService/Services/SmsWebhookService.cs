using System.Text.RegularExpressions;
using Blocks.Secrets;
using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Enums;
using Sms.DomainService.Providers;
using Sms.DomainService.Repositories;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Services;

public enum SmsWebhookOutcome
{
    Accepted = 1,
    Unauthorized = 2,
    Malformed = 3,
    NotFound = 4
}

public interface ISmsWebhookService
{
    Task<SmsWebhookOutcome> HandleAsync(string provider, string tenantId, SmsWebhookRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Delivery callbacks. The tenant comes from the route, which is only a claim until the provider's
/// signature has been checked with that tenant's own key: nothing is read from the body before then.
/// </summary>
public sealed partial class SmsWebhookService : ISmsWebhookService
{
    private readonly ISmsRepository _repository;
    private readonly ISmsProviderFactory _providerFactory;
    private readonly ISmsProviderContextResolver _contextResolver;
    private readonly ISmsProcessingService _processing;
    private readonly ILogger<SmsWebhookService> _logger;

    public SmsWebhookService(
        ISmsRepository repository,
        ISmsProviderFactory providerFactory,
        ISmsProviderContextResolver contextResolver,
        ISmsProcessingService processing,
        ILogger<SmsWebhookService> logger)
    {
        _repository = repository;
        _providerFactory = providerFactory;
        _contextResolver = contextResolver;
        _processing = processing;
        _logger = logger;
    }

    public async Task<SmsWebhookOutcome> HandleAsync(string provider, string tenantId, SmsWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<SmsProviderType>(provider, true, out var providerType) ||
            !Enum.IsDefined(providerType) ||
            !TenantIdPattern().IsMatch(tenantId))
        {
            return SmsWebhookOutcome.NotFound;
        }

        using var tenant = SmsTenantContext.Enter(tenantId);

        var configuration = await _repository.GetActiveProviderConfigurationAsync(tenantId, providerType, cancellationToken);
        if (configuration == null)
        {
            return SmsWebhookOutcome.NotFound;
        }

        SmsProviderContext context;
        try
        {
            context = await _contextResolver.ResolveAsync(tenantId, configuration, cancellationToken);
        }
        catch (SecretAccessDeniedException)
        {
            // Unknown or disabled tenant: the same answer as a tenant with no configuration.
            return SmsWebhookOutcome.NotFound;
        }

        var parsed = _providerFactory.GetProvider(configuration).VerifyAndParseCallback(context, request);
        if (parsed.Verdict != SmsWebhookVerdict.Verified)
        {
            _logger.LogWarning("SmsWebhookService: callback rejected Provider={Provider}, TenantId={TenantId}, Verdict={Verdict}", providerType, SmsLogSanitizer.Id(tenantId), parsed.Verdict);
            return parsed.Verdict == SmsWebhookVerdict.Unauthorized ? SmsWebhookOutcome.Unauthorized : SmsWebhookOutcome.Malformed;
        }

        return await _processing.ApplyCallbackAsync(tenantId, parsed.Callback!, cancellationToken)
            ? SmsWebhookOutcome.Accepted
            : SmsWebhookOutcome.NotFound;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex TenantIdPattern();
}
