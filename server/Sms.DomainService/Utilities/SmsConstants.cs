using Blocks.Genesis;

namespace Sms.DomainService.Utilities;

public static class SmsConstants
{
    public const string SmsSendQueue = "blocks_sms_send_listener";
    public const string SmsDeliveryCheckQueue = "blocks_sms_delivery_check_listener";
    public const string SmsStatusTopic = "blocks_sms_status_topic";

    private const string RabbitMqProvider = "rabbitmq";

    public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
    {
        var queues = new[] { SmsSendQueue, SmsDeliveryCheckQueue };
        return IsRabbitMq(messageConnectionString)
            ? new MessageConfiguration
            {
                RabbitMqConfiguration = new RabbitMqConfiguration
                {
                    ConsumerSubscriptions = queues.Select(queue => ConsumerSubscription.BindToQueue(queue)).ToList()
                }
            }
            : new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [..queues],
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


