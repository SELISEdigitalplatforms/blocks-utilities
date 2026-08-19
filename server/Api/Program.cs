using Blocks.Genesis;
using Api.Middleware;
using Api.OpenApi;
using BlocksTemplate.Api;
using Api.Utilities;
using DomainService.Utilities;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using SeliseBlocks.ConfigurationDriver;
using Scalar.AspNetCore;
using Subscription.DomainService.Utilities;
using Utility.DomainService.MagicLink.Utilities;
using Utility.DomainService.Messaging;
using Utility.DomainService.PdfGenerator.Utilities;
using Utility.DomainService.TemplateEngine.Utilities;

var serviceName = "blocks-utilities";
var vaultType = ApplicationConfigurations.ResolveVaultType();
var secret =
    await ApplicationConfigurations
        .ConfigureLogAndSecretsAsync(
            serviceName,
            vaultType);
// Key rings are resolved per tenant and organization on first use, not loaded here: at
// startup the service does not yet know which organizations exist.
var paymentVault = Vault.GetCloudVault(vaultType);
var builder = WebApplication.CreateBuilder(args);

ApplicationConfigurations.ConfigureApiEnv(builder, args);

// Load frontend runtime settings (and other config) from the DB. The Mongo
// "Secrets" document keyed by "blocks-secret-utilities" is merged into
// IConfiguration, exposing the "FrontendRuntime" section consumed below.
builder.Configuration.AddMongoDbConfiguration(options =>
{
    options.ConnectionString = secret.DatabaseConnectionString;
    options.DatabaseName     = secret.RootDatabaseName;
    options.CollectionName   = "Secrets";
    options.SecretKey        = "blocks-secret-utilities";
});

ApplicationConfigurations.ConfigureServices(builder.Services, GetCombinedMessageConfiguration(secret.MessageConnectionString));

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 15 * 1024 * 1024; // 15 MB
});

var services = builder.Services;

services.AddHealthChecks();
services.AddOpenApi(options =>
{
    options.AddDocumentTransformer(
        new ApiSecuritySchemeDocumentTransformer());
    options.AddOperationTransformer(
        new AuthorizedOperationSecurityTransformer());
});
services.AddSingleton<
    IWebhookRequestBodyReader,
    WebhookRequestBodyReader>();

ApplicationConfigurations.ConfigureApi(
    services,
    serviceName,
    apiRoutePrefix: "off");

builder.Services.Configure<MvcOptions>(options =>
{
    options.Conventions.Add(new GlobalApiRoutePrefixConvention("api"));
});

var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(wwwrootPath);

ApplyFrontendRuntimeSettings(builder.Configuration, wwwrootPath);

services.AddSingleton<IVault>(_ => paymentVault);
services.RegisterPaymentDomainServices(builder.Configuration);
services.RegisterSubscriptionDomainServices(builder.Configuration);
services.RegisterUtilityServices();

var app = builder.Build();

var documentationPath = Path.Combine(app.Environment.ContentRootPath, "Documentation");
Directory.CreateDirectory(documentationPath);
var documentationFiles = new PhysicalFileProvider(documentationPath);

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = documentationFiles,
    RequestPath = "/docs"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = documentationFiles,
    RequestPath = "/docs",
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();

var indexHtml = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
if (File.Exists(indexHtml))
{
    app.MapFallbackToFile("/index.html");
    // x-blocks-key cookie
    // check if domain match 
    // get google captch key BLOCKS_GOOGLE_SITE_KEY
    // Base Url 
    // Construct URL 


}

// Before the endpoint pipeline, so every log line a request produces is written inside its
// correlation scope rather than only the ones the controllers pass the id to by hand.
app.UsePaymentCorrelation();

ApplicationConfigurations.ConfigureMiddleware(app);

if (builder.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi().AllowAnonymous();
    app.MapOpenApi("/swagger/{documentName}/swagger.json")
        .AllowAnonymous();

    app.MapScalarApiReference(options =>
        options
            .WithTitle("Blocks Utilities API")
            .DisableAgent()).AllowAnonymous();

    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "Blocks Utilities API";
        options.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "Blocks Utilities API");
        options.DisplayRequestDuration();
        options.EnablePersistAuthorization();
    });
}

//app.MapHub<NotificationHub>("/notificationHub").WithDisplayName("Controller/notificationHub");
await app.RunAsync();

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
                ..magicLink.AzureServiceBusConfiguration?.Queues ?? [],
                ..helper.AzureServiceBusConfiguration?.Queues ?? [],
                ..pdfGenerator.AzureServiceBusConfiguration?.Queues ?? [],
                ..templateEngine.AzureServiceBusConfiguration?.Queues ?? []
            ],
            Topics = [
                //..idp.AzureServiceBusConfiguration?.Topics ?? [],
                ..magicLink.AzureServiceBusConfiguration?.Topics ?? [],
                ..helper.AzureServiceBusConfiguration?.Topics ?? [],
                ..pdfGenerator.AzureServiceBusConfiguration?.Topics ?? [],
                ..templateEngine.AzureServiceBusConfiguration?.Topics ?? []
            ]
        }
    };
}

