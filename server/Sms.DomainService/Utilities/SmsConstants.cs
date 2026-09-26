using Blocks.Genesis;

namespace Sms.DomainService.Utilities;

public static class SmsConstants
{
    public const string SmsSendQueue = "blocks_sms_send_listener";
    public const string SmsStatusTopic = "blocks_sms_status_topic";

    /// <summary>
    /// The broker carries only the first send; retries and delivery checks run from the root
    /// database queue. Same scheme test as <c>MagicLinkConstants.GetProvider</c>, so both halves of
    /// the combined configuration always pick the same broker.
    /// </summary>
    public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
    {
        return IsRabbitMq(messageConnectionString)
            ? new MessageConfiguration
            {
                RabbitMqConfiguration = new RabbitMqConfiguration
                {
                    ConsumerSubscriptions = [ConsumerSubscription.BindToQueue(SmsSendQueue)]
                }
            }
            : new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [SmsSendQueue],
                    Topics = [SmsStatusTopic],
                    QueueMaxDeliveryCount = 10
                }
            };
    }

    private static bool IsRabbitMq(string messageConnectionString)
    {
        return Uri.TryCreate(messageConnectionString, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase));
    }
}
