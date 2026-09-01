using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Simulation;
using Subscription.DomainService.Validators;
using Utility.DomainService.PdfGenerator.service;

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

        // A host may replace this with a controlled clock before registering the module. Keep a
        // production default in the container for services whose clock is a required dependency,
        // including the singleton mail-delivery reporter resolved by background work.
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

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
            ISubscriptionUsageCurrentRepository,
            SubscriptionUsageCurrentRepository>();
        services.AddSingleton<
            ISubscriptionPaymentLinkRepository,
            SubscriptionPaymentLinkRepository>();
        services.AddSingleton<
            ISubscriptionUsageInvoiceRepository,
            SubscriptionUsageInvoiceRepository>();
        services.AddSingleton<
            IUsagePeriodClosureRepository,
            UsagePeriodClosureRepository>();
        services.AddSingleton<
            ISubscriptionInvoiceHistoryRepository,
            SubscriptionInvoiceHistoryRepository>();
        services.AddSingleton<ISubscriptionDiscountRepository, SubscriptionDiscountRepository>();
        services.AddSingleton<
            ICampaignRedemptionRepository,
            CampaignRedemptionRepository>();
        services.AddSingleton<
            IMailDeliveryReportRepository,
            MailDeliveryReportRepository>();
        services.AddSingleton<
            ISubscriptionBillingProfileRepository,
            SubscriptionBillingProfileRepository>();
        services.AddSingleton<
            ISubscriptionFinancialDocumentRepository,
            SubscriptionFinancialDocumentRepository>();
        services.AddSingleton<
            IFinancialDocumentNumberAllocator,
            FinancialDocumentNumberAllocator>();
        services.AddSingleton<
            ISubscriptionMerchantProfileRepository,
            SubscriptionMerchantProfileRepository>();
        services.AddSingleton<
            ISubscriptionDocumentCursorRepository,
            SubscriptionDocumentCursorRepository>();
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

        // Singleton so the mandate is stated once per process rather than once per scope: it is a
        // startup announcement, and a deployment still carrying the retired settings should see one
        // warning about them, not one per request.
        services.AddSingleton<SubscriptionQueueMandate>();

        // Singleton because it is the drainer's own live state, and the loop that writes it lives
        // for the life of the process. Deliberately in-process only: it says nothing to any other
        // process, which is why worker liveness is published to the root database instead — see
        // ISubscriptionQueueWorkerRegistry.
        services.AddSingleton<SubscriptionQueueReadiness>();

        // Singleton for the same reason the queue is: it lives in the root database and needs no
        // ambient tenant. It is the only signal about whether anything is draining that crosses a
        // process boundary, which is what a readiness check in the Api has to read.
        services.AddSingleton<
            ISubscriptionQueueWorkerRegistry, SubscriptionQueueWorkerRegistry>();

        // Scoped, for the same reason SubscriptionRepairAnnouncer below is: it reads tenant-local
        // repositories, and the sweep resolves it fresh inside each tenant's own context.
        services.AddScoped<CampaignRedemptionReconciler>();

        // Scoped, because it reads tenant-local repositories: the sweep establishes a tenant
        // context per pass and resolves one of these inside it.
        services.AddScoped<SubscriptionRepairAnnouncer>();

        // Singleton because a Meter and its instruments are process-wide: created per scope, each
        // would publish its own series and an exporter would see the same counter many times.
        services.AddSingleton<SubscriptionWorkMetrics>();

        // Singleton for the same reason the tenant source is: the queue lives in the root database
        // and needs no ambient tenant, so there is nothing per-request about it. It also holds the
        // index guarantee, which is per-process state worth keeping in one place.
        services.AddSingleton<ISubscriptionWorkQueue, SubscriptionWorkQueue>();
        services.AddSingleton<ISubscriptionWorkScheduler, SubscriptionWorkScheduler>();
        services.AddSingleton<ISubscriptionWorkDispatcher, SubscriptionWorkDispatcher>();

        // Scoped: recovery reads the caller's own context to decide whose work they may act on.
        services.AddScoped<ISubscriptionWorkRecoveryService, SubscriptionWorkRecoveryService>();

        // Scoped, and resolved per work item: a handler runs inside an established tenant context
        // and depends on processors that read one tenant's database.
        services.AddScoped<ISubscriptionWorkHandler, ActivationSettlementWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, ActivationRecoveryWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, SettlementReservationRecoveryWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, RenewalWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, CancellationEffectiveWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, UsagePeriodClosureWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, UsageInvoiceChargeWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, OutboxPublicationWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, FinancialDocumentIssueWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, FinancialDocumentDeliveryWorkHandler>();
        services.AddScoped<ISubscriptionWorkHandler, UsageProjectionRefreshWorkHandler>();

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
        services.AddTransient<IValidator<UpdateDiscountRequest>, UpdateDiscountRequestValidator>();
        services.AddTransient<
            IValidator<UpdateBillingProfileRequest>,
            UpdateBillingProfileRequestValidator>();
        services.AddTransient<
            IValidator<UpdateMerchantProfileRequest>,
            UpdateMerchantProfileRequestValidator>();
        services.AddTransient<
            IValidator<ChangeSubscriptionPlanRequest>,
            ChangeSubscriptionPlanRequestValidator>();
        services.AddTransient<
            IValidator<ChangeQuantityRequest>,
            ChangeQuantityRequestValidator>();
        services.AddTransient<
            IValidator<RecordUsageRequest>,
            RecordUsageRequestValidator>();
        services.AddTransient<
            IValidator<PreviewUsageOverageRequest>,
            PreviewUsageOverageRequestValidator>();

        // Scoped: these read the caller's context, which belongs to one request.
        services.AddScoped<
            ISubscriptionContextResolver,
            SubscriptionContextResolver>();
        services.AddScoped<IPlanCatalogueService, PlanCatalogueService>();
        services.AddScoped<IDiscountCatalogueService, DiscountCatalogueService>();
        services.AddScoped<
            ISubscriptionBillingProfileService,
            SubscriptionBillingProfileService>();
        services.AddScoped<
            ISubscriptionMerchantProfileService,
            SubscriptionMerchantProfileService>();
        services.AddScoped<
            ISubscriptionBillingProfileGuard,
            SubscriptionBillingProfileGuard>();
        services.AddScoped<
            ISubscriptionFinancialDocumentAnnouncer,
            SubscriptionFinancialDocumentAnnouncer>();
        services.AddScoped<
            ISubscriptionFinancialDocumentIssuer,
            SubscriptionFinancialDocumentIssuer>();
        services.AddScoped<
            ISubscriptionFinancialDocumentDeliveryService,
            SubscriptionFinancialDocumentDeliveryService>();
        services.AddScoped<
            ISubscriptionFinancialDocumentHistoryService,
            SubscriptionFinancialDocumentHistoryService>();

        // The two adapters that reach into the platform's PDF and storage modules, and the pieces of
        // those modules they need. Registered here with TryAdd rather than assumed: the PDF module
        // does not register its own engines, and a host that later starts doing so must not end up
        // with two browsers.
        services.TryAddSingleton<PuppeteerSharpEngine>();
        services.TryAddSingleton<PdfStorageHelper>();
        services.AddSingleton<
            IFinancialDocumentPdfRenderer,
            PuppeteerFinancialDocumentPdfRenderer>();
        services.AddSingleton<
            IFinancialDocumentFileStore,
            StorageDriverFinancialDocumentFileStore>();
        // One gate shared by the Worker's startup probe, its periodic re-probe, and the delivery
        // handler that reads it — all three must see the same answer. Registered here rather than
        // only in the Worker because the delivery handler that reads it lives in this project too.
        services.AddSingleton<
            IFinancialDocumentRendererHealth,
            FinancialDocumentRendererHealthGate>();
        services.AddSingleton<
            IFinancialDocumentLogoResolver,
            FinancialDocumentLogoResolver>();
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
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IMeterAllowanceResolver, MeterAllowanceResolver>();
        services.AddScoped<IUsageRecordingService, UsageRecordingService>();
        services.AddScoped<IUsageProjectionPublisher, UsageProjectionPublisher>();
        services.AddScoped<IUsageProjectionReconciler, UsageProjectionReconciler>();
        services.AddScoped<
            ISubscriptionUsageOveragePreviewService,
            SubscriptionUsageOveragePreviewService>();
        services.AddScoped<IUsageThresholdEvaluator, UsageThresholdEvaluator>();
        // Singleton to match the repository it wraps, and because it holds no per-request state.
        services.AddSingleton<IMailDeliveryReporter, MailDeliveryReporter>();
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
            ISubscriptionCancellationEffectiveProcessor,
            SubscriptionCancellationEffectiveProcessor>();
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
