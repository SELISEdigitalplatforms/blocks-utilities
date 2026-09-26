using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Sms.DomainService.Providers;
using Sms.DomainService.Repositories;
using Sms.DomainService.Requests;
using Sms.DomainService.Scheduling;
using Sms.DomainService.Services;
using Sms.DomainService.Validators;

namespace Sms.DomainService.Utilities;

public static class ApplicationServiceCollectionExtensions
{
    /// <remarks>
    /// Anything that reads a provider key is scoped, because <c>ISecretService</c> is: it reads the
    /// ambient tenant. Callers without a request (consumers, the work-queue loop) open a scope per
    /// unit of work after entering the tenant's context. Expects <c>AddBlocksSecrets</c> to have run
    /// (<c>RegisterUtilityServices</c> does it).
    /// </remarks>
    public static void RegisterAllSmsApplicationServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient(TwilioSmsProvider.HttpClientName);

        services.AddSingleton<ISmsRepository, SmsRepository>();
        services.AddSingleton<ISmsWorkQueue, SmsWorkQueue>();
        services.AddSingleton<ISmsEventPublisher, SmsEventPublisher>();
        services.AddSingleton<ISmsRateLimiter, SmsRateLimiter>();
        services.AddSingleton<ISuspiciousMessageService, SuspiciousMessageService>();
        services.AddSingleton<ISmsRetryPolicy, SmsRetryPolicy>();
        services.AddSingleton<ISmsProviderFactory, SmsProviderFactory>();
        services.AddSingleton<ISmsProvider, TwilioSmsProvider>();
        services.AddSingleton<ISmsProvider, TelnyxSmsProvider>();

        services.AddScoped<ISmsProviderContextResolver, SmsProviderContextResolver>();
        services.AddScoped<ISmsService, SmsService>();
        services.AddScoped<ISmsProcessingService, SmsProcessingService>();
        services.AddScoped<ISmsWebhookService, SmsWebhookService>();

        services.AddTransient<IValidator<SendSmsRequest>, SendSmsRequestValidator>();
        services.AddTransient<IValidator<SendSmsByTemplateRequest>, SendSmsByTemplateRequestValidator>();
        services.AddTransient<IValidator<SaveSmsProviderConfigurationRequest>, SaveSmsProviderConfigurationRequestValidator>();
    }
}
