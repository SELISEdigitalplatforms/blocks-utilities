using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Worker;

namespace XUnitTest.Worker
{
    public class PeriodicPingBackgroundServiceTests
    {
        #region Constructor and Configuration Tests

        [Fact]
        public void Constructor_LoadsConfigurationCorrectly()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 60);
            var mockHttpFactory = new Mock<IHttpClientFactory>();
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();

            // Act
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);

            // Assert
            service.Should().NotBeNull();
        }

        #endregion

        #region ExecuteAsync - Disabled Service Tests

        [Fact]
        public async Task ExecuteAsync_WhenDisabled_LogsAndReturnsImmediately()
        {
            // Arrange
            var config = CreateConfiguration(enabled: false, url: "http://test.com", interval: 60);
            var mockHttpFactory = new Mock<IHttpClientFactory>();
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(100);
            await service.StopAsync(cts.Token);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Periodic ping is disabled")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            // Should not create any HTTP client
            mockHttpFactory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WhenEnabledButUrlEmpty_LogsWarningAndReturns()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "", interval: 60);
            var mockHttpFactory = new Mock<IHttpClientFactory>();
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(100);
            await service.StopAsync(cts.Token);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("PingUrl is empty")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            mockHttpFactory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WhenEnabledButUrlWhitespace_LogsWarningAndReturns()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "   ", interval: 60);
            var mockHttpFactory = new Mock<IHttpClientFactory>();
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(100);
            await service.StopAsync(cts.Token);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("PingUrl is empty")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region PingAsync - Success Tests

        [Fact]
        public async Task ExecuteAsync_WhenEnabled_PerformsImmediatePing()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 60);
            var mockHttpHandler = CreateMockHttpHandler(HttpStatusCode.OK);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(200); // Wait for immediate ping
            await service.StopAsync(cts.Token);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Pinging")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);

            mockHttpHandler.Protected().Verify(
                "SendAsync",
                Times.AtLeastOnce(),
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri.ToString().StartsWith("http://test.com")),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task PingAsync_WithSuccessResponse_LogsDebug()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 60);
            var mockHttpHandler = CreateMockHttpHandler(HttpStatusCode.OK);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
            await service.StopAsync(cts.Token);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Ping success")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        #endregion

        #region PingAsync - Error Response Tests

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        public async Task PingAsync_WithClientError_LogsWarning(HttpStatusCode statusCode)
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 60);
            var mockHttpHandler = CreateMockHttpHandler(statusCode);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
            await service.StopAsync(cts.Token);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("client error")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.GatewayTimeout)]
        public async Task PingAsync_WithServerError_LogsError(HttpStatusCode statusCode)
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 60);
            var mockHttpHandler = CreateMockHttpHandler(statusCode);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
            await service.StopAsync(cts.Token);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("server error")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        #endregion

        #region PingAsync - Exception Tests

        [Fact]
        public async Task PingAsync_WithTimeout_LogsWarning()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 60);
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new TaskCanceledException("Timeout"));

            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
            await service.StopAsync(cts.Token);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("timed out")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task PingAsync_WithHttpRequestException_LogsError()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 60);
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Connection failed"));

            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
            await service.StopAsync(cts.Token);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("request failed")),
                    It.IsAny<HttpRequestException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteAsync_WhenExceptionInLoop_ContinuesRunning()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 1);
            var callCount = 0;
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                        throw new HttpRequestException("First call failed");
                    return new HttpResponseMessage(HttpStatusCode.OK);
                });

            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(1500); // Wait for multiple pings
            await service.StopAsync(cts.Token);

            // Assert
            callCount.Should().BeGreaterThan(1, "Service should continue after exception");
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Periodic ping failed")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never); // HttpRequestException is caught in PingAsync, not outer loop
        }

        #endregion

        #region Cancellation Tests

        [Fact]
        public async Task ExecuteAsync_WhenCancelled_StopsGracefully()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 60);
            var mockHttpHandler = CreateMockHttpHandler(HttpStatusCode.OK);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(100);
            await service.StopAsync(cts.Token);

            // Assert - Should stop without errors
            service.Should().NotBeNull();
        }

        #endregion

        #region Timer Reset Tests

        [Fact]
        public async Task ResetTimer_WithZeroInterval_DisablesTimer()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 0);
            var mockHttpHandler = CreateMockHttpHandler(HttpStatusCode.OK);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(300); // Only immediate ping should happen
            await service.StopAsync(cts.Token);

            // Assert - Should only do immediate ping, no periodic pings
            mockHttpHandler.Protected().Verify(
                "SendAsync",
                Times.Once(), // Only the immediate ping
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task ResetTimer_WithNegativeInterval_DisablesTimer()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: -1);
            var mockHttpHandler = CreateMockHttpHandler(HttpStatusCode.OK);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(300);
            await service.StopAsync(cts.Token);

            // Assert
            mockHttpHandler.Protected().Verify(
                "SendAsync",
                Times.Once(), // Only immediate ping
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        #endregion

        #region Helper Methods

        private static IConfiguration CreateConfiguration(bool enabled, string url, int interval)
        {
            var configData = new Dictionary<string, string>
            {
                { "PeriodicPingConfiguration:Enabled", enabled.ToString() },
                { "PeriodicPingConfiguration:PingUrl", url },
                { "PeriodicPingConfiguration:PingIntervalInSeconds", interval.ToString() }
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();
        }

        private static Mock<HttpMessageHandler> CreateMockHttpHandler(HttpStatusCode statusCode)
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(statusCode));

            return mockHttpHandler;
        }

        private static Mock<IHttpClientFactory> CreateMockHttpClientFactory(HttpMessageHandler handler)
        {
            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(handler));
            return mockFactory;
        }

        #endregion
    }
}
