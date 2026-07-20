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
    ApplicationConfigurations.ResolveVaultType(
        VaultType.Azure);
var secret =
    await ApplicationConfigurations
        .ConfigureLogAndSecretsAsync(
            _serviceName,
            vaultType);
var paymentVault = Vault.GetCloudVault(vaultType);
var providerTokenEncryptionKeyRing =
    await ProviderTokenEncryptionKeyRingVaultLoader
        .LoadAsync(paymentVault);

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

            //services.AddSingleton<IConsumer<RefreshTokenEvent>, RefreshTokenWorkerService>();
            //services.AddSingleton<IConsumer<UserAuthenticationTimelineEvent>, UserAuthenticationTimelineWorkerService>();
            //services.AddSingleton<IConsumer<MfaActionEvent>, UpdateMfaConfigurationService>();

            //services.AddSingleton<IConsumer<ResourceMutationEvent>, ResourceMutationConsumer>();
            //services.AddSingleton<IConsumer<ResourceSetToPermissionMutationEvent>, ResourceSetToPermissionMutationConsumer>();
            //services.AddSingleton<IConsumer<UserMutationEvent>, UserMutationConsumer>();
            //services.AddSingleton<IConsumer<AccountActivityEvent>, AccountActivityWorkerService>();
            //services.AddSingleton<IConsumer<CreateUserByEmailEvent>, CreateUserByEmailConsumer>();
            //services.AddSingleton<IConsumer<CreateUserRequest>, CreateUserConsumer>();
            //services.AddSingleton<IConsumer<CreateUserViaSsoEvent>, CreateUserViaSsoConsumer>();
            //services.AddSingleton<IConsumer<UserStatusChangedEvent>, UserStatusChangedConsumer>();

            services.AddHostedService<PeriodicPingBackgroundService>();

            //services.RegisterAllServices();



            #region Identifier Service Consumers
            //services.AddApplicationServices();
            //services.AddSingleton<IConsumer<Tenant>, ConfigureProjectConsumer>();
            //services.AddSingleton<IConsumer<DisableDomainBindingRequest>, DisableDomainBindingConsumer>();
            //services.AddSingleton<IConsumer<RestoreProjectRequest>, RestoreProjectConsumer>();
            //services.AddSingleton<IConsumer<CreateUserByEmailPostEvent_Identifier>, CreateUserByEmailPostConsumer>();
            //services.AddSingleton<IConsumer<ConfigureDomainRequest>, DomainConfigureConsumer>();
            //services.AddSingleton<IConsumer<MigrationCompletionEvent>, MigrationCompletionConsumer>();
            //services.AddSingleton<IConsumer<EnvironmentDataMigrationEvent>, EnvironmentDataMigrationEventConsumer>();
            //services.AddSingleton<IConsumer<PublishScheduleCommand>, DataCleanupConsumer>();
            //services.AddSingleton<IConsumer<UpdateResourceUsageCommand_Identifier>, UpdateResourceUsageConsumer>();

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
            services.AddSingleton<
                IProviderTokenEncryptionKeyRing>(
                _ => providerTokenEncryptionKeyRing);
            services.RegisterPaymentDomainServices(context.Configuration);
            services.AddHostedService<
                PaymentReconciliationBackgroundService>();
            ApplicationConfigurations.ConfigureWorker(services, GetCombinedMessageConfiguration(secret.MessageConnectionString));
            //ApplicationConfigurations.ConfigureWorker(services, IdentifierConstants.GetMessageConfiguration(secret.MessageConnectionString));
            #endregion
        });

static MessageConfiguration GetCombinedMessageConfiguration(string connectionString)
{
    //var idp = IdpConstants.GetMessageConfiguration(connectionString);
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
