using Blocks.Genesis;
using Utility.DomainService.Messaging;

namespace Utility.DomainService.PdfIngestion.Utilities
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class PdfIngestionConstants
    {
        public const string IngestPdfQueue = "blocks_pdf_ingestion_listener";

        /// <summary>
        /// Must be spread into <c>GetCombinedMessageConfiguration</c> in <c>Worker/Program.cs</c>
        /// (both the RabbitMq and Azure Service Bus branches) - a separate constants class is not
        /// picked up automatically the way an entry added to <c>PdfGeneratorConstants</c> would be.
        /// </summary>
        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
        {
            return MessageConfigurationHelper.GetMessageConfiguration(
                messageConnectionString,
                IngestPdfQueue);
        }
    }
}
