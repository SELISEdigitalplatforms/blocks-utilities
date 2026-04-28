using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Shared.Utilities
{
    public static class Constants
    {
        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
        {
            var provider = GetProvider(messageConnectionString);

            return provider switch
            {
                RabbitMqProvider => CreateRabbitMqConfiguration(),
                _ => CreateAzureServiceBusConfiguration()
            };
        }

        private static string GetProvider(string messageConnectionString)
        {
            if (Uri.TryCreate(messageConnectionString, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase)))
            {
                return RabbitMqProvider;
            }

            return DefaultProvider;
        }

        private static MessageConfiguration CreateRabbitMqConfiguration()
        {
            return new MessageConfiguration
            {
                RabbitMqConfiguration = new RabbitMqConfiguration
                {
                    ConsumerSubscriptions = [],
                }
            };
        }

        private static MessageConfiguration CreateAzureServiceBusConfiguration()
        {
            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [],
                    Topics = []
                }
            };
        }

        public const string AuthenticationQueue = "blocks_authentication_listener";
        public const string DefaultMfaTemplateName = "MfaViaEmail";
        public const string DefaultMfaTemplateId = "0b121378-3c3d-44f3-a855-9da08cbef48c";
        public const string StorageQueue = "blocks_storage_listener";
        private const string DefaultProvider = "azure";
        private const string RabbitMqProvider = "rabbitmq";
    }
}
