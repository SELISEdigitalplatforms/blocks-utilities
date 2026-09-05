using Blocks.Extension.DependencyInjection;
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
using Utility.DomainService.MagicLink.Service;
using Utility.DomainService.MagicLink;
using DomainService.Storage;
using Storage.DomainService.Shared.Services;

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

            // Sequence Services
            services.AddSingleton<ISequenceService, SequenceService>();
            services.AddSingleton<ISequenceRepository, SequenceRepository>();

            // Geolocation Services
            services.AddSingleton<IGeolocationService, GeolocationService>();
            services.AddSingleton<IGeolocationRepository, GeolocationRepository>();

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
            services.TryAddSingleton<WkHtmlToPdfEngine>();
            services.TryAddSingleton<IPdfEngineProvider, PdfEngineProvider>();
            services.TryAddSingleton<PdfStorageHelper>();

            // Document conversion. Not an IPdfEngine: Aspose is the only library here that reads
            // Word formats, so there is nothing to select between.
            services.TryAddSingleton<IDocumentToPdfConverter, AsposeDocumentToPdfConverter>();
            services.TryAddSingleton<IDocumentConversionService, DocumentConversionService>();

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
            services.AddSingleton<DmsArtifactBuilderFactory>();
            services.AddTransient<AwsS3CompatibleStorageService>();
            services.AddSingleton<FileArtifactBuilder>();
            services.AddSingleton<FolderArtifactBuilder>();
            services.RegisterBlocksStorageServices();
            services.AddTransient<IValidator<UpdateFileRequest>, UpdateFileRequestValidator>();

            // Workflow Services (registered via extension method)
            services.AddSingleton<IClientCredentialTokenService, ClientCredentialTokenService>();
        }
    }
}


