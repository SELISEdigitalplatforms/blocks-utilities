using Blocks.Genesis;
using Utility.DomainService.Messaging;

namespace Utility.DomainService.TemplateEngine.Utilities
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class TemplateEngineConstants
    {
        // Queue names for template engine operations
        public const string RenderWithJsonQueue = "blocks_template_render_with_json_listener";
        public const string GenerateRenderedFileQueue = "blocks_template_generate_file_listener";
        public const string FilteredMongoQueryQueue = "blocks_template_filtered_query_listener";
        public const string BulkOperationsQueue = "blocks_template_bulk_operations_listener";

        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
        {
            return MessageConfigurationHelper.GetMessageConfiguration(
                messageConnectionString,
                RenderWithJsonQueue,
                GenerateRenderedFileQueue,
                FilteredMongoQueryQueue,
                BulkOperationsQueue
            );
        }
    }
}


