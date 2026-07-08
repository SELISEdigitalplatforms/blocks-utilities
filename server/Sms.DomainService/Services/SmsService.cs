using Blocks.Genesis;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Repositories;
using Sms.DomainService.Requests;
using Sms.DomainService.Responses;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Services;

public class SmsService : ISmsService
{
    private readonly IValidator<SendSmsRequest> _sendValidator;
    private readonly IValidator<SendSmsByTemplateRequest> _templateValidator;
    private readonly ISmsRepository _repository;
    private readonly IMessageClient _messageClient;
    private readonly ISuspiciousMessageService _suspiciousMessageService;
    private readonly ISmsRateLimiter _rateLimiter;
    private readonly ILogger<SmsService> _logger;

    public SmsService(
        IValidator<SendSmsRequest> sendValidator,
        IValidator<SendSmsByTemplateRequest> templateValidator,
        ISmsRepository repository,
        IMessageClient messageClient,
        ISuspiciousMessageService suspiciousMessageService,
        ISmsRateLimiter rateLimiter,
        ILogger<SmsService> logger)
    {
        _sendValidator = sendValidator;
        _templateValidator = templateValidator;
        _repository = repository;
        _messageClient = messageClient;
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

        var message = CreateMessage(request.ProjectKey, request.DestinationNumbers, request.MessageText, request.CorrelationId);
        return await AcceptAndQueueAsync(message, cancellationToken);
    }

    public async Task<SmsMutationResponse> SendByTemplateAsync(SendSmsByTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _templateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return FromValidation(validation.Errors);
        }

        var projectKey = ResolveProjectKey(request.ProjectKey);
        var template = await _repository.GetTemplateAsync(projectKey, request.TemplateName, request.Language, cancellationToken);
        if (template == null)
        {
            return SmsMutationResponse.Failure("TemplateName", "SMS template was not found for the requested name and language.");
        }

