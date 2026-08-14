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

        // Singleton so the cache is actually shared. Scoped, every request would get an empty
        // one and the hot path would read the database every time regardless.
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
            IValidator<CreatePriceRequest>,
            CreatePriceRequestValidator>();
        services.AddTransient<
            IValidator<CreateSubscriptionRequest>,
            CreateSubscriptionRequestValidator>();
        services.AddTransient<
            IValidator<RecordUsageRequest>,
            RecordUsageRequestValidator>();

        // Scoped: these read the caller's context, which belongs to one request.
        services.AddScoped<
            ISubscriptionContextResolver,
            SubscriptionContextResolver>();
        services.AddScoped<IPlanCatalogueService, PlanCatalogueService>();
        services.AddScoped<
            ISubscriptionCreationService,
            SubscriptionCreationService>();
        services.AddScoped<
            ISubscriptionCheckoutService,
            SubscriptionCheckoutService>();
        services.AddScoped<
            ISubscriptionCancellationService,
            SubscriptionCancellationService>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IUsageRecordingService, UsageRecordingService>();
        services.AddScoped<IUsageThresholdEvaluator, UsageThresholdEvaluator>();
        services.AddScoped<
            ISubscriptionActivationProcessor,
            SubscriptionActivationProcessor>();
        services.AddScoped<
            ISubscriptionOutboxProcessor,
            SubscriptionOutboxProcessor>();
        services.AddScoped<
            ISubscriptionBillingGateway,
            RecurringChargeBillingGateway>();
        services.AddScoped<
            ISubscriptionRenewalService,
            SubscriptionRenewalService>();

        return services;
    }
}
