using Blocks.Extension.DependencyInjection;
using FluentValidation;
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


