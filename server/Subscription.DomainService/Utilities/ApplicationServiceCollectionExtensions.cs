using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Simulation;
using Subscription.DomainService.Validators;

namespace Subscription.DomainService.Utilities;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the subscription capability. Called by both the Api and the Worker, because
    /// the same services back the request path and the background sweeps.
    /// </summary>
    /// <param name="hostEnvironment">
    /// Passed so a Production host that somehow has <c>SubscriptionSimulation:Enabled</c> set to
    /// <c>true</c> fails to start rather than exposing the harness. Optional only so existing
    /// callers compile unchanged; both actual hosts (Api, Worker) pass their own environment.
    /// </param>
    public static IServiceCollection RegisterSubscriptionDomainServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? hostEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SubscriptionOptions>(
            configuration.GetSection(SubscriptionOptions.SectionName));

        var simulationSection = configuration.GetSection(SubscriptionSimulationOptions.SectionName);
        services.Configure<SubscriptionSimulationOptions>(simulationSection);

        if (hostEnvironment is { } environment &&
            environment.IsProduction() &&
            (simulationSection.GetValue<bool>(nameof(SubscriptionSimulationOptions.Enabled)) ||
             simulationSection.GetValue<bool>(nameof(SubscriptionSimulationOptions.DataConsoleEnabled))))
        {
            // The harness can rewrite billing history through real domain processors, and the
            // data console reads and writes Mongo documents directly. Refusing to start is
            // deliberately louder than the request-time 404 guard the controller also applies —
            // a misconfigured Production deploy should never come up quietly.
            throw new InvalidOperationException(
                "SubscriptionSimulation:Enabled and SubscriptionSimulation:DataConsoleEnabled " +
                "must not be true in a Production environment.");
        }

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
        services.AddSingleton<
            ISubscriptionSimulationRunRepository,
            SubscriptionSimulationRunRepository>();

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

        // Singleton for the same reason the tenant source is: the queue lives in the root
        // database and needs no ambient tenant, so there is nothing per-request about it.
        // Singleton so the mode is captured once per process: the sweep and the scheduler have
        // to agree about which of them executes work, and two reads of configuration can disagree.
        services.AddSingleton<SubscriptionSchedulerMode>();
        services.AddSingleton<ISubscriptionWorkQueue, SubscriptionWorkQueue>();
        services.AddSingleton<ISubscriptionWorkScheduler, SubscriptionWorkScheduler>();
        services.AddSingleton<ISubscriptionWorkDispatcher, SubscriptionWorkDispatcher>();

        // Scoped, and resolved per work item: a handler runs inside an established tenant context
        // and depends on processors that read one tenant's database.
        services.AddScoped<ISubscriptionWorkHandler, ActivationSettlementWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, ActivationRecoveryWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, SettlementReservationRecoveryWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, RenewalWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, UsagePeriodClosureWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, UsageInvoiceChargeWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, OutboxPublicationWorkHandler>();

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
        // Also registered as itself, held by SubscriptionSimulationBillingGateway below — the
        // real charging logic every provider still goes through, scripted outcome or not.
        services.AddScoped<SubscriptionBillingGatewayResolver>();
        services.AddScoped<
            ISubscriptionSimulatedOutcomeSource,
            SubscriptionSimulatedOutcomeSource>();
        // The ISubscriptionBillingGateway DI entry itself. Always this decorator, never the bare
        // resolver: with the harness disabled or nothing scripted it delegates straight through,
        // so this changes nothing for a real request — see the class remarks.
        services.AddScoped<
            ISubscriptionBillingGateway,
            SubscriptionSimulationBillingGateway>();
        services.AddScoped<
            ISubscriptionRenewalService,
            SubscriptionRenewalService>();
        services.AddScoped<
            ISubscriptionRenewalProcessor,
            SubscriptionRenewalProcessor>();
        services.AddScoped<
            ISubscriptionUsageRatingProcessor,
            SubscriptionUsageRatingProcessor>();
        services.AddScoped<
            ISubscriptionSimulationService,
            SubscriptionSimulationService>();
        services.AddScoped<
            ISubscriptionSimulationDataConsoleService,
            SubscriptionSimulationDataConsoleService>();

        return services;
    }
}
