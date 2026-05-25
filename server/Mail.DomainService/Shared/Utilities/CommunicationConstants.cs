using Blocks.Genesis;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Utilities;

public static class CommunicationConstants
{
    public const string MailQueueName = "blocks_email_listener";
    public const string NotificationQueueName = "blocks_notification_listener";
    public const string EmailTriggerQueueName = "blocks_workflow_email_trigger_listener";

    public static readonly MailStatus[] AllowedFilterStatuses = { 
        MailStatus.Sent, 
        MailStatus.Delivered, 
        MailStatus.Bounced, 
        MailStatus.Complained, 
        MailStatus.Rejected,
        MailStatus.Received,
    };
    public const string SnsNotifierName = "blocks-test";
    private const string DefaultProvider = "azure";
    private const string RabbitMqProvider = "rabbitmq";

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

    private static MessageConfiguration CreateRabbitMqConfiguration()
    {
        return new MessageConfiguration
        {
            RabbitMqConfiguration = new RabbitMqConfiguration
            {
                ConsumerSubscriptions = [ConsumerSubscription.BindToQueue(MailQueueName)],
            }
        };
    }

    private static MessageConfiguration CreateAzureServiceBusConfiguration()
    {
        return new MessageConfiguration
        {
            AzureServiceBusConfiguration = new AzureServiceBusConfiguration
            {
                Queues = [MailQueueName],
                Topics = []
            }
        };
    }
}
