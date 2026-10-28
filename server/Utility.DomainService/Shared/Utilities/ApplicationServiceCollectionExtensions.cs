using Blocks.Extension.DependencyInjection;
using Blocks.Secrets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Storage.DomainService.Storage;
using Storage.DomainService.Storage.Validators;
using Microsoft.Extensions.DependencyInjection;
using Utility.DomainService.Sequence.service;
using Utility.DomainService.Geolocation.service;
using Utility.DomainService.TemplateEngine.service;
using Utility.DomainService.Shared.Services;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.PdfIngestion.service;
using Utility.DomainService.PdfSignatureValidation.service;
using Utility.DomainService.MagicLink.Service;
using Utility.DomainService.MagicLink;
using DomainService.Storage;
using Storage.DomainService.Services;

namespace DomainService.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        /// <summary>
        /// Registers application services, repositories, and validators to the service collection.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to which services will be registered.</param>

        public static void RegisterUtilityServices(this IServiceCollection services)
        {
            // Backs StorageHelperBase.CreateHttpClient for every storage helper below. Registered
            // here rather than left to the host so the Api and the Worker cannot disagree about
            // whether storage traffic is pooled; AddHttpClient is idempotent, so a host that also
            // calls it adds nothing.
            services.AddHttpClient(Utility.DomainService.Storage.StorageHelperBase.StorageHttpClientName);

            // Action magic links call whatever URL their creator typed, so the connection is vetted
            // after DNS resolution. No proxy: the connector would then vet the proxy, not the target.
            services.AddHttpClient(Utility.DomainService.MagicLink.Utilities.MagicLinkConstants.ActionHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    ConnectCallback = Utility.DomainService.Shared.Utilities.PublicNetwork.ConnectAsync,
                    UseProxy = false
                });

            // Sequence Services
            services.AddSingleton<ISequenceService, SequenceService>();
            services.AddSingleton<ISequenceRepository, SequenceRepository>();

            // The geolocation key cache expires on a clock, so the clock is injected rather than
            // read statically - a host can replace it before registering this module. TryAdd
            // because Subscription registers the same default.
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);

            // Geolocation Services
            services.AddSingleton<IGeolocationService, GeolocationService>();
            services.AddSingleton<IGeolocationRepository, GeolocationRepository>();

            // Backs the geolocation provider key when GeolocationApiKeySecretId names a secret.
            // Registered here rather than left to the host so the Api and the Worker cannot
            // disagree about whether the managed secret store is available; AddBlocksSecrets uses
            // TryAdd throughout, so a host that also calls it adds nothing.
            services.AddBlocksSecrets();

            // Shared Services
            services.AddSingleton<IHttpHelperServices, HttpHelperServices>();

            // Template Engine Services
            services.AddSingleton<ITemplateEngineService, TemplateEngineService>();
            services.AddSingleton<ITemplateEngineRepository, TemplateEngineRepository>();
            services.AddSingleton<ITemplateEngineNotificationService, TemplateEngineNotificationService>();
            services.AddSingleton<Utility.DomainService.TemplateEngine.service.StorageHelper>();
            services.AddSingleton<Utility.DomainService.TemplateEngine.service.TemplateRenderingService>();
            services.AddSingleton<Utility.DomainService.TemplateEngine.service.MongoQueryHelper>();

            // PDF Generator Services
            services.AddSingleton<IPdfGeneratorService, PdfGeneratorService>();
            services.AddSingleton<ISinglePdfGeneratorService, SinglePdfGeneratorService>();
            services.AddSingleton<IPdfGeneratorRepository, PdfGeneratorRepository>();
            services.AddSingleton<IPdfGeneratorNotificationService, PdfGeneratorNotificationService>();

            // PDF engines and the provider that selects between them by engine number. Registered
            // here rather than in the worker so both hosts resolve the same graph; without these the
            // PDF consumers cannot be constructed at all and every queued PDF message is dropped.
            // TryAdd so the Subscription module's own registration of PuppeteerSharpEngine, which
            // may run either before or after this, does not produce a second browser-owning
            // singleton.
            services.TryAddSingleton<PuppeteerSharpEngine>();
            services.TryAddSingleton<PdfSharpCoreEngine>();
            services.TryAddSingleton<AsposePdfEngine>();
            services.TryAddSingleton<IPdfEngineProvider, PdfEngineProvider>();
            services.TryAddSingleton<PdfStorageHelper>();

            // Document conversion. Not an IPdfEngine: Aspose is the only library here that reads
            // Word formats, so there is nothing to select between.
            services.TryAddSingleton<IDocumentToPdfConverter, AsposeDocumentToPdfConverter>();
            services.TryAddSingleton<IDocumentConversionService, DocumentConversionService>();

            // PDF Ingestion request/status surface. Registered here, for both hosts, because both
            // the Api (accepting requests, serving polls) and the Worker (the consumer that resolves
            // IPdfIngestionRepository too) need it. The tool-execution graph these depend on
            // downstream (qpdf, veraPDF, PDFBox, Ghostscript) is registered separately by
            // RegisterPdfIngestionToolchain, called only from the Worker - the Api must never need
            // those binaries on PATH.
            services.TryAddSingleton<IPdfIngestionRepository, PdfIngestionRepository>();
            services.TryAddSingleton<IPdfIngestionService, PdfIngestionService>();

            // PDF signature validation request/status surface, for both hosts for the same reason:
            // the Api accepts and answers polls, the Worker's consumer writes the same records.
            services.TryAddSingleton<IPdfSignatureValidationRepository, PdfSignatureValidationRepository>();
            services.TryAddSingleton<IPdfSignatureValidationService, PdfSignatureValidationService>();

            // Magic Link Services
            services.AddSingleton<IMagicLinkService, MagicLinkService>();
            services.AddSingleton<IMagicLinkRepository, MagicLinkRepository>();
            services.AddSingleton<IMagicLinkNotificationService, MagicLinkNotificationService>();
            services.AddSingleton<MagicLinkActionExecutor>();

            // Magic Link Validators
            services.AddTransient<IValidator<CreateMagicLinkRequest>, CreateMagicLinkRequestValidator>();
            services.AddTransient<IValidator<RemoveMagicLinksRequest>, RemoveMagicLinksRequestValidator>();
            services.AddTransient<IValidator<InvokeMagicLinkRequest>, InvokeMagicLinkRequestValidator>();

            // PDF Generator Validators - TODO: Re-implement validators
            // services.AddTransient<IValidator<MergePdfsRequest>, MergePdfsRequestValidator>();
            // services.AddTransient<IValidator<ExtractTextFromPdfsRequest>, ExtractTextFromPdfsRequestValidator>();
            // services.AddTransient<IValidator<CreatePdfsFromHtmlRequest>, CreatePdfsFromHtmlRequestValidator>();
            // services.AddTransient<IValidator<CreatePdfsFromHtmlUsingTERequest>, CreatePdfsFromHtmlUsingTERequestValidator>();
            // services.AddTransient<IValidator<CreatePdfsFromHtmlUsingTEBulkRequest>, CreatePdfsFromHtmlUsingTEBulkRequestValidator>();
            // services.AddTransient<IValidator<FixPdfsRequest>, FixPdfsRequestValidator>();
            // services.AddTransient<IValidator<StampImageToPdfRequest>, StampImageToPdfRequestValidator>();
            // services.AddTransient<IValidator<StampTextToPdfRequest>, StampTextToPdfRequestValidator>();
            // services.AddTransient<IValidator<StampIntoPdfRequest>, StampIntoPdfRequestValidator>();

            // Register Storage Driver Services (required for StorageHelper)
            services.AddTransient<AwsS3CompatibleStorageService>();
            services.RegisterBlocksStorageServices();
            // Not registered by the driver, but needed to create the directories uploads go into:
            // driver 4.1.2 refuses an upload whose parent directory does not exist.
            services.TryAddSingleton<IFileDirectoryManagementService, FileDirectoryManagementService>();
            services.TryAddSingleton<Utility.DomainService.Storage.StorageDirectoryResolver>();
            services.AddTransient<IValidator<UpdateFileRequest>, UpdateFileRequestValidator>();

            // Workflow Services (registered via extension method)
            services.AddSingleton<IClientCredentialTokenService, ClientCredentialTokenService>();

            RegisterDriveServices(services);
        }

        /// <summary>
        /// OneDrive / SharePoint integration (spec 004). Options bind from the <c>Drive</c>
        /// section; the two secret values are read from Blocks Secrets by the ids named there,
        /// through the same <c>ISecretService</c> the geolocation key uses.
        /// </summary>
        private static void RegisterDriveServices(IServiceCollection services)
        {
            services.AddOptions<Utility.DomainService.Drive.DriveOptions>()
                .BindConfiguration(Utility.DomainService.Drive.DriveOptions.SectionName);

            // Separate clients so a policy on Graph traffic never touches the token endpoint.
            services.AddHttpClient(Utility.DomainService.Drive.Service.GraphClient.HttpClientName);
            services.AddHttpClient(Utility.DomainService.Drive.Service.EntraOAuthClient.HttpClientName);

            services.TryAddSingleton<Utility.DomainService.Drive.DriveMetrics>();
            services.AddSingleton<Utility.DomainService.Drive.Service.IDriveSecrets, Utility.DomainService.Drive.Service.DriveSecrets>();
            services.AddSingleton<Utility.DomainService.Drive.Service.DriveTokenProtector>();
            services.AddSingleton<Utility.DomainService.Drive.Service.IDriveRepository, Utility.DomainService.Drive.Service.DriveRepository>();
            services.AddSingleton<Utility.DomainService.Drive.Service.IEntraOAuthClient, Utility.DomainService.Drive.Service.EntraOAuthClient>();
            services.AddSingleton<Utility.DomainService.Drive.Service.IGraphClient, Utility.DomainService.Drive.Service.GraphClient>();
            services.AddSingleton<Utility.DomainService.Drive.Service.IDriveStorageBridge, Utility.DomainService.Drive.Service.DriveStorageBridge>();
            services.AddSingleton<Utility.DomainService.Drive.Service.IDriveConnectionService, Utility.DomainService.Drive.Service.DriveConnectionService>();
            services.AddSingleton<Utility.DomainService.Drive.Service.IDriveService, Utility.DomainService.Drive.Service.DriveService>();

            services.AddTransient<IValidator<Utility.DomainService.Drive.Models.StartConnectionRequest>, Utility.DomainService.Drive.Validators.StartConnectionRequestValidator>();
            services.AddTransient<IValidator<Utility.DomainService.Drive.Models.ListItemsRequest>, Utility.DomainService.Drive.Validators.ListItemsRequestValidator>();
            services.AddTransient<IValidator<Utility.DomainService.Drive.Models.CreateFolderRequest>, Utility.DomainService.Drive.Validators.CreateFolderRequestValidator>();
            services.AddTransient<IValidator<Utility.DomainService.Drive.Models.ImportFileRequest>, Utility.DomainService.Drive.Validators.ImportFileRequestValidator>();
            services.AddTransient<IValidator<Utility.DomainService.Drive.Models.UploadFilesRequest>, Utility.DomainService.Drive.Validators.UploadFilesRequestValidator>();
        }
    }
}


