using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Utility.DomainService.PdfGenerator.Tooling.Ghostscript;
using Utility.DomainService.PdfGenerator.Tooling.Inspection;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Options;
using Utility.DomainService.PdfGenerator.Tooling.PdfBox;
using Utility.DomainService.PdfGenerator.Tooling.ProcessExecution;
using Utility.DomainService.PdfGenerator.Tooling.Qpdf;
using Utility.DomainService.PdfGenerator.Tooling.VeraPdf;
using Utility.DomainService.PdfGenerator.Tooling.Workspace;

namespace Utility.DomainService.PdfGenerator.Tooling
{
    /// <summary>
    /// Registers the external-tool wrappers the PDF ingestion pipeline drives (qpdf, veraPDF,
    /// PDFBox, Ghostscript) plus the pipeline and inspector that sit on top of them.
    /// </summary>
    /// <remarks>
    /// Called only from <c>Worker/Program.cs</c>. The Api host answers requests and serves polling;
    /// it must never resolve a graph that expects <c>qpdf</c>, Java or Ghostscript on <c>PATH</c>,
    /// which is exactly what registering these singletons there would set it up to need. The
    /// request/status surface (<c>IPdfIngestionRepository</c>, <c>IPdfIngestionService</c>) is
    /// registered separately in <c>RegisterUtilityServices</c>, which both hosts call, because both
    /// need to accept requests and answer status polls.
    /// </remarks>
    public static class PdfIngestionToolchainServiceCollectionExtensions
    {
        public static IServiceCollection RegisterPdfIngestionToolchain(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.Configure<PdfToolingOptions>(configuration.GetSection(PdfToolingOptions.SectionName));

            services.AddSingleton<IProcessRunner, ProcessRunner>();

            // Must be singleton: a scoped or transient lifetime would give each resolution its own
            // semaphore, so nothing would actually be limited across concurrent ingestions.
            services.AddSingleton<IExternalProcessConcurrencyLimiter, ExternalProcessConcurrencyLimiter>();

            services.AddSingleton<IPdfWorkspaceService, PdfWorkspaceService>();

            services.AddSingleton<QpdfCommandFactory>();
            services.AddSingleton<IPdfNormalizer, QpdfPdfNormalizer>();

            services.AddSingleton<VeraPdfCommandFactory>();
            services.AddSingleton<VeraPdfXmlParser>();
            services.AddSingleton<IPdfAValidator, VeraPdfAValidator>();

            services.AddSingleton<PdfBoxCommandFactory>();
            services.AddSingleton<PdfBoxGeometryReportParser>();
            services.AddSingleton<IPdfGeometryNormalizer, PdfBoxGeometryNormalizer>();
            services.AddSingleton<IPdfFlattener, PdfBoxPdfFlattener>();
            services.AddSingleton<IStandardPdfConverter, PdfBoxStandardPdfConverter>();

            services.AddSingleton<GhostscriptCommandFactory>();
            services.AddSingleton<IPdfAConverter, GhostscriptPdfAConverter>();

            services.AddSingleton<IPdfInspector, PdfInspector>();
            services.AddSingleton<IPdfIngestionPipeline, PdfIngestionPipeline>();

            return services;
        }
    }
}
