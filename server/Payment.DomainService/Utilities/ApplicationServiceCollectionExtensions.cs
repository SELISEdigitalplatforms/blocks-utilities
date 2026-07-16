using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Validators;

namespace Payment.DomainService.Utilities;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection RegisterPaymentDomainServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.AddSingleton<IPaymentRepository, PaymentRepository>();
        services.AddSingleton<IPaymentWebhookInboxRepository, PaymentWebhookInboxRepository>();
        services.AddSingleton<IStoredPaymentMethodRepository, StoredPaymentMethodRepository>();
        services.AddSingleton<IPaymentProviderCache, PaymentProviderCache>();
        services.AddSingleton<ICurrencyMinorUnitResolver, CurrencyMinorUnitResolver>();
        services.AddSingleton<ICheckoutUrlPolicy, CheckoutUrlPolicy>();
        services.AddSingleton<ICheckoutCallbackStateProtector, CheckoutCallbackStateProtector>();
        services.AddSingleton<IPaymentWebhookReferenceService, PaymentWebhookReferenceService>();
        services.AddSingleton<IShopperReferenceService, ShopperReferenceService>();
        services.AddSingleton<IWebhookTenantResolver, WebhookTenantResolver>();
        services.AddSingleton<IWebhookSignatureValidator, WebhookSignatureValidator>();
        services.AddSingleton<IWebhookPayloadFactory, WebhookPayloadFactory>();
        services.AddSingleton<IPaymentRateLimiter, PaymentRateLimiter>();
        services.AddSingleton<ICheckoutCallbackRateLimiter, CheckoutCallbackRateLimiter>();
        services.AddSingleton<ICheckoutCallbackRequestValidator, CheckoutCallbackRequestValidator>();
        services.AddSingleton<IPaymentLockRenewalScheduler, PaymentLockRenewalScheduler>();
        services.AddSingleton<IPaymentDistributedLock, PaymentDistributedLock>();
        services.AddSingleton<IPaymentIdempotencyCache, PaymentIdempotencyCache>();
        services.AddSingleton<IPaymentExecutionContextResolver, PaymentExecutionContextResolver>();
        services.AddSingleton<IPaymentResponseMapper, PaymentResponseMapper>();
        services.AddSingleton<IPaymentOutboxEventFactory, PaymentOutboxEventFactory>();
        services.AddSingleton<IPaymentReservationService, PaymentReservationService>();
        services.AddSingleton<IPaymentStateTransitionService, PaymentStateTransitionService>();
        services.AddSingleton<IPaymentInitiationService, HostedCheckoutInitiationService>();
        services.AddSingleton<IPaymentSessionClient, HostedCheckoutSessionClient>();
        services.AddSingleton<ICheckoutResultClient, HostedCheckoutResultClient>();
        services.AddSingleton<IStoredPaymentMethodProviderClient, StoredPaymentMethodProviderClient>();
        services.AddSingleton<ICheckoutResultValidator, CheckoutResultValidator>();
        services.AddSingleton<ICheckoutStatusMapper, CheckoutStatusMapper>();
        services.AddSingleton<IPaymentRedirectBuilder, PaymentRedirectBuilder>();
        services.AddScoped<ICheckoutCallbackService, CheckoutCallbackService>();
        services.AddScoped<ICheckoutCallbackContextResolver, CheckoutCallbackContextResolver>();
        services.AddScoped<ICheckoutObservationService, CheckoutObservationService>();
        services.AddScoped<IPaymentWebhookIntakeService, PaymentWebhookIntakeService>();
        services.AddScoped<IPaymentWebhookStateTransitionService, PaymentWebhookStateTransitionService>();
        services.AddScoped<IPaymentWebhookProcessor, PaymentWebhookProcessor>();
        services.AddScoped<IStoredPaymentMethodService, StoredPaymentMethodService>();
        services.AddScoped<IStoredPaymentMethodRecoveryProcessor, StoredPaymentMethodService>();
        services.AddTransient<IValidator<MakePaymentRequest>, MakePaymentRequestValidator>();
        services.AddScoped<IPaymentPreflightService, PaymentPreflightService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentOutboxProcessor, PaymentOutboxProcessor>();
        services.AddScoped<IPaymentRecoveryProcessor, PaymentRecoveryProcessor>();
        return services;
    }
}
