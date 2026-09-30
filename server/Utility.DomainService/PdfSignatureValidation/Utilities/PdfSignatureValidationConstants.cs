using Blocks.Genesis;
using Utility.DomainService.Messaging;

namespace Utility.DomainService.PdfSignatureValidation.Utilities
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class PdfSignatureValidationConstants
    {
        public const string ValidatePdfSignaturesQueue = "blocks_pdf_signature_validation_listener";

        /// <summary>
        /// Must be spread into <c>GetCombinedMessageConfiguration</c> in <c>Worker/Program.cs</c>
        /// (both the RabbitMq and Azure Service Bus branches) - a separate constants class is not
        /// picked up automatically, as <c>PdfIngestionConstants</c> also notes. Only the Worker
        /// subscribes; the Api publishes to the queue without listening on it.
        /// </summary>
        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
        {
            return MessageConfigurationHelper.GetMessageConfiguration(
                messageConnectionString,
                ValidatePdfSignaturesQueue);
        }
    }
}
