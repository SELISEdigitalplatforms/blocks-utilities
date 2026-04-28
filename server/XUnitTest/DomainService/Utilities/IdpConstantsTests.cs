using Blocks.Genesis;
using DomainService.Utilities;
using FluentAssertions;
using Xunit;

namespace XUnitTest.DomainService.Utilities
{
    public class IdpConstantsTests
    {
        #region Constant Values Tests

        [Fact]
        public void TenantTokenPublicCertificateCachePrefix_ShouldHaveCorrectValue()
        {
            // Assert
            IdpConstants.TenantTokenPublicCertificateCachePrefix.Should().Be("tetocertpublic::");
        }

        [Fact]
        public void AuthenticationQueue_ShouldHaveCorrectValue()
        {
            // Assert
            IdpConstants.AuthenticationQueue.Should().Be("blocks_authentication_listener");
        }

        [Fact]
        public void IamQueue_ShouldHaveCorrectValue()
        {
            // Assert
            IdpConstants.IamQueue.Should().Be("blocks_iam_listener");
        }

        [Fact]
        public void MailQueue_ShouldHaveCorrectValue()
        {
            // Assert
            IdpConstants.MailQueue.Should().Be("blocks_mail_listener");
        }

        [Fact]
        public void MfaQueueName_ShouldHaveCorrectValue()
        {
            // Assert
            IdpConstants.MfaQueueName.Should().Be("blocks_mfa_listener");
        }

        [Fact]
        public void AccessTokenCookieName_ShouldHaveCorrectValue()
        {
            // Assert
            IdpConstants.AccessTokenCookieName.Should().Be("access_token");
        }

        [Fact]
        public void RefreshTokenCookieName_ShouldHaveCorrectValue()
        {
            // Assert
            IdpConstants.RefreshTokenCookieName.Should().Be("refresh_token");
        }

        #endregion

        #region GetMessageConfiguration - RabbitMQ Tests

