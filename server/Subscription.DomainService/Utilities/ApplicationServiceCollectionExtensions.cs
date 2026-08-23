using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Validators;

namespace Subscription.DomainService.Utilities;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the subscription capability. Called by both the Api and the Worker, because
    /// the same services back the request path and the background sweeps.
    /// </summary>
    public static IServiceCollection RegisterSubscriptionDomainServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SubscriptionOptions>(
            configuration.GetSection(SubscriptionOptions.SectionName));

        // Repositories are singletons and take the tenant as an argument, so the same instance
        // serves a request and a background sweep. They hold no per-tenant state beyond the
        // record of which tenants they have already indexed.
        services.AddSingleton<
            ISubscriptionCatalogueRepository,
            SubscriptionCatalogueRepository>();
        services.AddSingleton<
            IBillingAccountRepository,
            BillingAccountRepository>();
        services.AddSingleton<
            ISubscriptionRepository,
            SubscriptionRepository>();
        services.AddSingleton<
            ISubscriptionUsageRepository,
            SubscriptionUsageRepository>();
        services.AddSingleton<
            ISubscriptionPaymentLinkRepository,
            SubscriptionPaymentLinkRepository>();
        services.AddSingleton<
            ISubscriptionUsageInvoiceRepository,
            SubscriptionUsageInvoiceRepository>();
        services.AddSingleton<
            ISubscriptionInvoiceHistoryRepository,
            SubscriptionInvoiceHistoryRepository>();
        services.AddSingleton<ISubscriptionDiscountRepository, SubscriptionDiscountRepository>();
        services.AddSingleton<ISubscriptionAuditRepository, SubscriptionAuditRepository>();

        // Singleton so the cache is actually shared. Scoped, every request would get an empty
        // one and the hot path would read the database every time regardless.
        services.AddSingleton<
            ISubscriptionTenantSource,
            RootDatabaseTenantSource>();

        // Singleton so the roster is actually cached. Scoped, every sweep would read the
        // registry again and the refresh interval would mean nothing.
        services.AddSingleton<
            ISubscriptionTenantDirectory,
            SubscriptionTenantDirectory>();

        services.AddSingleton<IEntitlementSnapshotCache, EntitlementSnapshotCache>();
        services.AddSingleton<IPlanResponseMapper, PlanResponseMapper>();
        services.AddSingleton<
            ISubscriptionResponseMapper,
            SubscriptionResponseMapper>();
        services.AddSingleton<
            ISubscriptionOutboxEventFactory,
            SubscriptionOutboxEventFactory>();

        services.AddTransient<
            IValidator<CreatePlanRequest>,
            CreatePlanRequestValidator>();
        services.AddTransient<
            IValidator<UpdatePlanRequest>,
            UpdatePlanRequestValidator>();
        services.AddTransient<
            IValidator<CreatePriceRequest>,
            CreatePriceRequestValidator>();
        services.AddTransient<
            IValidator<CreateSubscriptionRequest>,
            CreateSubscriptionRequestValidator>();
        services.AddTransient<IValidator<CreateDiscountRequest>, CreateDiscountRequestValidator>();
        services.AddTransient<
            IValidator<ChangeSubscriptionPlanRequest>,
            ChangeSubscriptionPlanRequestValidator>();
        services.AddTransient<
            IValidator<ChangeQuantityRequest>,
            ChangeQuantityRequestValidator>();
        services.AddTransient<
            IValidator<RecordUsageRequest>,
            RecordUsageRequestValidator>();

        // Scoped: these read the caller's context, which belongs to one request.
        services.AddScoped<
            ISubscriptionContextResolver,
            SubscriptionContextResolver>();
        services.AddScoped<IPlanCatalogueService, PlanCatalogueService>();
        services.AddScoped<IDiscountCatalogueService, DiscountCatalogueService>();
        services.AddScoped<
            ISubscriptionCreationService,
            SubscriptionCreationService>();
        services.AddScoped<ISubscriptionAuditTrail, SubscriptionAuditTrail>();
        services.AddScoped<
            ISubscriptionCheckoutService,
            SubscriptionCheckoutService>();
        services.AddScoped<
            ISubscriptionCancellationService,
            SubscriptionCancellationService>();
        services.AddScoped<
            ISubscriptionPlanChangeService,
            SubscriptionPlanChangeService>();
        services.AddScoped<
            ISubscriptionQuantityChangeService,
            SubscriptionQuantityChangeService>();
        services.AddScoped<
            ISubscriptionInvoiceDocumentService,
            SubscriptionInvoiceDocumentService>();
        services.AddScoped<
            ISubscriptionInvoiceHistoryService,
            SubscriptionInvoiceHistoryService>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IMeterAllowanceResolver, MeterAllowanceResolver>();
        services.AddScoped<IUsageRecordingService, UsageRecordingService>();
        services.AddScoped<IUsageThresholdEvaluator, UsageThresholdEvaluator>();
        services.AddScoped<IUsageThresholdEmailService, UsageThresholdEmailService>();
        services.AddScoped<
            ISubscriptionActivationProcessor,
            SubscriptionActivationProcessor>();
        services.AddScoped<
            ISubscriptionSettlementReservationProcessor,
            SubscriptionSettlementReservationProcessor>();
        services.AddScoped<
            ISubscriptionOutboxProcessor,
            SubscriptionOutboxProcessor>();
        // Registered as themselves — SubscriptionBillingGatewayResolver picks between them by
        // provider name, so neither is the ISubscriptionBillingGateway DI entry on its own.
        services.AddScoped<RecurringChargeBillingGateway>();
        services.AddScoped<StripeInvoiceBillingGateway>();
        services.AddScoped<
            ISubscriptionBillingGateway,
            SubscriptionBillingGatewayResolver>();
        services.AddScoped<
            ISubscriptionRenewalService,
            SubscriptionRenewalService>();
        services.AddScoped<
            ISubscriptionRenewalProcessor,
            SubscriptionRenewalProcessor>();
        services.AddScoped<
            ISubscriptionUsageRatingProcessor,
            SubscriptionUsageRatingProcessor>();

        return services;
    }
}
