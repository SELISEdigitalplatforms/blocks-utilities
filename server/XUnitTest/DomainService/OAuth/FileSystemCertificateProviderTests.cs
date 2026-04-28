using DomainService.OAuth;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.OAuth
{
    public class FileSystemCertificateProviderTests
    {
        private readonly Mock<ILogger> _logger = new();

        [Fact]
        public async Task GetCertificateAsync_WithEmptyPath_ReturnsEmptyArrayAndLogsError()
        {
            // Arrange
            var provider = new FileSystemCertificateProvider(_logger.Object);
            var key = "test-certificate-key";

            // Act
            var result = await provider.GetCertificateAsync(key);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error retrieving certificate from file system")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
    }
}