        [Fact]
        public void GetMessageConfiguration_WithAmqpScheme_ReturnsRabbitMqConfiguration()
        {
            // Arrange
            var connectionString = "amqp://<username>:<password>@localhost:5672/";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithAmqpsScheme_ReturnsRabbitMqConfiguration()
        {
            // Arrange
            var connectionString = "amqps://<username>:<password>@rabbitmq.example.com:5671/vhost";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithAmqpScheme_ConfiguresAllQueues()
        {
            // Arrange
            var connectionString = "amqp://localhost:5672";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.RabbitMqConfiguration.ConsumerSubscriptions.Should().HaveCount(3);
            result.RabbitMqConfiguration.ConsumerSubscriptions.Should().Contain(s => 
                s.QueueName == IdpConstants.AuthenticationQueue);
            result.RabbitMqConfiguration.ConsumerSubscriptions.Should().Contain(s => 
                s.QueueName == IdpConstants.IamQueue);
            result.RabbitMqConfiguration.ConsumerSubscriptions.Should().Contain(s => 
                s.QueueName == IdpConstants.MfaQueueName);
        }

        [Fact]
        public void GetMessageConfiguration_WithRabbitMq_CreatesBindToQueueSubscriptions()
        {
            // Arrange
            var connectionString = "amqp://localhost:5672";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            var authSubscription = result.RabbitMqConfiguration.ConsumerSubscriptions
                .FirstOrDefault(s => s.QueueName == IdpConstants.AuthenticationQueue);
            authSubscription.Should().NotBeNull();
            authSubscription.QueueName.Should().Be(IdpConstants.AuthenticationQueue);

            var iamSubscription = result.RabbitMqConfiguration.ConsumerSubscriptions
                .FirstOrDefault(s => s.QueueName == IdpConstants.IamQueue);
            iamSubscription.Should().NotBeNull();
            iamSubscription.QueueName.Should().Be(IdpConstants.IamQueue);

            var mfaSubscription = result.RabbitMqConfiguration.ConsumerSubscriptions
                .FirstOrDefault(s => s.QueueName == IdpConstants.MfaQueueName);
            mfaSubscription.Should().NotBeNull();
            mfaSubscription.QueueName.Should().Be(IdpConstants.MfaQueueName);
        }

        [Fact]
        public void GetMessageConfiguration_WithAmqpUpperCase_ReturnsRabbitMqConfiguration()
        {
            // Arrange
            var connectionString = "AMQP://localhost:5672";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithAmqpsUpperCase_ReturnsRabbitMqConfiguration()
        {
            // Arrange
            var connectionString = "AMQPS://localhost:5671";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithMixedCaseAmqp_ReturnsRabbitMqConfiguration()
        {
            // Arrange
            var connectionString = "AmQp://localhost:5672";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().BeNull();
        }

        #endregion

        #region GetMessageConfiguration - Azure Service Bus Tests

        [Fact]
        public void GetMessageConfiguration_WithAzureServiceBusConnectionString_ReturnsAzureConfiguration()
        {
            // Arrange
            var connectionString = "Endpoint=sb://myservicebus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=key";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithHttpScheme_ReturnsAzureConfiguration()
        {
            // Arrange
            var connectionString = "http://example.com";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithHttpsScheme_ReturnsAzureConfiguration()
        {
            // Arrange
            var connectionString = "https://example.com";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithInvalidUri_ReturnsAzureConfiguration()
        {
            // Arrange
            var connectionString = "not-a-valid-uri";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithEmptyString_ReturnsAzureConfiguration()
        {
            // Arrange
            var connectionString = "";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithAzureServiceBus_ConfiguresAllQueues()
        {
            // Arrange
            var connectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Key;SharedAccessKey=value";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.AzureServiceBusConfiguration.Queues.Should().HaveCount(3);
            result.AzureServiceBusConfiguration.Queues.Should().Contain(IdpConstants.AuthenticationQueue);
            result.AzureServiceBusConfiguration.Queues.Should().Contain(IdpConstants.IamQueue);
            result.AzureServiceBusConfiguration.Queues.Should().Contain(IdpConstants.MfaQueueName);
        }

        [Fact]
        public void GetMessageConfiguration_WithAzureServiceBus_ConfiguresEmptyTopics()
        {
            // Arrange
            var connectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Key;SharedAccessKey=value";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.AzureServiceBusConfiguration.Topics.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Topics.Should().BeEmpty();
        }

        [Fact]
        public void GetMessageConfiguration_WithSbScheme_ReturnsAzureConfiguration()
        {
            // Arrange
            var connectionString = "sb://test.servicebus.windows.net/";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().BeNull();
        }

        #endregion

        #region GetMessageConfiguration - Edge Cases Tests

        [Fact]
        public void GetMessageConfiguration_WithAmqpAndQueryString_ReturnsRabbitMqConfiguration()
        {
            // Arrange
            var connectionString = "amqp://localhost:5672?heartbeat=30";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().BeNull();
        }

        [Fact]
        public void GetMessageConfiguration_WithAmqpAndFragment_ReturnsRabbitMqConfiguration()
        {
            // Arrange
            var connectionString = "amqp://localhost:5672#fragment";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().BeNull();
        }
                
        [Fact]
        public void GetMessageConfiguration_WithFtpScheme_ReturnsAzureConfiguration()
        {
            // Arrange
            var connectionString = "ftp://example.com";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().BeNull();
        }

        [Theory]
        [InlineData("amqp://localhost")]
        [InlineData("amqps://localhost")]
        [InlineData("AMQP://LOCALHOST")]
        [InlineData("AmQpS://localhost")]
        public void GetMessageConfiguration_WithVariousAmqpFormats_ReturnsRabbitMqConfiguration(string connectionString)
        {
            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().BeNull();
        }

        [Theory]
        [InlineData("http://example.com")]
        [InlineData("https://example.com")]
        [InlineData("sb://servicebus.windows.net")]
        [InlineData("Endpoint=sb://test.servicebus.windows.net")]
        [InlineData("InvalidConnectionString")]
        [InlineData("")]
        public void GetMessageConfiguration_WithNonRabbitMqFormats_ReturnsAzureConfiguration(string connectionString)
        {
            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.Should().NotBeNull();
            result.AzureServiceBusConfiguration.Should().NotBeNull();
            result.RabbitMqConfiguration.Should().BeNull();
        }

        #endregion

        #region Queue Names Consistency Tests

        [Fact]
        public void GetMessageConfiguration_RabbitMqQueues_ShouldMatchConstantValues()
        {
            // Arrange
            var connectionString = "amqp://localhost";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            var queueNames = result.RabbitMqConfiguration.ConsumerSubscriptions
                .Select(s => s.QueueName)
                .ToList();

            queueNames.Should().Contain(IdpConstants.AuthenticationQueue);
            queueNames.Should().Contain(IdpConstants.IamQueue);
            queueNames.Should().Contain(IdpConstants.MfaQueueName);
            queueNames.Should().NotContain(IdpConstants.MailQueue); // Mail queue is not in consumer subscriptions
        }

        [Fact]
        public void GetMessageConfiguration_AzureQueues_ShouldMatchConstantValues()
        {
            // Arrange
            var connectionString = "Endpoint=sb://test.servicebus.windows.net";

            // Act
            var result = IdpConstants.GetMessageConfiguration(connectionString);

            // Assert
            result.AzureServiceBusConfiguration.Queues.Should().Contain(IdpConstants.AuthenticationQueue);
            result.AzureServiceBusConfiguration.Queues.Should().Contain(IdpConstants.IamQueue);
            result.AzureServiceBusConfiguration.Queues.Should().Contain(IdpConstants.MfaQueueName);
            result.AzureServiceBusConfiguration.Queues.Should().NotContain(IdpConstants.MailQueue); // Mail queue is not configured
        }

        #endregion

        #region Return Type Tests

        [Fact]
        public void GetMessageConfiguration_AlwaysReturnsMessageConfiguration()
        {
            // Arrange
            var connectionStrings = new[]
            {
                "amqp://localhost",
                "amqps://localhost",
                "https://example.com",
                "invalid-string",
                ""
            };

            foreach (var connectionString in connectionStrings)
            {
                // Act
                var result = IdpConstants.GetMessageConfiguration(connectionString);

                // Assert
                result.Should().NotBeNull();
                result.Should().BeOfType<MessageConfiguration>();
            }
        }

        [Fact]
        public void GetMessageConfiguration_NeverReturnsNull()
        {
            // Arrange
            var testCases = new[]
            {
                "amqp://test",
                "amqps://test",
                "http://test",
                "https://test",
                "invalid",
                ""
            };

            foreach (var connectionString in testCases)
            {
                // Act
                var result = IdpConstants.GetMessageConfiguration(connectionString);

                // Assert
                result.Should().NotBeNull($"connection string '{connectionString}' should not return null");
            }
        }

        [Fact]
        public void GetMessageConfiguration_OnlyOneConfigurationIsSet()
        {
            // Arrange
            var testCases = new[]
            {
                "amqp://localhost",
                "amqps://localhost",
                "https://example.com",
                "Endpoint=sb://test.servicebus.windows.net"
            };

            foreach (var connectionString in testCases)
            {
                // Act
                var result = IdpConstants.GetMessageConfiguration(connectionString);

                // Assert
                var bothNull = result.RabbitMqConfiguration == null && result.AzureServiceBusConfiguration == null;
                var bothSet = result.RabbitMqConfiguration != null && result.AzureServiceBusConfiguration != null;
                
                bothNull.Should().BeFalse("at least one configuration should be set");
                bothSet.Should().BeFalse("only one configuration should be set at a time");
            }
        }

        #endregion
    }
}
