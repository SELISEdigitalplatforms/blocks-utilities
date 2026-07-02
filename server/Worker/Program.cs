using Blocks.Genesis;
using DomainService.Utilities;
using Mail.DomainService.Dtos;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Utilities;
using Mail.DomainService.Utilities;
using Mail.Worker.Consumers;
using Utility.DomainService.MagicLink.Utilities;
using Utility.DomainService.Messaging;
using Utility.DomainService.PdfGenerator.Utilities;
using Utility.DomainService.TemplateEngine.Utilities;
using SeliseBlocks.ConfigurationDriver;
using Worker;
using Worker.Configuration;

const string _serviceName = "blocks-utilities-worker";

//var vaultType = ResolveVaultType();
//Console.WriteLine($"Using Genesis vault type: {vaultType}");
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(_serviceName, VaultType.Azure);

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
                 options.DatabaseName     = secret.RootDatabaseName;
                 options.CollectionName   = "Secrets";
                 options.SecretKey        = "blocks-secret-utilities";
             });
        })
        .ConfigureServices((services) =>
        {
            services.AddHttpClient();

            services.Configure<VerioSystemSettings>(services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection("VerioSystemSettings"));

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
            // Register the test consumer
            services.AddSingleton<ISendMailService, SendMailService>();
            services.AddSingleton<SmtpClientProvider>();
            services.AddSingleton<MicrosoftSmtpClient>();
            services.AddSingleton<MailKitSmtpClient>();

            services.RegisterAllMailApplicationServices();
            services.RegisterAllNotificationApplicationServices();
            services.RegisterUtilityServices();
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
                    ..templateEngine.RabbitMqConfiguration?.ConsumerSubscriptions ?? []
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
                ..templateEngine.AzureServiceBusConfiguration?.Queues ?? []
            ],
            Topics = [
                //..idp.AzureServiceBusConfiguration?.Topics ?? [],
                ..communication.AzureServiceBusConfiguration?.Topics ?? [],
                ..magicLink.AzureServiceBusConfiguration?.Topics ?? [],
                ..helper.AzureServiceBusConfiguration?.Topics ?? [],
                ..pdfGenerator.AzureServiceBusConfiguration?.Topics ?? [],
                ..templateEngine.AzureServiceBusConfiguration?.Topics ?? []
            ]
        }
    };
}

static VaultType ResolveVaultType()
{
    var configuredVaultType = Environment.GetEnvironmentVariable("BLOCKS_VAULT_TYPE");
    if (!string.IsNullOrWhiteSpace(configuredVaultType) &&
        Enum.TryParse<VaultType>(configuredVaultType, true, out var parsedVaultType))
    {
        return parsedVaultType;
    }

    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                      Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

    return string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
        ? VaultType.OnPrem
        : VaultType.Azure;
}
