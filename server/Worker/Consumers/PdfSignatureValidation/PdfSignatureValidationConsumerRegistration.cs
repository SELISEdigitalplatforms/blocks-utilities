using Blocks.Genesis;
using Utility.DomainService.PdfSignatureValidation.Events;

namespace Worker.Consumers.PdfSignatureValidation
{
    /// <summary>
    /// Registers the PDF signature validation message consumer with the worker's container.
    /// </summary>
    /// <remarks>
    /// Subscribing to the queue (<c>PdfSignatureValidationConstants.GetMessageConfiguration</c>) only
    /// creates the listener; the dispatcher still has to resolve an <c>IConsumer&lt;TEvent&gt;</c> to
    /// hand each message to, or every message is accepted and then silently dropped - the failure
    /// <c>PdfIngestionConsumerRegistration</c> exists to prevent for its own events.
    /// </remarks>
    public static class PdfSignatureValidationConsumerRegistration
    {
        public static IServiceCollection RegisterPdfSignatureValidationConsumers(this IServiceCollection services)
        {
            services.AddSingleton<IConsumer<ValidatePdfSignaturesEvent>, ValidatePdfSignaturesConsumer>();

            return services;
        }
    }
}
