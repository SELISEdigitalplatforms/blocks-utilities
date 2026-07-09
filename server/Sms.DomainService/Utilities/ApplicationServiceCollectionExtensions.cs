using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Sms.DomainService.Providers;
using Sms.DomainService.Repositories;
using Sms.DomainService.Requests;
using Sms.DomainService.Services;
using Sms.DomainService.Validators;

namespace Sms.DomainService.Utilities;

public static class ApplicationServiceCollectionExtensions
{
    public static void RegisterAllSmsApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ISmsRepository, SmsRepository>();
        services.AddSingleton<ISmsService, SmsService>();
        services.AddSingleton<ISmsProcessingService, SmsProcessingService>();
        services.AddSingleton<ISmsEventPublisher, SmsEventPublisher>();
        services.AddSingleton<ISmsRateLimiter, SmsRateLimiter>();
        services.AddSingleton<ISuspiciousMessageService, SuspiciousMessageService>();
        services.AddSingleton<ISmsRetryPolicy, SmsRetryPolicy>();
        services.AddSingleton<ISmsProviderFactory, SmsProviderFactory>();
        services.AddSingleton<ISmsProvider, TwilioSmsProvider>();
        services.AddSingleton<ISmsProvider, TelnyxSmsProvider>();
        services.AddTransient<IValidator<SendSmsRequest>, SendSmsRequestValidator>();
        services.AddTransient<IValidator<SendSmsByTemplateRequest>, SendSmsByTemplateRequestValidator>();
    }
}
