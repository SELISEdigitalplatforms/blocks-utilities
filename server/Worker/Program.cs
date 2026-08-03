using Blocks.Genesis;
using DomainService.Utilities;
using Mail.DomainService.Dtos;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Utilities;
using Mail.DomainService.Utilities;
using Mail.Worker.Consumers;
using Payment.DomainService.Commands;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Utility.DomainService.MagicLink.Utilities;
using Utility.DomainService.Messaging;
using Utility.DomainService.PdfGenerator.Utilities;
using Utility.DomainService.TemplateEngine.Utilities;
using SeliseBlocks.ConfigurationDriver;
using Worker;
using Worker.Configuration;
using Worker.Consumers.Payment;

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
            services.AddSingleton<IConsumer<SendEmailEvent>, SendEmailConsumer>();
            services.AddSingleton<IConsumer<SendMail>, SendConsumer>();
            services.AddSingleton<
                IConsumer<ProcessPaymentWorkCommand>,
                PaymentWorkCommandConsumer>();
            // Register the test consumer
            services.AddSingleton<ISendMailService, SendMailService>();
            services.AddSingleton<SmtpClientProvider>();
            services.AddSingleton<MicrosoftSmtpClient>();
            services.AddSingleton<MailKitSmtpClient>();

            services.RegisterAllMailApplicationServices();
            services.RegisterAllNotificationApplicationServices();
            services.RegisterUtilityServices();
            services.AddSingleton<IVault>(_ => paymentVault);
            services.RegisterPaymentDomainServices(context.Configuration);
            services.AddHostedService<
                PaymentReconciliationBackgroundService>();
            ApplicationConfigurations.ConfigureWorker(services, GetCombinedMessageConfiguration(secret.MessageConnectionString));
            //ApplicationConfigurations.ConfigureWorker(services, IdentifierConstants.GetMessageConfiguration(secret.MessageConnectionString));
            #endregion
        });

static MessageConfiguration GetCombinedMessageConfiguration(string connectionString)
{
    var communication = CommunicationConstants.GetMessageConfiguration(connectionString);
    var magicLink = MagicLinkConstants.GetMessageConfiguration(connectionString);
    var helper = MessageConfigurationHelper.GetMessageConfiguration(connectionString);
    var pdfGenerator = PdfGeneratorConstants.GetMessageConfiguration(connectionString);
    var templateEngine = TemplateEngineConstants.GetMessageConfiguration(connectionString);

    if (communication.RabbitMqConfiguration != null)
    {
        return new MessageConfiguration
        {
            RabbitMqConfiguration = new RabbitMqConfiguration
            {
                ConsumerSubscriptions = [
                    //..idp.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ..communication.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ..magicLink.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ..helper.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ..pdfGenerator.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ..templateEngine.RabbitMqConfiguration?.ConsumerSubscriptions ?? [],
                    ConsumerSubscription.BindToQueue(
                        PaymentConstants.PaymentWorkQueue)
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
                ..communication.AzureServiceBusConfiguration?.Queues ?? [],
                ..magicLink.AzureServiceBusConfiguration?.Queues ?? [],
                ..helper.AzureServiceBusConfiguration?.Queues ?? [],
                ..pdfGenerator.AzureServiceBusConfiguration?.Queues ?? [],
                ..templateEngine.AzureServiceBusConfiguration?.Queues ?? [],
                PaymentConstants.PaymentWorkQueue
            ],
            Topics = [
                //..idp.AzureServiceBusConfiguration?.Topics ?? [],
                ..communication.AzureServiceBusConfiguration?.Topics ?? [],
                ..magicLink.AzureServiceBusConfiguration?.Topics ?? [],
                ..helper.AzureServiceBusConfiguration?.Topics ?? [],
                ..pdfGenerator.AzureServiceBusConfiguration?.Topics ?? [],
                ..templateEngine.AzureServiceBusConfiguration?.Topics ?? [],
                PaymentConstants.LifecycleTopic
            ]
        }
    };
}
