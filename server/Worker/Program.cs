using Blocks.Genesis;
using DomainService.Utilities;
using Payment.DomainService.Commands;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Utility.DomainService.MagicLink.Utilities;
using Utility.DomainService.Messaging;
using Utility.DomainService.PdfGenerator.Utilities;
using Utility.DomainService.TemplateEngine.Utilities;
using SeliseBlocks.ConfigurationDriver;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Worker;
using Worker.Configuration;
using Worker.Consumers.Payment;
using Worker.Consumers.Subscription;
using Subscription.DomainService.Entities;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Subscription.DomainService.Scheduling;

const string _serviceName = "blocks-utilities-worker";

var vaultType =
    ApplicationConfigurations.ResolveVaultType();
var secret =
    await ApplicationConfigurations
        .ConfigureLogAndSecretsAsync(
            _serviceName,
            vaultType);
// Key rings are resolved per tenant and organization on first use, not loaded here: at
// startup the service does not yet know which organizations exist.
var paymentVault = Vault.GetCloudVault(vaultType);

await CreateHostBuilder(args).Build().RunAsync();

IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, builder) =>
        {
            ApplicationConfigurations.ConfigureWorkerEnv(builder, args);

            // Merge the DB-backed "Secrets" document into configuration (same
            // SecretKey as the Api) so KeyPairs values such as RootTenantId,
            // AuthenticationTokenEndpoint and PdfToolPath are read from the DB.
            builder.AddMongoDbConfiguration(options =>
            {
                options.ConnectionString = secret.DatabaseConnectionString;
                options.DatabaseName = secret.RootDatabaseName;
                options.CollectionName = "Secrets";
                options.SecretKey = "blocks-secret-utilities";
            });
        })
        .ConfigureServices((context, services) =>
        {
            services.AddHttpClient();

            services.Configure<VerioSystemSettings>(context.Configuration.GetSection("VerioSystemSettings"));



            services.AddHostedService<PeriodicPingBackgroundService>();




            #region Identifier Service Consumers

            services.AddHttpClient();
            services.AddSingleton<
                IConsumer<ProcessPaymentWorkCommand>,
                PaymentWorkCommandConsumer>();
            services.AddSingleton<
                IConsumer<SubscriptionLifecycleEvent>,
                UsageThresholdReachedConsumer>();
            // Register the test consumer
            services.RegisterUtilityServices();
            services.AddSingleton<IVault>(_ => paymentVault);
            services.RegisterPaymentDomainServices(context.Configuration);
            services.RegisterSubscriptionDomainServices(
                context.Configuration, context.HostingEnvironment);
            services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics
                    // The constant rather than the literal it used to repeat, now that the name is
                    // shared with the activity source below and a drift between them would be two
                    // signals for one thing filed under two names.
                    .AddMeter(SubscriptionWorkMetrics.MeterName)
                    .AddMeter(FinancialDocumentRendererHealthGate.MeterName)
                    // The reconciliation sweep and the backfill run here, so version lag and repair
                    // volume are recorded in this process. Creating the instruments is not enough:
                    // an exporter only observes a meter it has been told to subscribe to.
                    .AddMeter(UsageProjectionMetrics.MeterName)
                    .AddOtlpExporter())
                // Subscribing to the source is what makes StartActivity return an activity at all:
                // one nothing listens to returns null and sets nothing current, exactly as the
                // meters above record nothing until an exporter asks for them.
                //
                // No exporter is named here on purpose. The platform's own tracing registration
                // owns where spans go, and adding a second destination from this composition root
                // would send them somewhere nobody configured. What this line is for is the trace
                // id: the worker serves no request, so until now nothing made an activity current
                // and every line it logged carried an empty one.
                .WithTracing(tracing => tracing
                    .AddSource(SubscriptionWorkActivity.SourceName));
            // First, deliberately: an operator should learn whether the renderer works before
            // anything else in this worker starts moving. It no longer stops the host on failure —
            // see the check's own remarks — only records what it found for
            // FinancialDocumentDeliveryWorkHandler to read.
            services.AddHostedService<FinancialDocumentRendererReadinessCheck>();
            // Re-probes on an interval only while the gate above is unhealthy, so a renderer that
            // recovers reopens document delivery without a restart.
            services.AddHostedService<FinancialDocumentRendererHealthMonitor>();
            services.AddHostedService<
                PaymentReconciliationBackgroundService>();
            services.AddHostedService<
                PaymentWorkSchedulerBackgroundService>();
            services.AddHostedService<
                SubscriptionReconciliationBackgroundService>();
            services.AddHostedService<
                SubscriptionWorkSchedulerBackgroundService>();
            ApplicationConfigurations.ConfigureWorker(services, GetCombinedMessageConfiguration(secret.MessageConnectionString));
            //ApplicationConfigurations.ConfigureWorker(services, IdentifierConstants.GetMessageConfiguration(secret.MessageConnectionString));
            #endregion
        });

static MessageConfiguration GetCombinedMessageConfiguration(string connectionString)
{
   
    var magicLink = MagicLinkConstants.GetMessageConfiguration(connectionString);
    var helper = MessageConfigurationHelper.GetMessageConfiguration(connectionString);
    var pdfGenerator = PdfGeneratorConstants.GetMessageConfiguration(connectionString);
    var templateEngine = TemplateEngineConstants.GetMessageConfiguration(connectionString);

    if (MagicLinkConstants.GetProvider(connectionString) == MagicLinkConstants.RabbitMqProvider)
    {
        return new MessageConfiguration
        {
            RabbitMqConfiguration = new RabbitMqConfiguration
            {
                ConsumerSubscriptions = [
                    //..idp.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                   
                    ..magicLink.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ..helper.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ..pdfGenerator.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ..templateEngine.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ConsumerSubscription.BindToQueue(
                        PaymentConstants.PaymentWorkQueue),
                    ConsumerSubscription.BindToQueueViaExchange(
                        SubscriptionConstants.UsageThresholdEmailQueue,
                        SubscriptionConstants.LifecycleTopic)
                ]
            }
        };
    }

    return new MessageConfiguration
    {
        AzureServiceBusConfiguration = new AzureServiceBusConfiguration
        {
            Queues = [
                //..idp.AzureServiceBusConfiguration?.Queues ?? [],
                ..magicLink.AzureServiceBusConfiguration?.Queues ?? [],
                ..helper.AzureServiceBusConfiguration?.Queues ?? [],
                ..pdfGenerator.AzureServiceBusConfiguration?.Queues ?? [],
                ..templateEngine.AzureServiceBusConfiguration?.Queues ?? [],
                PaymentConstants.PaymentWorkQueue
            ],
            Topics = [
                //..idp.AzureServiceBusConfiguration?.Topics ?? [],
                ..magicLink.AzureServiceBusConfiguration?.Topics ?? [],
                ..helper.AzureServiceBusConfiguration?.Topics ?? [],
                ..pdfGenerator.AzureServiceBusConfiguration?.Topics ?? [],
                ..templateEngine.AzureServiceBusConfiguration?.Topics ?? [],
                PaymentConstants.LifecycleTopic,
                SubscriptionConstants.LifecycleTopic
            ]
        }
    };
}
