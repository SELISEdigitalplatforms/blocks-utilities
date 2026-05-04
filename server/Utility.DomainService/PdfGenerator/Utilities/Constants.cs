using Blocks.Genesis;
using Utility.DomainService.Messaging;

namespace Utility.DomainService.PdfGenerator.Utilities
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class Constants
    {
        // Queue names for PDF generator operations
        public const string MergePdfsQueue = "blocks_pdf_merge_listener_local";
        public const string CreatePdfsFromHtmlQueue = "blocks_pdf_create_from_html_listener_local";
        public const string ExtractTextFromPdfsQueue = "blocks_pdf_extract_text_listener";
        public const string CreatePdfsUsingTEQueue = "blocks_pdf_create_using_te_listener";
        public const string CreatePdfsUsingTEBulkQueue = "blocks_pdf_create_using_te_bulk_listener";
        public const string FixPdfsQueue = "blocks_pdf_fix_listener";
        public const string StampImageToPdfQueue = "blocks_pdf_stamp_image_listener";
        public const string StampTextToPdfQueue = "blocks_pdf_stamp_text_listener";
        public const string StampIntoPdfQueue = "blocks_pdf_stamp_listener";

        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
        {
            return MessageConfigurationHelper.GetMessageConfiguration(
                messageConnectionString,
                MergePdfsQueue,
                CreatePdfsFromHtmlQueue,
                ExtractTextFromPdfsQueue,
                CreatePdfsUsingTEQueue,
                CreatePdfsUsingTEBulkQueue,
                FixPdfsQueue,
                StampImageToPdfQueue,
                StampTextToPdfQueue,
                StampIntoPdfQueue
            );
        }
    }
}

