using Blocks.Genesis;

namespace Utility.DomainService.Messaging
{
    /// <summary>
    /// Shared helper for creating message queue configurations across all modules.
    /// Eliminates duplication in Constants files by centralizing provider detection
    /// and configuration creation logic.
    /// </summary>
    public static class MessageConfigurationHelper
    {
        private const string DefaultProvider = "azure";
        private const string RabbitMqProvider = "rabbitmq";

        /// <summary>
        /// Creates message configuration for the given connection string and queue names.
        /// Automatically detects provider (Azure Service Bus or RabbitMQ) from connection string.
        /// </summary>
        /// <param name="messageConnectionString">The message broker connection string</param>
        /// <param name="queueNames">Array of queue names to configure</param>
        /// <returns>MessageConfiguration for the detected provider</returns>
        public static MessageConfiguration GetMessageConfiguration(
            string messageConnectionString, 
            params string[] queueNames)
        {
            var provider = GetProvider(messageConnectionString);

            return provider switch
            {
                RabbitMqProvider => CreateRabbitMqConfiguration(queueNames),
                _ => CreateAzureServiceBusConfiguration(queueNames)
            };
        }

        /// <summary>
        /// Detects the message broker provider from the connection string.
        /// Checks for AMQP/AMQPS schemes to identify RabbitMQ.
        /// </summary>
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

        /// <summary>
        /// Creates RabbitMQ configuration with queue bindings.
        /// </summary>
        private static MessageConfiguration CreateRabbitMqConfiguration(string[] queueNames)
        {
            var subscriptions = queueNames
                .Select(queue => ConsumerSubscription.BindToQueue(queue))
                .ToList();

            return new MessageConfiguration
            {
                RabbitMqConfiguration = new RabbitMqConfiguration
                {
                    ConsumerSubscriptions = subscriptions
                }
            };
        }

        /// <summary>
        /// Creates Azure Service Bus configuration with queues.
        /// </summary>
        private static MessageConfiguration CreateAzureServiceBusConfiguration(string[] queueNames)
        {
            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [..queueNames],
                    Topics = [],
                    QueueMaxDeliveryCount = 10,
                }
            };
        }
    }
}