static void ApplyFrontendRuntimeSettings(IConfiguration configuration, string webRootPath)
{
    // Read frontend runtime values from the DB-backed "FrontendRuntime" config
    // section (populated by AddMongoDbConfiguration from the "Secrets" collection).
    // Standard .NET config layering still applies, so env vars named
    // "FrontendRuntime__BLOCKS_*" override individual keys at deploy time.
    var section = configuration.GetSection("FrontendRuntime");
    var replacements = new Dictionary<string, string?>
    {
        ["__BLOCKS_PUBLIC_API_BASE_URL__"] = section["BLOCKS_PUBLIC_API_BASE_URL"],
        ["__BLOCKS_IAM_BASE_URL__"] = section["BLOCKS_IAM_BASE_URL"],
        ["__BLOCKS_X_BLOCKS_KEY__"] = section["BLOCKS_X_BLOCKS_KEY"],
        ["__BLOCKS_GOOGLE_SITE_KEY__"] = section["BLOCKS_GOOGLE_SITE_KEY"],
        ["__BLOCKS_CONSTRUCT_URL__"] = section["BLOCKS_CONSTRUCT_URL"],
        ["__BLOCKS_GITHUB_SSO_CLIENT_ID__"] = section["BLOCKS_GITHUB_SSO_CLIENT_ID"],
        ["__BLOCKS_OIDC_CLIENT_ID__"] = section["BLOCKS_OIDC_CLIENT_ID"],
        ["__BLOCKS_BASE_DOMAIN__"] = section["BLOCKS_BASE_DOMAIN"],
        ["__BLOCKS_IAM_CALLBACK_URL__"] = section["BLOCKS_IAM_CALLBACK_URL"],
        ["__BLOCKS_IAM_CLIENT_ID__"] = section["BLOCKS_IAM_CLIENT_ID"],
        ["__BLOCKS_LOCALIZATION_BASE_URL__"] = section["BLOCKS_LOCALIZATION_BASE_URL"],
        ["__BLOCKS_LOCALIZATION_CALLBACK_URL__"] = section["BLOCKS_LOCALIZATION_CALLBACK_URL"],
        ["__BLOCKS_LOCALIZATION_CLIENT_ID__"] = section["BLOCKS_LOCALIZATION_CLIENT_ID"],
        ["__BLOCKS_AGENTS_BASE_URL__"] = section["BLOCKS_AGENTS_BASE_URL"],
        ["__BLOCKS_AGENTS_CALLBACK_URL__"] = section["BLOCKS_AGENTS_CALLBACK_URL"],
        ["__BLOCKS_AGENTS_CLIENT_ID__"] = section["BLOCKS_AGENTS_CLIENT_ID"],
        ["__BLOCKS_DATA_BASE_URL__"] = section["BLOCKS_DATA_BASE_URL"],
        ["__BLOCKS_DATA_CALLBACK_URL__"] = section["BLOCKS_DATA_CALLBACK_URL"],
        ["__BLOCKS_DATA_CLIENT_ID__"] = section["BLOCKS_DATA_CLIENT_ID"],
        ["__BLOCKS_OS_BASE_URL__"] = section["BLOCKS_OS_BASE_URL"],
        ["__BLOCKS_OS_CALLBACK_URL__"] = section["BLOCKS_OS_CALLBACK_URL"],
        ["__BLOCKS_OS_CLIENT_ID__"] = section["BLOCKS_OS_CLIENT_ID"],
        ["__BLOCKS_UTILITIES_BASE_URL__"] = section["BLOCKS_UTILITIES_BASE_URL"],
        ["__BLOCKS_UTILITIES_CALLBACK_URL__"] = section["BLOCKS_UTILITIES_CALLBACK_URL"],
        ["__BLOCKS_UTILITIES_CLIENT_ID__"] = section["BLOCKS_UTILITIES_CLIENT_ID"],
        ["__BLOCKS_LOGIC_BASE_URL__"] = section["BLOCKS_LOGIC_BASE_URL"],
        ["__BLOCKS_LOGIC_CALLBACK_URL__"] = section["BLOCKS_LOGIC_CALLBACK_URL"],
        ["__BLOCKS_LOGIC_CLIENT_ID__"] = section["BLOCKS_LOGIC_CLIENT_ID"],
        ["__BLOCKS_MONITOR_BASE_URL__"] = section["BLOCKS_MONITOR_BASE_URL"],
        ["__BLOCKS_MONITOR_CALLBACK_URL__"] = section["BLOCKS_MONITOR_CALLBACK_URL"],
        ["__BLOCKS_MONITOR_CLIENT_ID__"] = section["BLOCKS_MONITOR_CLIENT_ID"],
        ["__BLOCKS_RELEASE_BASE_URL__"] = section["BLOCKS_RELEASE_BASE_URL"],
        ["__BLOCKS_RELEASE_CALLBACK_URL__"] = section["BLOCKS_RELEASE_CALLBACK_URL"],
        ["__BLOCKS_RELEASE_CLIENT_ID__"] = section["BLOCKS_RELEASE_CLIENT_ID"],
        ["__BLOCKS_STUDIO_BASE_URL__"] = section["BLOCKS_STUDIO_BASE_URL"],
        ["__BLOCKS_STUDIO_CALLBACK_URL__"] = section["BLOCKS_STUDIO_CALLBACK_URL"],
        ["__BLOCKS_ALLOWED_SERVICES__"] = section["BLOCKS_ALLOWED_SERVICES"],
    };

    var files = Directory.EnumerateFiles(webRootPath, "*", SearchOption.AllDirectories)
        .Where(path =>
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".css", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
        });

    foreach (var filePath in files)
    {
        var content = File.ReadAllText(filePath);
        var updated = content;

        foreach (var (token, value) in replacements)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                updated = updated.Replace(token, value, StringComparison.Ordinal);
            }
        }

        if (!ReferenceEquals(content, updated) && !content.Equals(updated, StringComparison.Ordinal))
        {
            File.WriteAllText(filePath, updated);
        }
    }
}
