using Blocks.Genesis;
using Utility.DomainService.Messaging;

namespace Utility.DomainService.MagicLink.Utilities
{
    public static class Constants
    {
        // Queue names for magic link operations
        public const string MagicLinkUsageQueue = "blocks_magiclink_usage_listener";
        public const string MagicLinkActionQueue = "blocks_magiclink_action_listener";

        // Collection names
        public const string MagicLinksCollection = "MagicLinks";
        public const string MagicLinkVisitorUsagesCollection = "MagicLinkVisitorUsages";
        public const string LinkBasedActionConfigsCollection = "LinkBasedActionConfigs";
        public const string ClientCredentialsCollection = "ClientCredentials";

        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
        {
            return MessageConfigurationHelper.GetMessageConfiguration(
                messageConnectionString,
                MagicLinkUsageQueue,
                MagicLinkActionQueue
            );
        }
    }
}

