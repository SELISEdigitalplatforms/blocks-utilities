using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Utility.DomainService.MagicLink.Models
{
    /// <summary>
    /// Configuration for link-based action generation.
    /// Collection: LinkBasedActionConfigs
    /// </summary>
    [BsonIgnoreExtraElements]
    public class LinkBasedActionConfig : IProjectKey
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        
        /// <summary>
        /// Context name for the configuration
        /// </summary>
        public string ContextName { get; set; } = string.Empty;
        
        /// <summary>
        /// Base URL for generating short URLs (e.g., https://short.example.com/)
        /// </summary>
        public string ShortUrlBase { get; set; } = string.Empty;
        
        /// <summary>
        /// The project/tenant identifier
        /// </summary>
        public string ProjectKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Created date
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// Last updated date
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
