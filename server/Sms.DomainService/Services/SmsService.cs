using Blocks.Genesis;
using Blocks.Secrets;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Repositories;
using Sms.DomainService.Requests;
using Sms.DomainService.Responses;
using Sms.DomainService.Scheduling;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Services;

public class SmsService : ISmsService
{
    private readonly IValidator<SendSmsRequest> _sendValidator;
    private readonly IValidator<SendSmsByTemplateRequest> _templateValidator;
    private readonly IValidator<SaveSmsProviderConfigurationRequest> _configurationValidator;
    private readonly ISmsRepository _repository;
    private readonly ISmsWorkQueue _workQueue;
    private readonly IMessageClient _messageClient;
    private readonly ISecretService _secrets;
    private readonly ISuspiciousMessageService _suspiciousMessageService;
    private readonly ISmsRateLimiter _rateLimiter;
    private readonly ILogger<SmsService> _logger;

    public SmsService(
        IValidator<SendSmsRequest> sendValidator,
        IValidator<SendSmsByTemplateRequest> templateValidator,
        IValidator<SaveSmsProviderConfigurationRequest> configurationValidator,
        ISmsRepository repository,
        ISmsWorkQueue workQueue,
        IMessageClient messageClient,
        ISecretService secrets,
        ISuspiciousMessageService suspiciousMessageService,
        ISmsRateLimiter rateLimiter,
        ILogger<SmsService> logger)
    {
        _sendValidator = sendValidator;
        _templateValidator = templateValidator;
        _configurationValidator = configurationValidator;
        _repository = repository;
        _workQueue = workQueue;
        _messageClient = messageClient;
        _secrets = secrets;
        _suspiciousMessageService = suspiciousMessageService;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<SmsMutationResponse> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _sendValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return FromValidation(validation.Errors);
        }

        if (SmsTenantContext.CurrentTenantId() is not { } tenantId)
        {
            return NoTenant();
        }

