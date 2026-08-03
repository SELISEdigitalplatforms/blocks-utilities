using FluentAssertions;
using Utility.DomainService.Messaging;

namespace XUnitTest.Messaging;

public sealed class MessageConfigurationHelperTests
{
    [Theory]
    [InlineData("amqp://guest:guest@localhost:5672")]
    [InlineData("amqps://guest:guest@broker:5671")]
    public void Amqp_connection_strings_select_rabbitmq(string connectionString)
    {
        var configuration = MessageConfigurationHelper.GetMessageConfiguration(
            connectionString,
            "queue-a",
            "queue-b");

        configuration.RabbitMqConfiguration.Should().NotBeNull();
        configuration.AzureServiceBusConfiguration.Should().BeNull();
        configuration.RabbitMqConfiguration!.ConsumerSubscriptions
            .Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=x;SharedAccessKey=y")]
    [InlineData("not-a-uri")]
    public void Non_amqp_connection_strings_select_azure_service_bus(string connectionString)
    {
        var configuration = MessageConfigurationHelper.GetMessageConfiguration(
            connectionString,
            "queue-a",
            "queue-b",
            "queue-c");

        configuration.AzureServiceBusConfiguration.Should().NotBeNull();
        configuration.RabbitMqConfiguration.Should().BeNull();
        configuration.AzureServiceBusConfiguration!.Queues
            .Should().BeEquivalentTo("queue-a", "queue-b", "queue-c");
        configuration.AzureServiceBusConfiguration.QueueMaxDeliveryCount
            .Should().Be(10);
    }
}
