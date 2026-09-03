using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.Events;

namespace Worker.Consumers.PdfGenerator
{
    /// <summary>
    /// Registers the PDF generator message consumers with the worker's container.
    /// </summary>
    /// <remarks>
    /// The worker subscribes to every PDF queue through
    /// <c>PdfGeneratorConstants.GetMessageConfiguration</c>, but subscribing only creates the
    /// listener — the dispatcher still has to resolve an <c>IConsumer&lt;TEvent&gt;</c> to hand the
    /// message to. Until this ran, none of these types were registered, so every PDF request the API
    /// accepted was queued and then dropped while the caller was told the operation had been
    /// accepted. Adding a consumer without a line here reintroduces exactly that failure, which is
    /// why they are collected in one place rather than scattered through Program.cs.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    public static class PdfGeneratorConsumerRegistration
    {
        public static IServiceCollection RegisterPdfGeneratorConsumers(this IServiceCollection services)
        {
            services.AddSingleton<IConsumer<MergePdfsEvent>, MergePdfsConsumer>();
            services.AddSingleton<IConsumer<CreatePdfsFromHtmlEvent>, CreatePdfsFromHtmlConsumer>();
            services.AddSingleton<IConsumer<ExtractTextFromPdfsEvent>, ExtractTextFromPdfsConsumer>();
            services.AddSingleton<IConsumer<CreatePdfsFromHtmlUsingTEEvent>, CreatePdfsFromHtmlUsingTEConsumer>();
            services.AddSingleton<IConsumer<CreatePdfsFromHtmlUsingTEBulkEvent>, CreatePdfsFromHtmlUsingTEBulkConsumer>();
            services.AddSingleton<IConsumer<FixPdfsEvent>, FixPdfsConsumer>();
            services.AddSingleton<IConsumer<StampImageToPdfEvent>, StampImageToPdfConsumer>();
            services.AddSingleton<IConsumer<StampTextToPdfEvent>, StampTextToPdfConsumer>();
            services.AddSingleton<IConsumer<StampIntoPdfEvent>, StampIntoPdfConsumer>();
            services.AddSingleton<IConsumer<ConvertDocumentsToPdfEvent>, ConvertDocumentsToPdfConsumer>();

            return services;
        }
    }
}