        return await AcceptAndQueueAsync(CreateMessage(tenantId, request.DestinationNumbers, request.MessageText, request.CorrelationId), cancellationToken);
    }

    public async Task<SmsMutationResponse> SendByTemplateAsync(SendSmsByTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _templateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return FromValidation(validation.Errors);
        }

        if (SmsTenantContext.CurrentTenantId() is not { } tenantId)
        {
            return NoTenant();
        }

        var template = await _repository.GetTemplateAsync(tenantId, request.TemplateName, request.Language, cancellationToken);
        if (template == null)
        {
            return SmsMutationResponse.Failure("TemplateName", "SMS template was not found for the requested name and language.");
        }

        var body = SmsTemplateRenderer.Render(template.Body, request.DataContext, out var missing);
        if (missing.Count > 0)
        {
            return SmsMutationResponse.Failure("DataContext", $"Missing values for template placeholders: {string.Join(", ", missing)}.");
        }

        var message = CreateMessage(tenantId, request.DestinationNumbers, body, request.CorrelationId);
        message.TemplateName = request.TemplateName;
        message.Language = request.Language;
        message.DataContext = request.DataContext;
        return await AcceptAndQueueAsync(message, cancellationToken);
    }

    public async Task<SmsMutationResponse> SaveProviderConfigurationAsync(SaveSmsProviderConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _configurationValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return FromValidation(validation.Errors);
        }

        if (SmsTenantContext.CurrentTenantId() is not { } tenantId)
        {
            return NoTenant();
        }

        SmsProviderConfiguration configuration;
        if (string.IsNullOrWhiteSpace(request.ConfigurationId))
        {
            configuration = new SmsProviderConfiguration { TenantId = tenantId };
        }
        else
        {
            var existing = await _repository.GetProviderConfigurationAsync(tenantId, request.ConfigurationId, cancellationToken);
            if (existing == null)
            {
                return SmsMutationResponse.Failure("ConfigurationId", "SMS provider configuration was not found.");
            }

            configuration = existing;
        }

        configuration.Name = request.Name;
        configuration.ProviderType = request.ProviderType;
        configuration.IsDefault = request.IsDefault;
        configuration.IsEnabled = request.IsEnabled;
        configuration.SenderNumber = request.SenderNumber?.Trim() ?? string.Empty;
        configuration.SenderName = string.IsNullOrWhiteSpace(request.SenderName) ? null : request.SenderName.Trim();
        configuration.SenderNameExcludedPrefixes = request.SenderNameExcludedPrefixes?
            .Select(p => p.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [.. SmsProviderConfiguration.DefaultSenderNameExcludedPrefixes];
        configuration.AccountId = request.AccountId ?? string.Empty;
        configuration.MessagingProfileId = request.MessagingProfileId;
        configuration.WebhookPublicKey = request.WebhookPublicKey;
        configuration.StatusCallbackBaseUrl = request.StatusCallbackBaseUrl;
        configuration.MaxRetryAttempts = request.MaxRetryAttempts;
        configuration.DeliveryCheckDelayMinutes = request.DeliveryCheckDelayMinutes;
        configuration.RateLimit = request.RateLimit;
        configuration.SpamFilter = request.SpamFilter;

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            configuration.ApiKeySecretId = await StoreApiKeyAsync(configuration, request.ApiKey, cancellationToken);
        }

        await _repository.SaveProviderConfigurationAsync(configuration, cancellationToken);
        if (configuration.IsDefault)
        {
            await _repository.ClearOtherDefaultsAsync(tenantId, configuration.ItemId, cancellationToken);
        }

        return SmsMutationResponse.Success(configuration.ItemId);
    }

    public async Task<SmsProviderConfigurationResponse> GetProviderConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var configuration = SmsTenantContext.CurrentTenantId() is { } tenantId
            ? await _repository.GetActiveProviderConfigurationAsync(tenantId, cancellationToken: cancellationToken)
            : null;

        return new SmsProviderConfigurationResponse
        {
            IsSuccess = configuration != null,
            Configuration = configuration == null ? null : SmsProviderConfigurationView.From(configuration),
            Errors = configuration == null ? new Dictionary<string, string> { ["Configuration"] = "No active SMS provider configuration was found." } : []
        };
    }

    /// <summary>
    /// The key goes to Blocks Secrets and only its id comes back to the configuration. An existing
    /// secret is rotated in place so the id, and anything holding it, stays valid.
    /// </summary>
    private async Task<string> StoreApiKeyAsync(SmsProviderConfiguration configuration, string apiKey, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuration.ApiKeySecretId))
        {
            await _secrets.RotateAsync(configuration.ApiKeySecretId, new RotateSecretRequest { Value = apiKey }, cancellationToken);
            return configuration.ApiKeySecretId;
        }

        // Service type: readable by the tenant's own worker and webhooks, never revealed in the UI.
        return await _secrets.SetAsync(new SetSecretRequest
        {
            Name = $"sms-{SmsCallbackUrls.ProviderSegment(configuration.ProviderType)}-{configuration.ItemId}",
            Value = apiKey,
            Type = SecretTypes.Service,
            Tags = ["sms"]
        }, cancellationToken);
    }

    private async Task<SmsMutationResponse> AcceptAndQueueAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        using var scope = SmsLogScope.Begin(_logger, message.TenantId, message.CorrelationId, message.ItemId);

        var configuration = await _repository.GetActiveProviderConfigurationAsync(message.TenantId, cancellationToken: cancellationToken);
        if (configuration == null)
        {
            message.Status = SmsMessageStatus.Failed;
            message.LastErrorCode = "sms_provider_configuration_missing";
            await _repository.SaveMessageAsync(message, cancellationToken);
            return SmsMutationResponse.Failure("Configuration", "No active SMS provider configuration was found.");
        }

        var numbers = message.Recipients.Select(r => r.Number).ToArray();
        message.ProviderType = configuration.ProviderType;
        var risk = _suspiciousMessageService.Analyze(message.MessageText, numbers, configuration.SpamFilter);
        message.RiskLevel = risk.RiskLevel;
        message.RiskReasons = risk.Reasons;

        if (risk.ShouldBlock)
        {
            message.Status = SmsMessageStatus.Quarantined;
            await _repository.SaveMessageAsync(message, cancellationToken);
            return SmsMutationResponse.Failure("Security", string.Join(" ", risk.Reasons));
        }

        var rateLimit = await _rateLimiter.CheckAsync(message.TenantId, numbers, configuration.RateLimit, cancellationToken);
        if (!rateLimit.IsAllowed)
        {
            message.Status = SmsMessageStatus.Failed;
            message.LastErrorCode = "sms_rate_limited";
            message.LastErrorMessage = rateLimit.Reason;
            await _repository.SaveMessageAsync(message, cancellationToken);
            return SmsMutationResponse.Failure("RateLimit", rateLimit.Reason ?? "SMS rate limit exceeded.");
        }

        message.Status = SmsMessageStatus.Accepted;
        await _repository.SaveMessageAsync(message, cancellationToken);

        try
        {
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<SendSmsCommand>
            {
                ConsumerName = SmsConstants.SmsSendQueue,
                Payload = new SendSmsCommand { MessageId = message.ItemId, TenantId = message.TenantId, CorrelationId = message.CorrelationId }
            });
        }
        catch (Exception ex)
        {
            // The broker is the fast path, not the only one: the root-database queue picks the
            // message up instead, so an accepted SMS is not lost to a broker blip.
            _logger.LogWarning(ex, "SmsService: broker publish failed, falling back to the work queue");
            try
            {
                await _workQueue.ScheduleAsync(message.TenantId, message.ItemId, message.CorrelationId, SmsWorkKind.Retry, DateTime.UtcNow, cancellationToken);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "SmsService: failed to queue");
                await _repository.SetStatusAsync(message.TenantId, message.ItemId, SmsMessageStatus.Failed, "sms_queue_publish_failed", ex.Message, cancellationToken);
                return SmsMutationResponse.Failure("Queue", "SMS request could not be queued. Please retry.");
            }
        }

        await _repository.MarkQueuedAsync(message.TenantId, message.ItemId, cancellationToken);
        _logger.LogInformation("SmsService: accepted Recipients={Recipients}", message.Recipients.Count);
        return SmsMutationResponse.Success(message.ItemId);
    }

    private static SmsMessage CreateMessage(string tenantId, string[] destinationNumbers, string messageText, string? correlationId) => new()
    {
        TenantId = tenantId,
        Recipients = destinationNumbers
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => new SmsRecipient { Number = n })
            .ToList(),
        MessageText = messageText,
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId
    };

    private static SmsMutationResponse NoTenant() =>
        SmsMutationResponse.Failure("Tenant", "The request has no tenant context.");

    private static SmsMutationResponse FromValidation(IEnumerable<FluentValidation.Results.ValidationFailure> failures) => new()
    {
        IsSuccess = false,
        Errors = failures
            .GroupBy(x => x.PropertyName)
            .ToDictionary(x => x.Key, x => x.First().ErrorMessage)
    };
}