        var body = RenderTemplate(template.Body, request.DataContext);
        var message = CreateMessage(projectKey, request.DestinationNumbers, body, request.CorrelationId);
        message.TemplateName = request.TemplateName;
        message.Language = request.Language;
        message.DataContext = request.DataContext;
        return await AcceptAndQueueAsync(message, cancellationToken);
    }

    public async Task<SmsMutationResponse> SaveProviderConfigurationAsync(SaveSmsProviderConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        var projectKey = ResolveProjectKey(request.ProjectKey);
        var configuration = new SmsProviderConfiguration
        {
            ItemId = string.IsNullOrWhiteSpace(request.ConfigurationId) ? Guid.NewGuid().ToString() : request.ConfigurationId,
            ProjectKey = projectKey,
            Name = request.Name,
            ProviderType = request.ProviderType,
            IsDefault = request.IsDefault,
            IsEnabled = request.IsEnabled,
            Sender = request.Sender,
            AccountId = request.AccountId,
            AuthToken = request.AuthToken,
            MessagingProfileId = request.MessagingProfileId,
            StatusCallbackBaseUrl = request.StatusCallbackBaseUrl,
            MaxRetryAttempts = Math.Max(1, request.MaxRetryAttempts),
            RateLimitMaxPerWindow = Math.Max(1, request.RateLimitMaxPerWindow),
            RateLimitWindowSeconds = Math.Max(1, request.RateLimitWindowSeconds),
            DeliveryCheckDelayMinutes = Math.Max(1, request.DeliveryCheckDelayMinutes)
        };

        await _repository.SaveProviderConfigurationAsync(configuration, cancellationToken);
        return SmsMutationResponse.Success(configuration.ItemId);
    }

    public async Task<SmsProviderConfigurationResponse> GetProviderConfigurationAsync(string? projectKey, CancellationToken cancellationToken = default)
    {
        var configuration = await _repository.GetActiveProviderConfigurationAsync(ResolveProjectKey(projectKey), cancellationToken);
        return new SmsProviderConfigurationResponse
        {
            IsSuccess = configuration != null,
            Configuration = configuration,
            Errors = configuration == null ? new Dictionary<string, string> { ["Configuration"] = "No active SMS provider configuration was found." } : []
        };
    }

    public Task<SmsMutationResponse> ProcessTwilioStatusAsync(TwilioSmsStatusCallbackRequest request, CancellationToken cancellationToken = default)
    {
        var providerMessageId = request.MessageSid ?? request.SmsSid;
        var status = NormalizeTwilioStatus(request.MessageStatus ?? request.SmsStatus);
        return ApplyProviderStatusAsync(providerMessageId, status, request.ErrorCode, request.ErrorMessage, cancellationToken);
    }

    public Task<SmsMutationResponse> ProcessTelnyxStatusAsync(TelnyxSmsStatusCallbackRequest request, CancellationToken cancellationToken = default)
    {
        var providerMessageId = request.Data?.Payload?.Id ?? request.Data?.Id;
        var providerStatus = request.Data?.Payload?.To?.Status ?? request.Data?.EventType;
        var error = request.Data?.Payload?.Errors.FirstOrDefault();
        var status = NormalizeTelnyxStatus(providerStatus);
        return ApplyProviderStatusAsync(providerMessageId, status, error?.Code, error?.Title, cancellationToken);
    }

    private async Task<SmsMutationResponse> AcceptAndQueueAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        var configuration = await _repository.GetActiveProviderConfigurationAsync(message.ProjectKey, cancellationToken);
        if (configuration == null)
        {
            message.Status = SmsMessageStatus.Failed;
            message.LastErrorCode = "sms_provider_configuration_missing";
            message.LastErrorMessage = "No active SMS provider configuration was found.";
            await _repository.SaveMessageAsync(message, cancellationToken);
            return SmsMutationResponse.Failure("Configuration", "No active SMS provider configuration was found.");
        }

        message.ProviderType = configuration.ProviderType;
        var risk = _suspiciousMessageService.Analyze(message.MessageText, message.DestinationNumbers);
        message.RiskLevel = risk.RiskLevel;
        message.RiskReasons = risk.Reasons;

        if (risk.ShouldBlock)
        {
            message.Status = SmsMessageStatus.Quarantined;
            await _repository.SaveMessageAsync(message, cancellationToken);
            return SmsMutationResponse.Failure("Security", string.Join(" ", risk.Reasons));
        }

        var rateLimit = await _rateLimiter.CheckAsync(message, configuration, cancellationToken);
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

        var outbox = new SmsOutboxMessage
        {
            MessageId = message.ItemId,
            TenantId = message.TenantId,
            ProjectKey = message.ProjectKey,
            CorrelationId = message.CorrelationId,
            MaxRetryCount = configuration.MaxRetryAttempts
        };
        await _repository.SaveOutboxAsync(outbox, cancellationToken);

        try
        {
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<SendSmsCommand>
            {
                ConsumerName = SmsConstants.SmsSendQueue,
                Payload = new SendSmsCommand
                {
                    MessageId = message.ItemId,
                    TenantId = message.TenantId,
                    ProjectKey = message.ProjectKey,
                    CorrelationId = message.CorrelationId
                }
            });

            await _repository.UpdateMessageStatusAsync(message.ProjectKey, message.ItemId, SmsMessageStatus.Queued, cancellationToken: cancellationToken);
            _logger.LogInformation("SmsService: accepted MessageId={MessageId}, TenantId={TenantId}, CorrelationId={CorrelationId}", message.ItemId, message.TenantId, message.CorrelationId);
            return SmsMutationResponse.Success(message.ItemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SmsService: failed to queue MessageId={MessageId}, CorrelationId={CorrelationId}", message.ItemId, message.CorrelationId);
            await _repository.UpdateOutboxStatusAsync(message.ProjectKey, outbox.ItemId, SmsOutboxStatus.Failed, lastError: ex.Message, cancellationToken: cancellationToken);
            await _repository.UpdateMessageStatusAsync(message.ProjectKey, message.ItemId, SmsMessageStatus.Failed, errorCode: "sms_queue_publish_failed", errorMessage: ex.Message, cancellationToken: cancellationToken);
            return SmsMutationResponse.Failure("Queue", "SMS request could not be offloaded. Please retry.");
        }
    }

    private async Task<SmsMutationResponse> ApplyProviderStatusAsync(string? providerMessageId, SmsMessageStatus status, string? errorCode, string? errorMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerMessageId))
        {
            return SmsMutationResponse.Failure("ProviderMessageId", "Provider message id is required.");
        }

        var projectKey = ResolveProjectKey(null);
        var message = await _repository.GetMessageByProviderMessageIdAsync(projectKey, providerMessageId, cancellationToken);
        if (message == null)
        {
            return SmsMutationResponse.Failure("ProviderMessageId", "SMS message was not found for provider callback.");
        }

        if (message.Status is SmsMessageStatus.Delivered or SmsMessageStatus.Undelivered or SmsMessageStatus.DeliveryFailed)
        {
            return SmsMutationResponse.Success(message.ItemId);
        }

        await _repository.UpdateMessageStatusAsync(projectKey, message.ItemId, status, errorCode: errorCode, errorMessage: errorMessage, cancellationToken: cancellationToken);
        return SmsMutationResponse.Success(message.ItemId);
    }

    private SmsMessage CreateMessage(string? projectKey, string[] destinationNumbers, string messageText, string? correlationId)
    {
        var context = BlocksContext.GetContext();
        var resolvedProjectKey = ResolveProjectKey(projectKey);
        return new SmsMessage
        {
            TenantId = context?.TenantId ?? resolvedProjectKey,
            ProjectKey = resolvedProjectKey,
            DestinationNumbers = destinationNumbers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MessageText = messageText,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId
        };
    }

    private static string ResolveProjectKey(string? projectKey)
    {
        return !string.IsNullOrWhiteSpace(projectKey) ? projectKey : BlocksContext.GetContext()?.TenantId ?? string.Empty;
    }

    private static string RenderTemplate(string templateBody, Dictionary<string, string> dataContext)
    {
        var body = templateBody;
        foreach (var item in dataContext)
        {
            body = body.Replace("{{" + item.Key + "}}", item.Value, StringComparison.OrdinalIgnoreCase);
        }

        return body;
    }

    private static SmsMessageStatus NormalizeTwilioStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "delivered" => SmsMessageStatus.Delivered,
            "undelivered" => SmsMessageStatus.Undelivered,
            "failed" => SmsMessageStatus.DeliveryFailed,
            _ => SmsMessageStatus.Submitted
        };
    }

    private static SmsMessageStatus NormalizeTelnyxStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "delivered" or "message.delivered" => SmsMessageStatus.Delivered,
            "delivery_failed" or "sending_failed" or "message.delivery_failed" => SmsMessageStatus.DeliveryFailed,
            _ => SmsMessageStatus.Submitted
        };
    }

    private static SmsMutationResponse FromValidation(IEnumerable<FluentValidation.Results.ValidationFailure> failures)
    {
        return new SmsMutationResponse
        {
            IsSuccess = false,
            Errors = failures
                .GroupBy(x => x.PropertyName)
                .ToDictionary(x => x.Key, x => x.First().ErrorMessage)
        };
    }
}

