using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfIngestion.Events;

namespace Worker.Consumers.PdfIngestion
{
    /// <summary>
    /// Registers the PDF ingestion message consumer with the worker's container.
    /// </summary>
    /// <remarks>
    /// The worker subscribes to the ingestion queue through
    /// <c>PdfIngestionConstants.GetMessageConfiguration</c>, but subscribing only creates the
    /// listener - the dispatcher still has to resolve an <c>IConsumer&lt;TEvent&gt;</c> to hand the
    /// message to. Omitting this registration means every ingestion message is accepted onto the
    /// queue and then silently dropped, exactly the failure
    /// <c>PdfGeneratorConsumerRegistration</c> exists to prevent for its own events.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    public static class PdfIngestionConsumerRegistration
    {
        public static IServiceCollection RegisterPdfIngestionConsumers(this IServiceCollection services)
        {
            services.AddSingleton<IConsumer<IngestPdfEvent>, IngestPdfConsumer>();

            return services;
        }
    }
}
