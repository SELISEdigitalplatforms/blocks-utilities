using Blocks.Genesis;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using Utility.DomainService.MagicLink;
using Utility.DomainService.MagicLink.Events;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Service;
using Utility.DomainService.MagicLink.Utilities;

namespace XUnitTest.MagicLink
{
    public class MagicLinkServiceMethodTests
    {
        private readonly Mock<ILogger<MagicLinkService>> _mockLogger;
        private readonly Mock<IMagicLinkRepository> _mockRepository;
        private readonly Mock<ICacheClient> _mockCacheClient;
        private readonly Mock<IMessageClient> _mockMessageClient;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly MagicLinkService _service;

        public MagicLinkServiceMethodTests()
        {
            _mockLogger = new Mock<ILogger<MagicLinkService>>();
            _mockRepository = new Mock<IMagicLinkRepository>();
            _mockCacheClient = new Mock<ICacheClient>();
            _mockMessageClient = new Mock<IMessageClient>();
            _mockConfiguration = new Mock<IConfiguration>();

            _mockConfiguration.Setup(c => c["RootTenantId"]).Returns("root-tenant");
            _mockConfiguration.Setup(c => c["MagicLinkBaseAddress"]).Returns("https://short.test.com");

            _service = new MagicLinkService(
                _mockLogger.Object,
                _mockRepository.Object,
                _mockCacheClient.Object,
                _mockMessageClient.Object,
                _mockConfiguration.Object
            );
        }

        #region SendUsageEventAsync Tests

        [Fact]
        public async Task SendUsageEventAsync_ShouldSendMessageToQueue()
        {
            // Arrange
            var usageEvent = new MagicLinkUsageEvent
            {
                LinkId = "test-link-123",
                ProjectKey = "project-abc",
                AccessedAt = DateTime.UtcNow,
                VisitorIpAddress = "192.168.1.1"
            };

            _mockMessageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<MagicLinkUsageEvent>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendUsageEventAsync(usageEvent);

            // Assert
            _mockMessageClient.Verify(m => m.SendToConsumerAsync(
                It.Is<ConsumerMessage<MagicLinkUsageEvent>>(msg =>
                    msg.ConsumerName == Constants.MagicLinkUsageQueue &&
                    msg.Payload == usageEvent)), Times.Once);
        }

        #endregion

        #region SendActionEventAsync Tests

        [Fact]
        public async Task SendActionEventAsync_ShouldSendMessageToQueue()
        {
            // Arrange
            var actionEvent = new MagicLinkActionEvent
            {
                LinkId = "test-link-456",
                ProjectKey = "project-xyz",
                SubscriptionFilterId = "sub-filter-123",
                NotifyOnProcessEnding = true
            };

            _mockMessageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<MagicLinkActionEvent>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendActionEventAsync(actionEvent);

            // Assert
            _mockMessageClient.Verify(m => m.SendToConsumerAsync(
                It.Is<ConsumerMessage<MagicLinkActionEvent>>(msg =>
                    msg.ConsumerName == Constants.MagicLinkActionQueue &&
                    msg.Payload == actionEvent)), Times.Once);
        }

        #endregion

        #region GenerateUniqueLinkIdAsync Tests (tested through CreateLinkAsync)

