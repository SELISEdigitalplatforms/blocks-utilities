using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Providers.Stripe;
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
        services.AddHostedService<PaymentConfigurationReadinessLogger>();
        services.AddSingleton<IPaymentRepository, PaymentRepository>();
        services.AddSingleton<
            IPaymentQueryRepository,
            PaymentQueryRepository>();
        services.AddSingleton<IPaymentRefundRepository, PaymentRefundRepository>();
        services.AddSingleton<IPaymentCaptureRepository, PaymentCaptureRepository>();
        services.AddSingleton<IPaymentWebhookInboxRepository, PaymentWebhookInboxRepository>();
        services.AddSingleton<IStoredPaymentMethodRepository, StoredPaymentMethodRepository>();
        services.AddSingleton<IProviderSecretReader, ProviderSecretReader>();
        services.AddSingleton<
            IProviderSecretMigrationService,
            ProviderSecretMigrationService>();
        services.AddHostedService<ProviderSecretMigrationStartupTask>();
        services.AddSingleton<IProviderSecretHydrator, AdyenSecretHydrator>();
        services.AddSingleton<IProviderSecretHydrator, StripeSecretHydrator>();
        services.AddSingleton<
            IProviderCredentialRotationStrategy,
            AdyenCredentialRotationStrategy>();
        services.AddSingleton<
            IProviderCredentialRotationStrategy,
            StripeCredentialRotationStrategy>();
        services.AddSingleton<
            IPaymentProviderSecretHydrator,
            PaymentProviderSecretHydrator>();
        services.AddSingleton<
            IPaymentTenantContextScopeFactory,
            PaymentTenantContextScopeFactory>();
        services.AddSingleton<
            IPaymentWorkDispatcher,
            PaymentWorkDispatcher>();
        services.AddSingleton<
            IPaymentProviderCatalog,
            PaymentProviderCatalog>();
        services.AddSingleton<IPaymentProviderCache, PaymentProviderCache>();
        services.AddSingleton<ICurrencyMinorUnitResolver, CurrencyMinorUnitResolver>();
        services.AddSingleton<ICheckoutUrlPolicy, CheckoutUrlPolicy>();
        services.AddSingleton<AdyenEndpointPolicy>();
        services.AddSingleton<IProviderEndpointPolicy>(
            provider => provider.GetRequiredService<AdyenEndpointPolicy>());
        services.AddSingleton<StripeEndpointPolicy>();
        services.AddSingleton<IProviderEndpointPolicy>(
            provider => provider.GetRequiredService<StripeEndpointPolicy>());
        services.AddSingleton<
            IProviderEndpointPolicyResolver,
            ProviderEndpointPolicyResolver>();
        services.AddSingleton<ICheckoutCallbackStateProtector, CheckoutCallbackStateProtector>();
        services.AddSingleton<IPaymentWebhookReferenceService, PaymentWebhookReferenceService>();
        services.AddSingleton<IPaymentRefundWebhookReferenceService, PaymentRefundWebhookReferenceService>();
        services.AddSingleton<IPaymentCaptureWebhookReferenceService, PaymentCaptureWebhookReferenceService>();
        services.AddSingleton<IShopperReferenceService, ShopperReferenceService>();
        services.AddSingleton<IWebhookTenantResolver, WebhookTenantResolver>();
        services.AddSingleton<IWebhookSignatureVerifier, AdyenWebhookSignatureVerifier>();
        services.AddSingleton<IWebhookSignatureVerifier, StripeWebhookSignatureVerifier>();
        services.AddSingleton<
            IWebhookSignatureVerifierResolver,
            WebhookSignatureVerifierResolver>();
        services.AddSingleton<
            IProviderFailureReasonMapper,
            ProviderFailureReasonMapper>();
        services.AddSingleton<IWebhookNormalizer, AdyenWebhookNormalizer>();
        services.AddSingleton<IWebhookNormalizer, StripeWebhookNormalizer>();
        services.AddSingleton<
            IWebhookNormalizerResolver,
            WebhookNormalizerResolver>();
        services.AddSingleton<IPaymentRateLimiter, PaymentRateLimiter>();
        services.AddSingleton<
            IPaymentQueryRateLimiter,
            PaymentQueryRateLimiter>();
        services.AddSingleton<IStoredPaymentMethodRateLimiter, StoredPaymentMethodRateLimiter>();
        services.AddSingleton<ICheckoutCallbackRateLimiter, CheckoutCallbackRateLimiter>();
        services.AddSingleton<ICheckoutCallbackRequestValidator, CheckoutCallbackRequestValidator>();
        services.AddSingleton<IPaymentLockRenewalScheduler, PaymentLockRenewalScheduler>();
        services.AddSingleton<IPaymentDistributedLock, PaymentDistributedLock>();
        services.AddSingleton<IPaymentIdempotencyCache, PaymentIdempotencyCache>();
        services.AddSingleton<IPaymentExecutionContextResolver, PaymentExecutionContextResolver>();
        services.AddSingleton<IPaymentResponseMapper, PaymentResponseMapper>();
        services.AddSingleton<
            IPaymentProviderResponseMapper,
            PaymentProviderResponseMapper>();
        services.AddSingleton<
            IPaymentQueryCursorCodec,
            PaymentQueryCursorCodec>();
        services.AddSingleton<
            IPaymentQueryResponseMapper,
            PaymentQueryResponseMapper>();
        services.AddSingleton<IPaymentOutboxEventFactory, PaymentOutboxEventFactory>();
        services.AddSingleton<IPaymentRefundOutboxEventFactory, PaymentRefundOutboxEventFactory>();
        services.AddSingleton<IPaymentCaptureOutboxEventFactory, PaymentCaptureOutboxEventFactory>();
        services.AddSingleton<IPaymentRefundResponseMapper, PaymentRefundResponseMapper>();
        services.AddSingleton<IPaymentRefundRequestFactory, PaymentRefundRequestFactory>();
        services.AddSingleton<IPaymentFundReturnStrategyResolver, PaymentFundReturnStrategyResolver>();
        services.AddSingleton<IPaymentCaptureRequestFactory, PaymentCaptureRequestFactory>();
        services.AddSingleton<IPaymentCaptureResponseMapper, PaymentCaptureResponseMapper>();
        services.AddSingleton<IPaymentReservationService, PaymentReservationService>();
        services.AddSingleton<IPaymentStateTransitionService, PaymentStateTransitionService>();
        services.AddSingleton<IPaymentInitiationService, HostedCheckoutInitiationService>();
        services.AddSingleton<
            IProviderInitiationRequestFactory,
            AdyenInitiationRequestFactory>();
        services.AddSingleton<
            IProviderInitiationRequestFactory,
            StripeInitiationRequestFactory>();
        services.AddSingleton<
            IProviderInitiationRequestFactoryResolver,
            ProviderInitiationRequestFactoryResolver>();
        services.AddSingleton<IPaymentSessionClient, HostedCheckoutSessionClient>();
        services.AddSingleton<IPaymentSessionClient, StripeCheckoutSessionClient>();
        services.AddSingleton<
            IPaymentSessionClientResolver,
            PaymentSessionClientResolver>();
        services.AddSingleton<IPaymentRefundProviderGateway, CheckoutApiPaymentRefundProviderGateway>();
        services.AddSingleton<IPaymentRefundProviderGateway, StripeRefundProviderGateway>();
        services.AddSingleton<IPaymentRefundProviderGatewayResolver, PaymentRefundProviderGatewayResolver>();
        services.AddSingleton<IPaymentCaptureProviderGateway, CheckoutApiPaymentCaptureProviderGateway>();
        services.AddSingleton<IPaymentCaptureProviderGateway, StripeCaptureProviderGateway>();
        services.AddSingleton<IPaymentCaptureProviderGatewayResolver, PaymentCaptureProviderGatewayResolver>();
        services.AddSingleton<ICheckoutResultClient, HostedCheckoutResultClient>();
        services.AddSingleton<ICheckoutResultClient, StripeCheckoutResultClient>();
        services.AddSingleton<
            ICheckoutResultClientResolver,
            CheckoutResultClientResolver>();
        services.AddSingleton<
            IProviderTokenEncryptionKeyRingProvider,
            ProviderTokenEncryptionKeyRingProvider>();
        services.AddSingleton<IAesGcmSecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<IProviderTokenProtector, ProviderTokenProtector>();
        services.AddSingleton<IStoredPaymentMethodProviderGateway, HostedCheckoutStoredPaymentMethodProviderGateway>();
        services.AddSingleton<IStoredPaymentMethodProviderGateway, StripeStoredPaymentMethodProviderGateway>();
        services.AddSingleton<IStoredPaymentMethodProviderGatewayResolver, StoredPaymentMethodProviderGatewayResolver>();
        services.AddSingleton<
            IStoredPaymentMethodDetailProviderGateway,
            StripeStoredPaymentMethodDetailGateway>();
        services.AddSingleton<
            IStoredPaymentMethodDetailProviderGatewayResolver,
            StoredPaymentMethodDetailProviderGatewayResolver>();
        services.AddSingleton<
            IStoredPaymentChargeProviderGateway,
            CheckoutApiStoredPaymentChargeProviderGateway>();
        services.AddSingleton<
            IStoredPaymentChargeProviderGatewayResolver,
            StoredPaymentChargeProviderGatewayResolver>();
        services.AddSingleton<ICheckoutResultValidator, CheckoutResultValidator>();
        services.AddSingleton<ICheckoutStatusMapper, AdyenCheckoutStatusMapper>();
        services.AddSingleton<ICheckoutStatusMapper, StripeCheckoutStatusMapper>();
        services.AddSingleton<
            ICheckoutStatusMapperResolver,
            CheckoutStatusMapperResolver>();
        services.AddSingleton<IPaymentRedirectBuilder, PaymentRedirectBuilder>();
        services.AddScoped<ICheckoutCallbackService, CheckoutCallbackService>();
        services.AddScoped<ICheckoutCallbackContextResolver, CheckoutCallbackContextResolver>();
        services.AddScoped<ICheckoutObservationService, CheckoutObservationService>();
        services.AddScoped<IPaymentWebhookIntakeService, PaymentWebhookIntakeService>();
        services.AddScoped<IPaymentWebhookStateTransitionService, PaymentWebhookStateTransitionService>();
        services.AddScoped<IPaymentRefundWebhookStateTransitionService, PaymentRefundWebhookStateTransitionService>();
        services.AddScoped<IPaymentCaptureWebhookStateTransitionService, PaymentCaptureWebhookStateTransitionService>();
        services.AddScoped<IStoredPaymentMethodLifecycleService, StoredPaymentMethodLifecycleService>();
        services.AddScoped<IPaymentWebhookProcessor, PaymentWebhookProcessor>();
        services.AddScoped<IStoredPaymentMethodQueryService, StoredPaymentMethodQueryService>();
        services.AddScoped<IStoredPaymentMethodRemovalService, StoredPaymentMethodRemovalService>();
        services.AddScoped<IStoredPaymentMethodRemovalRecoveryProcessor, StoredPaymentMethodRemovalRecoveryProcessor>();
        services.AddTransient<IValidator<MakePaymentRequest>, MakePaymentRequestValidator>();
        services.AddTransient<
            IValidator<RegisterPaymentProviderRequest>,
            RegisterPaymentProviderRequestValidator>();
        services.AddTransient<
            IValidator<UpdatePaymentProviderRequest>,
            UpdatePaymentProviderRequestValidator>();
        services.AddTransient<
            IValidator<RotatePaymentProviderCredentialsRequest>,
            RotatePaymentProviderCredentialsRequestValidator>();
        services.AddTransient<
            IValidator<GetPaymentsRequest>,
            GetPaymentsRequestValidator>();
        services.AddTransient<
            IValidator<CreateRecurringPaymentRequest>,
            CreateRecurringPaymentRequestValidator>();
        services.AddTransient<IValidator<CreatePaymentRefundRequest>, CreatePaymentRefundRequestValidator>();
        services.AddTransient<IValidator<CreatePaymentCaptureRequest>, CreatePaymentCaptureRequestValidator>();
        services.AddScoped<IPaymentPreflightService, PaymentPreflightService>();
        services.AddSingleton<
            IStoredPaymentChargeRequestFactory,
            StoredPaymentChargeRequestFactory>();
        services.AddScoped<
            IRecurringPaymentPreflightService,
            RecurringPaymentPreflightService>();
        services.AddScoped<
            IRecurringPaymentReservationService,
            RecurringPaymentReservationService>();
        services.AddScoped<
            IRecurringPaymentInitiationService,
            RecurringPaymentInitiationService>();
        services.AddScoped<
            IRecurringPaymentService,
            RecurringPaymentService>();
        services.AddScoped<
            IPaymentProviderRegistrationService,
            PaymentProviderRegistrationService>();
        services.AddScoped<
            IPaymentProviderQueryService,
            PaymentProviderQueryService>();
        services.AddScoped<
            IPaymentProviderConfigurationService,
            PaymentProviderConfigurationService>();
        services.AddScoped<
            IPaymentProviderCredentialRotationService,
            PaymentProviderCredentialRotationService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentQueryService, PaymentQueryService>();
        services.AddScoped<IPaymentRefundPreflightService, PaymentRefundPreflightService>();
        services.AddScoped<IPaymentRefundReservationService, PaymentRefundReservationService>();
        services.AddScoped<IPaymentRefundInitiationService, PaymentRefundInitiationService>();
        services.AddScoped<IPaymentRefundService, PaymentRefundService>();
        services.AddScoped<IPaymentCapturePreflightService, PaymentCapturePreflightService>();
        services.AddScoped<IPaymentCaptureReservationService, PaymentCaptureReservationService>();
        services.AddScoped<IPaymentCaptureInitiationService, PaymentCaptureInitiationService>();
        services.AddScoped<IPaymentCaptureService, PaymentCaptureService>();
        services.AddScoped<IPaymentOutboxProcessor, PaymentOutboxProcessor>();
        services.AddScoped<IPaymentRefundOutboxProcessor, PaymentRefundOutboxProcessor>();
        services.AddScoped<IPaymentRecoveryProcessor, PaymentRecoveryProcessor>();
        services.AddScoped<IPaymentRefundRecoveryProcessor, PaymentRefundRecoveryProcessor>();
        services.AddScoped<IPaymentCaptureRecoveryProcessor, PaymentCaptureRecoveryProcessor>();
        return services;
    }
}
