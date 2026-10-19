using Blocks.Genesis;
using Utility.DomainService.Messaging;

namespace Utility.DomainService.MagicLink.Utilities
{
    public static class MagicLinkConstants
    {
        // Queue names for magic link operations
        public const string MagicLinkUsageQueue = "blocks_magiclink_usage_listener";
        public const string MagicLinkActionQueue = "blocks_magiclink_action_listener";

        // Collection names
        public const string MagicLinksCollection = "MagicLinks";
        public const string MagicLinkVisitorUsagesCollection = "MagicLinkVisitorUsages";
        public const string LinkBasedActionConfigsCollection = "LinkBasedActionConfigs";
        public const string ClientCredentialsCollection = "ClientCredentials";

        // Named HttpClient for Action links; registered with a public-address-only connector
        public const string ActionHttpClientName = "MagicLinkAction";
        public const string DefaultAuthenticationTokenEndpoint = "https://api.blocksdevelopers.com/idp/v1/Authentication/token";

        public const string DefaultProvider = "azure";
        public const string RabbitMqProvider = "rabbitmq";
        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
        {
            return MessageConfigurationHelper.GetMessageConfiguration(
                messageConnectionString,
                MagicLinkUsageQueue,
                MagicLinkActionQueue
            );
        }
        public static string GetProvider ( string messageConnectionString )
        {
            if (Uri.TryCreate(messageConnectionString, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase))
                {
                    return RabbitMqProvider;
                }
            }

            return DefaultProvider;
        }
     }
}