        [Fact]
        public async Task CreateLinkAsync_ShouldGenerateUniqueLinkId_OnFirstAttempt()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ProjectKey = "test-project"
            };

            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null); // No collision
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.LinkId.Should().NotBeNullOrEmpty();
            result.LinkId.Length.Should().Be(6); // Default length
            _mockRepository.Verify(r => r.GetMagicLinkAsync(It.IsAny<string>(), null), Times.Once); // Only one check needed
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldRetryOnCollision_ThenSucceed()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com",
                RequestMethod = "GET"
            };

            var existingLink = new Utility.DomainService.MagicLink.Models.MagicLink { ItemId = "ABC123" };

            // First call returns collision, second call returns null (no collision)
            _mockRepository.SetupSequence(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync(existingLink) // Collision
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null); // Success
            
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.LinkId.Should().NotBeNullOrEmpty();
            _mockRepository.Verify(r => r.GetMagicLinkAsync(It.IsAny<string>(), null), Times.Exactly(2)); // Retried once
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldGenerateLongerIdAfterMaxRetries()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };

            var existingLink = new Utility.DomainService.MagicLink.Models.MagicLink { ItemId = "collision" };

            // Setup to always return collision for first 10 attempts, then null for the longer ID
            var sequence = _mockRepository.SetupSequence(r => r.GetMagicLinkAsync(It.IsAny<string>(), null));
            for (int i = 0; i < 10; i++)
            {
                sequence = sequence.ReturnsAsync(existingLink);
            }
            sequence.ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null); // Longer ID succeeds

            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.LinkId.Should().NotBeNullOrEmpty();
            result.LinkId.Length.Should().Be(10); // Extended length (6 + 4)
            _mockRepository.Verify(r => r.GetMagicLinkAsync(It.IsAny<string>(), null), Times.AtLeast(10)); // At least 10 collision checks
        }

        #endregion

        #region GenerateLinkId Tests (tested through CreateLinkAsync)

        [Fact]
        public async Task CreateLinkAsync_ShouldGenerateLinkIdWithOnlyLetters()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };

            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            
            string? capturedLinkId = null;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLinkId = link.ItemId)
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            capturedLinkId.Should().NotBeNullOrEmpty();
            capturedLinkId.Should().MatchRegex("^[A-Za-z]+$"); // Only letters
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldGenerateDifferentLinkIds()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };

            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            var result1 = await _service.CreateLinkAsync(request);
            var result2 = await _service.CreateLinkAsync(request);
            var result3 = await _service.CreateLinkAsync(request);

            // Assert - All should be different (statistically very likely with 6 chars)
            var ids = new[] { result1.LinkId, result2.LinkId, result3.LinkId };
            ids.Should().OnlyHaveUniqueItems();
        }

        #endregion

        #region BuildShortUri Tests (tested through CreateLinkAsync)

        [Fact]
        public async Task CreateLinkAsync_ShouldUseConfigShortUrlBase_WhenConfigProvided()
        {
            // Arrange
            var configId = "config-123";
            var config = new LinkBasedActionConfig
            {
                ItemId = configId,
                ShortUrlBase = "https://custom.short.url",
                ProjectKey = "test-project"
            };

            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com",
                RequestMethod = "GET",
                LinkBasedActionConfigId = configId,
                ProjectKey = "test-project"
            };

            _mockRepository.Setup(r => r.GetLinkConfigAsync(configId, "test-project")).ReturnsAsync(config);
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.ShortUri.Should().StartWith("https://custom.short.url/");
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldUseMagicLinkBaseAddress_WhenNoConfigProvided()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };

            _mockConfiguration.Setup(c => c["MagicLinkBaseAddress"]).Returns("https://magic.link.com");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.ShortUri.Should().StartWith("https://magic.link.com/");
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldUseShortUrlBaseAddress_AsFallback()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };

            _mockConfiguration.Setup(c => c["MagicLinkBaseAddress"]).Returns((string?)null);
            _mockConfiguration.Setup(c => c["ShortUrlBaseAddress"]).Returns("https://fallback.short.com");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.ShortUri.Should().StartWith("https://fallback.short.com/");
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldTrimTrailingSlashFromBaseUrl()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };

            _mockConfiguration.Setup(c => c["MagicLinkBaseAddress"]).Returns("https://short.test.com/");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.ShortUri.Should().MatchRegex(@"^https://short\.test\.com/[A-Za-z]+$");
        }

        #endregion

        #region AddToCache Tests (tested through CreateLinkAsync)

        [Fact]
        public async Task CreateLinkAsync_ShouldAddToCache_WithExpiryForTimedLinks()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = 60000, // 60 seconds
                Persistent = false
            };

            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            _mockCacheClient.Verify(c => c.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>()), Times.Once); // Should be 60 seconds TTL
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldAddToCache_WithLongTTLForPersistentLinks()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = 0,
                Persistent = true
            };

            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            _mockCacheClient.Verify(c => c.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                (long)(365 * 24 * 60 * 60)), Times.Once); // 1 year
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldAddToCache_WithDefaultTTLForNonPersistentLinks()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = 0,
                Persistent = false
            };

            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            _mockCacheClient.Verify(c => c.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                (long)(7 * 24 * 60 * 60)), Times.Once); // 7 days
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldCacheWithCorrectJsonStructure()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com",
                RequestMethod = "POST",
                ProjectKey = "test-project"
            };

            string? capturedCacheValue = null;
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);
            _mockCacheClient.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, value, _) => capturedCacheValue = value)
                .ReturnsAsync(true);

            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            capturedCacheValue.Should().NotBeNullOrEmpty();
            capturedCacheValue.Should().Contain("\"ProjectKey\":\"test-project\"");
            capturedCacheValue.Should().Contain("\"Type\":\"Action\"");
        }

        #endregion

        #region Integration Tests for Private Methods

        [Fact]
        public async Task CreateLinkAsync_WithExpiryLifeSpan_ShouldSetExpiryDate()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = 3600000 // 1 hour in milliseconds
            };

            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            capturedLink.Should().NotBeNull();
            capturedLink!.ExpiryDate.Should().NotBeNull();
            capturedLink.ExpiryDate.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task CreateLinkAsync_WithNoExpiryLifeSpan_ShouldNotSetExpiryDate()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = 0
            };

            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), null))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);

            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            capturedLink.Should().NotBeNull();
            capturedLink!.ExpiryDate.Should().BeNull();
        }

        #endregion
    }
}
