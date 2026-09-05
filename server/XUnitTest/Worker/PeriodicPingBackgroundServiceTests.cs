using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Worker;

namespace XUnitTest.Worker
{
    /// <summary>
    /// The periodic ping service, observed through what it logs and what it sends.
    /// </summary>
    /// <remarks>
    /// <see cref="BackgroundService.StartAsync"/> returns as soon as <c>ExecuteAsync</c> first
    /// yields, so every test here has to wait for work that is still in flight. It waits for the
    /// specific thing it is about to assert -- the log entry, or the nth request -- rather than for
    /// a fixed number of milliseconds.
    /// <para>
    /// That distinction is the difference between these tests passing and failing. Sleeping a fixed
    /// interval encodes a guess about how long a machine takes to schedule a continuation, and the
    /// guess is wrong exactly when the suite is running in parallel and the CPU is saturated --
    /// which is when the whole suite runs. A wait on the event itself cannot lose that race, and it
    /// finishes in milliseconds instead of burning the full interval on every green run.
    /// </para>
    /// <para>
    /// <see cref="Timeout"/> is therefore a deadlock ceiling, not a delay: no passing test waits
    /// anywhere near it.
    /// </para>
    /// </remarks>
    public class PeriodicPingBackgroundServiceTests
    {
        /// <summary>How long a test waits before calling the service hung. Never reached on a pass.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

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
            var logged = LogSignal(mockLogger, LogLevel.Information, "Periodic ping is disabled");
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await logged.WaitAsync(Timeout);
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
            var logged = LogSignal(mockLogger, LogLevel.Warning, "PingUrl is empty");
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await logged.WaitAsync(Timeout);
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
            var logged = LogSignal(mockLogger, LogLevel.Warning, "PingUrl is empty");
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await logged.WaitAsync(Timeout);
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
            var sends = new SendCounter(1);
            var mockHttpHandler = CreateMockHttpHandler(HttpStatusCode.OK, sends);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await sends.Reached.WaitAsync(Timeout);
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
            var logged = LogSignal(mockLogger, LogLevel.Debug, "Ping success");
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await logged.WaitAsync(Timeout);
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
            var logged = LogSignal(mockLogger, LogLevel.Warning, "client error");
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await logged.WaitAsync(Timeout);
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
            var logged = LogSignal(mockLogger, LogLevel.Error, "server error");
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await logged.WaitAsync(Timeout);
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
            var logged = LogSignal(mockLogger, LogLevel.Warning, "timed out");
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await logged.WaitAsync(Timeout);
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
            var logged = LogSignal(mockLogger, LogLevel.Error, "request failed");
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await logged.WaitAsync(Timeout);
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

        /// <summary>
        /// A failed ping must not take the loop down with it: the next tick still fires.
        /// </summary>
        /// <remarks>
        /// The only test here that genuinely needs the clock to move, because it is asserting that
        /// a *second* tick happens. It waits for that second request rather than for the interval,
        /// so a slow machine makes it slower rather than making it fail.
        /// </remarks>
        [Fact]
        public async Task ExecuteAsync_WhenExceptionInLoop_ContinuesRunning()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 1);
            var sends = new SendCounter(2);
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns(() =>
                {
                    var attempt = sends.Record();
                    if (attempt == 1)
                        throw new HttpRequestException("First call failed");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                });

            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await sends.Reached.WaitAsync(Timeout);
            await service.StopAsync(cts.Token);

            // Assert
            sends.Count.Should().BeGreaterThan(1, "Service should continue after exception");
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
            var sends = new SendCounter(1);
            var mockHttpHandler = CreateMockHttpHandler(HttpStatusCode.OK, sends);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act - stop the service while it is parked on the timer, mid-loop
            await service.StartAsync(cts.Token);
            await sends.Reached.WaitAsync(Timeout);

            var stop = service.StopAsync(cts.Token);

            // Assert - shutdown returns rather than hanging on the 60s timer wait
            var act = async () => await stop.WaitAsync(Timeout);
            await act.Should().NotThrowAsync(
                "StopAsync signals the token the loop is waiting on, so it must unwind promptly");
        }

        #endregion

        #region Timer Reset Tests

        [Fact]
        public async Task ResetTimer_WithZeroInterval_DisablesTimer()
        {
            // Arrange
            var config = CreateConfiguration(enabled: true, url: "http://test.com", interval: 0);
            var sends = new SendCounter(1);
            var mockHttpHandler = CreateMockHttpHandler(HttpStatusCode.OK, sends);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await sends.Reached.WaitAsync(Timeout);
            await service.StopAsync(cts.Token);

            // Assert - only the immediate ping; a zero interval leaves no timer to fire a second
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
            var sends = new SendCounter(1);
            var mockHttpHandler = CreateMockHttpHandler(HttpStatusCode.OK, sends);
            var mockHttpFactory = CreateMockHttpClientFactory(mockHttpHandler.Object);
            var mockLogger = new Mock<ILogger<PeriodicPingBackgroundService>>();
            var service = new PeriodicPingBackgroundService(mockHttpFactory.Object, config, mockLogger.Object);
            var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await sends.Reached.WaitAsync(Timeout);
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

        /// <summary>
        /// Counts requests and completes <see cref="Reached"/> once the target count is in.
        /// </summary>
        /// <remarks>
        /// Interlocked throughout: the handler runs on the background service's thread while the
        /// test reads the count from its own, and a count read from a plain field is not guaranteed
        /// to be the count that was written.
        /// </remarks>
        private sealed class SendCounter(int target)
        {
            private readonly TaskCompletionSource _reached =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int _count;

            public Task Reached => _reached.Task;

            public int Count => Volatile.Read(ref _count);

            /// <summary>Records one request and returns which attempt it was, counting from one.</summary>
            public int Record()
            {
                var attempt = Interlocked.Increment(ref _count);
                if (attempt >= target)
                {
                    _reached.TrySetResult();
                }

                return attempt;
            }
        }

        /// <summary>
        /// A task that completes the first time <paramref name="logger"/> is given a matching entry.
        /// </summary>
        /// <remarks>
        /// Registered as a setup before the service starts, so nothing can be missed between the
        /// service beginning its work and the test beginning to wait.
        /// </remarks>
        private static Task LogSignal(
            Mock<ILogger<PeriodicPingBackgroundService>> logger,
            LogLevel level,
            string contains)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            logger
                .Setup(x => x.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(contains)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(() => signal.TrySetResult());

            return signal.Task;
        }

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

        /// <summary>
        /// A handler answering every request with <paramref name="statusCode"/>, optionally
        /// reporting each one to <paramref name="sends"/>.
        /// </summary>
        /// <remarks>
        /// A fresh response per call, because the service disposes each one it receives and a
        /// single shared instance would be handed back already disposed on the second ping.
        /// </remarks>
        private static Mock<HttpMessageHandler> CreateMockHttpHandler(
            HttpStatusCode statusCode,
            SendCounter? sends = null)
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns(() =>
                {
                    sends?.Record();
                    return Task.FromResult(new HttpResponseMessage(statusCode));
                });

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
