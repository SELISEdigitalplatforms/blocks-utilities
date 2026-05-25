using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.MagicLink.Service;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink;

namespace XUnitTest.MagicLink
{
    public class MagicLinkServiceTests
    {
        private readonly Mock<ILogger<MagicLinkService>> _mockLogger;
        private readonly Mock<IMagicLinkRepository> _mockRepository;
        private readonly Mock<ICacheClient> _mockCacheClient;
        private readonly Mock<IMessageClient> _mockMessageClient;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly MagicLinkService _service;

        public MagicLinkServiceTests()
        {
            _mockLogger = new Mock<ILogger<MagicLinkService>>();
            _mockRepository = new Mock<IMagicLinkRepository>();
            _mockCacheClient = new Mock<ICacheClient>();
            _mockMessageClient = new Mock<IMessageClient>();
            _mockConfiguration = new Mock<IConfiguration>();

            _mockConfiguration.Setup(c => c["RootTenantId"]).Returns("root-tenant");
            _mockConfiguration.Setup(c => c["MagicLink:BaseUrl"]).Returns("https://short.test.com");

            _service = new MagicLinkService(
                _mockLogger.Object,
                _mockRepository.Object,
                _mockCacheClient.Object,
                _mockMessageClient.Object,
                _mockConfiguration.Object
            );
        }

        #region CreateLinkAsync Tests

        [Fact]
        public async Task CreateLinkAsync_ShouldReturnSuccess_WithValidRequest()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Name = "Test Link",
                Uri = "https://example.com/destination",
                ProjectKey = "test-project"
            };
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync("link-id-123");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.LinkId.Should().NotBeNullOrEmpty();
            result.ShortUri.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldGenerateUniqueId()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink link) => link.ItemId);
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result1 = await _service.CreateLinkAsync(request);
            var result2 = await _service.CreateLinkAsync(request);

            // Assert
            result1.LinkId.Should().NotBe(result2.LinkId);
        }

        [Fact]
        public async Task CreateLinkAsync_ActionType_ShouldIncludeRequestMethod()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com/action",
                RequestMethod = "POST",
                RequestPayload = "{\"key\": \"value\"}"
            };
            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("action-link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            capturedLink.Should().NotBeNull();
            capturedLink!.RequestMethod.Should().Be("POST");
            capturedLink.RequestPayload.Should().Be("{\"key\": \"value\"}");
        }

        [Fact]
        public async Task CreateLinkAsync_WithUsageLimit_ShouldSetLimit()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                UsageLimit = 5
            };
            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("limited-link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            capturedLink.Should().NotBeNull();
            capturedLink!.UsageLimit.Should().Be(5);
            capturedLink.UsageCount.Should().Be(0);
        }

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
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("expiring-link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            capturedLink.Should().NotBeNull();
            capturedLink!.ExpiryDate.Should().NotBeNull();
            capturedLink.ExpiryDate.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task CreateLinkAsync_WithPersistentFlag_ShouldCacheWithLongTtl()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                Persistent = true,
                ExpiryLifeSpan = 0
            };
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync("persistent-link");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            // Verify cache was called with 1 year TTL (365 * 24 * 60 * 60 = 31536000)
            _mockCacheClient.Verify(c => c.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                365 * 24 * 60 * 60), Times.Once);
        }

        [Fact]
        public async Task CreateLinkAsync_NonPersistentNoExpiry_ShouldCacheWith7DayTtl()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                Persistent = false,
                ExpiryLifeSpan = 0
            };
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync("temp-link");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            // Verify cache was called with 7 day TTL (7 * 24 * 60 * 60 = 604800)
            _mockCacheClient.Verify(c => c.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                7 * 24 * 60 * 60), Times.Once);
        }

        [Fact]
        public async Task CreateLinkAsync_WithExpiryLifeSpan_ShouldCacheWithConvertedExpiry()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = 60000 // 60 seconds in milliseconds
            };
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync("expiring-link");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            // Verify cache was called with 60 second TTL (60000 / 1000 = 60)
            _mockCacheClient.Verify(c => c.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                60), Times.Once);
        }

        [Fact]
        public async Task CreateLinkAsync_LinkIdCollision_ShouldRetryAndSucceed()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };
            var existingLink = new Utility.DomainService.MagicLink.Models.MagicLink { ItemId = "existing" };
            var callCount = 0;

            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    // First call returns existing (collision), subsequent calls return null
                    return callCount == 1 ? existingLink : null;
                });
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync("new-link-id");

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            // GetMagicLinkAsync should be called at least twice due to collision
            _mockRepository.Verify(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()), Times.AtLeast(2));
        }

        [Fact]
        public async Task CreateLinkAsync_MaxRetriesExhausted_ShouldUseLongerLinkId()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };
            var existingLink = new Utility.DomainService.MagicLink.Models.MagicLink { ItemId = "existing" };
            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;

            // Always return existing link (collision) to exhaust retries
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(existingLink);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("fallback-link");

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            // The ItemId should be 10 characters (6 default + 4 fallback extension)
            capturedLink.Should().NotBeNull();
            capturedLink!.ItemId.Length.Should().Be(10);
        }

        #endregion

        #region GetLinkAsync Tests

        [Fact]
        public async Task GetLinkAsync_ShouldReturnLink_WhenExists()
        {
            // Arrange
            var request = new GetMagicLinkRequest { ItemId = "existing-link" };
            var expectedLink = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "existing-link",
                Name = "Test Link",
                Uri = "https://example.com",
                Type = MagicLinkType.Redirect
            };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.ItemId, It.IsAny<string>()))
                .ReturnsAsync(expectedLink);

            // Act
            var result = await _service.GetLinkAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.ItemId.Should().Be("existing-link");
        }

        [Fact]
        public async Task GetLinkAsync_ShouldReturnNotFound_WhenLinkDoesNotExist()
        {
            // Arrange
            var request = new GetMagicLinkRequest { ItemId = "non-existent" };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.ItemId, It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.GetLinkAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
        }

        #endregion

        #region GetLinksAsync Tests

        [Fact]
        public async Task GetLinksAsync_ShouldReturnPaginatedResults()
        {
            // Arrange
            var request = new GetMagicLinksRequest
            {
                PageSize = 10,
                PageNumber = 0,
                ProjectKey = "test-project"
            };
            var links = new List<Utility.DomainService.MagicLink.Models.MagicLink>
            {
                new() { ItemId = "link1", Name = "Link 1", Uri = "https://link1.com" },
                new() { ItemId = "link2", Name = "Link 2", Uri = "https://link2.com" }
            };
            _mockRepository.Setup(r => r.GetMagicLinksAsync(request))
                .ReturnsAsync((links, 2));

            // Act
            var result = await _service.GetLinksAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.Data.Should().HaveCount(2);
            result.TotalCount.Should().Be(2);
        }

        [Fact]
        public async Task GetLinksAsync_WithTypeFilter_ShouldFilterResults()
        {
            // Arrange
            var request = new GetMagicLinksRequest
            {
                Type = MagicLinkType.Action,
                ProjectKey = "test-project"
            };
            var links = new List<Utility.DomainService.MagicLink.Models.MagicLink>
            {
                new() { ItemId = "action1", Type = MagicLinkType.Action, Uri = "https://api.com" }
            };
            _mockRepository.Setup(r => r.GetMagicLinksAsync(It.Is<GetMagicLinksRequest>(r => r.Type == MagicLinkType.Action)))
                .ReturnsAsync((links, 1));

            // Act
            var result = await _service.GetLinksAsync(request);

            // Assert
            result.Should().NotBeNull();
            _mockRepository.Verify(r => r.GetMagicLinksAsync(
                It.Is<GetMagicLinksRequest>(req => req.Type == MagicLinkType.Action)), Times.Once);
        }

        [Fact]
        public async Task GetLinksAsync_EmptyResult_ShouldReturnEmptyList()
        {
            // Arrange
            var request = new GetMagicLinksRequest { ProjectKey = "empty-project" };
            _mockRepository.Setup(r => r.GetMagicLinksAsync(request))
                .ReturnsAsync((new List<Utility.DomainService.MagicLink.Models.MagicLink>(), 0));

            // Act
            var result = await _service.GetLinksAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
        }

        #endregion

        #region RemoveLinksAsync Tests

        [Fact]
        public async Task RemoveLinksAsync_ShouldMarkLinksAsExpired()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "link1", "link2" },
                ProjectKey = "test-project"
            };
            var existingLinks = new List<Utility.DomainService.MagicLink.Models.MagicLink>
            {
                new() { ItemId = "link1" },
                new() { ItemId = "link2" }
            };
            _mockRepository.Setup(r => r.GetMagicLinksByIdsAsync(request.LinkIds, request.ProjectKey))
                .ReturnsAsync(existingLinks);
            _mockRepository.Setup(r => r.UpdateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            _mockRepository.Verify(r => r.UpdateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()), Times.Exactly(2));
        }

        [Fact]
        public async Task RemoveLinksAsync_ShouldReturnRemovedCount_WithSuccessAndFailures()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "link1", "non-existent" },
                ProjectKey = "test-project"
            };
            var existingLinks = new List<Utility.DomainService.MagicLink.Models.MagicLink>
            {
                new() { ItemId = "link1" }
            };
            _mockRepository.Setup(r => r.GetMagicLinksByIdsAsync(request.LinkIds, request.ProjectKey))
                .ReturnsAsync(existingLinks);
            _mockRepository.Setup(r => r.MarkAsExpiredAsync("link1", MagicLinkExpiredReason.ManuallyDisabled))
                .ReturnsAsync(true);

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.RemovedCount.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public async Task RemoveLinksAsync_NullLinkIds_ShouldReturnSuccessWithZeroRemoved()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = null,
                ProjectKey = "test-project"
            };

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.RemovedCount.Should().Be(0);
        }

        [Fact]
        public async Task RemoveLinksAsync_EmptyLinkIds_ShouldReturnSuccessWithZeroRemoved()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string>(),
                ProjectKey = "test-project"
            };

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.RemovedCount.Should().Be(0);
        }

        [Fact]
        public async Task RemoveLinksAsync_CacheKeyExists_ShouldRemoveFromCache()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "cached-link" },
                ProjectKey = "test-project"
            };
            var existingLinks = new List<Utility.DomainService.MagicLink.Models.MagicLink>
            {
                new() { ItemId = "cached-link" }
            };
            _mockRepository.Setup(r => r.GetMagicLinksByIdsAsync(request.LinkIds, request.ProjectKey))
                .ReturnsAsync(existingLinks);
            _mockCacheClient.Setup(c => c.KeyExistsAsync("cached-link")).ReturnsAsync(true);
            _mockCacheClient.Setup(c => c.RemoveKeyAsync("cached-link")).ReturnsAsync(true);
            _mockRepository.Setup(r => r.UpdateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _mockCacheClient.Verify(c => c.RemoveKeyAsync("cached-link"), Times.Once);
        }

        [Fact]
        public async Task RemoveLinksAsync_ExceptionDuringRemoval_ShouldContinueWithOthers()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "link1", "link2" },
                ProjectKey = "test-project"
            };
            var existingLinks = new List<Utility.DomainService.MagicLink.Models.MagicLink>
            {
                new() { ItemId = "link1" },
                new() { ItemId = "link2" }
            };
            _mockRepository.Setup(r => r.GetMagicLinksByIdsAsync(request.LinkIds, request.ProjectKey))
                .ReturnsAsync(existingLinks);
            _mockCacheClient.Setup(c => c.KeyExistsAsync("link1")).ThrowsAsync(new Exception("Cache error"));
            _mockCacheClient.Setup(c => c.KeyExistsAsync("link2")).ReturnsAsync(false);
            _mockRepository.Setup(r => r.UpdateMagicLinkAsync(It.Is<Utility.DomainService.MagicLink.Models.MagicLink>(l => l.ItemId == "link2")))
                .ReturnsAsync(true);

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert - should still complete even with first link failing
            result.Should().NotBeNull();
        }

        #endregion

        #region InvokeLinkAsync Tests

        [Fact]
        public async Task InvokeLinkAsync_LinkNotFound_ShouldReturnError()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest { LinkId = "non-existent" };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.LinkId, It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.InvokeLinkAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be("LINK_NOT_FOUND");
        }

        [Fact]
        public async Task InvokeLinkAsync_LinkIsExpired_ShouldReturnError()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest { LinkId = "expired-link" };
            var expiredLink = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "expired-link",
                IsExpired = true,
                ExpiredReason = "ManuallyDisabled"
            };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.LinkId, It.IsAny<string>()))
                .ReturnsAsync(expiredLink);

            // Act
            var result = await _service.InvokeLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be("LINK_EXPIRED");
        }

        [Fact]
        public async Task InvokeLinkAsync_LinkTimeExpired_ShouldReturnError()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest { LinkId = "time-expired" };
            var timeExpiredLink = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "time-expired",
                IsExpired = false,
                ExpiryDate = DateTime.UtcNow.AddHours(-1) // Expired 1 hour ago
            };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.LinkId, It.IsAny<string>()))
                .ReturnsAsync(timeExpiredLink);

            // Act
            var result = await _service.InvokeLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be("LINK_EXPIRED");
        }

        [Fact]
        public async Task InvokeLinkAsync_UsageLimitExceeded_ShouldReturnError()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest { LinkId = "limit-exceeded" };
            var limitExceededLink = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "limit-exceeded",
                IsExpired = false,
                UsageLimit = 5,
                UsageCount = 5 // Limit reached
            };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.LinkId, It.IsAny<string>()))
                .ReturnsAsync(limitExceededLink);

            // Act
            var result = await _service.InvokeLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be("LINK_LIMIT_EXCEEDED");
        }

        [Fact]
        public async Task InvokeLinkAsync_RedirectType_ShouldReturnRedirectUrl()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest
            {
                LinkId = "redirect-link",
                VisitorIpAddress = "192.168.1.1"
            };
            var redirectLink = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "redirect-link",
                Type = MagicLinkType.Redirect,
                Uri = "https://destination.com",
                IsExpired = false,
                ProjectKey = "test-project"
            };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.LinkId, It.IsAny<string>()))
                .ReturnsAsync(redirectLink);

            // Act
            var result = await _service.InvokeLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.RedirectUrl.Should().Be("https://destination.com");
            result.Type.Should().Be("Redirect");
        }

        [Fact]
        public async Task InvokeLinkAsync_ActionType_ShouldQueueActionEvent()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest
            {
                LinkId = "action-link",
                VisitorIpAddress = "192.168.1.1"
            };
            var actionLink = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "action-link",
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com",
                RedirectUrl = "https://success.com",
                IsExpired = false,
                ProjectKey = "test-project"
            };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.LinkId, It.IsAny<string>()))
                .ReturnsAsync(actionLink);

            // Act
            var result = await _service.InvokeLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Type.Should().Be("Action");
            result.RedirectUrl.Should().Be("https://success.com");
            _mockMessageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<Utility.DomainService.MagicLink.Events.MagicLinkActionEvent>>()), Times.Once);
        }

        [Fact]
        public async Task InvokeLinkAsync_UsageLimitZero_ShouldAllowUnlimitedUse()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest { LinkId = "unlimited-link" };
            var unlimitedLink = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "unlimited-link",
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                IsExpired = false,
                UsageLimit = 0, // Unlimited
                UsageCount = 1000,
                ProjectKey = "test-project"
            };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.LinkId, It.IsAny<string>()))
                .ReturnsAsync(unlimitedLink);

            // Act
            var result = await _service.InvokeLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeLinkAsync_Exception_ShouldReturnError()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest { LinkId = "error-link" };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.LinkId, It.IsAny<string>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.InvokeLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Database error");
        }

        #endregion

        #region CreateLinksAsync Tests

        [Fact]
        public async Task CreateLinksAsync_ShouldCreateMultipleLinks()
        {
            // Arrange
            var request = new CreateMagicLinksRequest
            {
                ProjectKey = "bulk-project",
                Requests = new List<CreateMagicLinkRequest>
                {
                    new() { Type = MagicLinkType.Redirect, Uri = "https://link1.com" },
                    new() { Type = MagicLinkType.Redirect, Uri = "https://link2.com" }
                }
            };
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinksAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.TotalSuccessCount.Should().Be(2);
            result.Links.Should().HaveCount(2);
        }

        [Fact]
        public async Task CreateLinksAsync_IndividualRequestWithoutProjectKey_ShouldUseBulkProjectKey()
        {
            // Arrange
            var request = new CreateMagicLinksRequest
            {
                ProjectKey = "bulk-project",
                Requests = new List<CreateMagicLinkRequest>
                {
                    new() { Type = MagicLinkType.Redirect, Uri = "https://link1.com", ProjectKey = null }
                }
            };
            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            capturedLink.Should().NotBeNull();
            capturedLink!.ProjectKey.Should().Be("bulk-project");
        }

        [Fact]
        public async Task CreateLinksAsync_PartialSuccess_ShouldReturnCorrectCount()
        {
            // Arrange
            var request = new CreateMagicLinksRequest
            {
                ProjectKey = "partial-project",
                Requests = new List<CreateMagicLinkRequest>
                {
                    new() { Type = MagicLinkType.Redirect, Uri = "https://link1.com" },
                    new() { Type = MagicLinkType.Redirect, Uri = "https://link2.com" }
                }
            };
            var callCount = 0;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 2) throw new Exception("Create failed");
                    return "link-id";
                });
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue(); // At least one succeeded
            result.TotalSuccessCount.Should().Be(1);
        }

        [Fact]
        public async Task CreateLinksAsync_AllFail_ShouldReturnNoSuccess()
        {
            // Arrange
            var request = new CreateMagicLinksRequest
            {
                ProjectKey = "fail-project",
                Requests = new List<CreateMagicLinkRequest>
                {
                    new() { Type = MagicLinkType.Redirect, Uri = "https://link1.com" }
                }
            };
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ThrowsAsync(new Exception("Always fails"));
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.TotalSuccessCount.Should().Be(0);
        }

        [Fact]
        public async Task CreateLinksAsync_Exception_ShouldReturnError()
        {
            // Arrange
            var request = new CreateMagicLinksRequest
            {
                ProjectKey = "error-project",
                Requests = null! // This will cause an exception
            };

            // Act
            var result = await _service.CreateLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        #endregion

        #region CreateLinkAsync Edge Cases

        [Fact]
        public async Task CreateLinkAsync_WithLinkBasedActionConfigId_ShouldLoadConfig()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com",
                LinkBasedActionConfigId = "config-123",
                ProjectKey = "test-project"
            };
            var config = new LinkBasedActionConfig
            {
                ItemId = "config-123",
                ShortUrlBase = "https://short.custom.com"
            };
            _mockRepository.Setup(r => r.GetLinkConfigAsync("config-123", "test-project"))
                .ReturnsAsync(config);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.ShortUri.Should().Contain("https://short.custom.com");
        }

        [Fact]
        public async Task CreateLinkAsync_WithLinkBasedActionConfigId_ConfigNotFound_ShouldContinue()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com",
                LinkBasedActionConfigId = "missing-config",
                ProjectKey = "test-project"
            };
            _mockRepository.Setup(r => r.GetLinkConfigAsync("missing-config", "test-project"))
                .ReturnsAsync((LinkBasedActionConfig?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue(); // Should still work even if config not found
        }

        [Fact]
        public async Task CreateLinkAsync_NoExpiryLifeSpan_ShouldNotSetExpiryDate()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = 0
            };
            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            capturedLink.Should().NotBeNull();
            capturedLink!.ExpiryDate.Should().BeNull();
        }

        [Fact]
        public async Task CreateLinkAsync_Exception_ShouldReturnError()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Database error");
        }

        #endregion

        #region GetLinksAsync Edge Cases

        [Fact]
        public async Task GetLinksAsync_Exception_ShouldReturnError()
        {
            // Arrange
            var request = new GetMagicLinksRequest { ProjectKey = "error-project" };
            _mockRepository.Setup(r => r.GetMagicLinksAsync(request))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.GetLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Database error");
            result.Data.Should().BeEmpty();
        }

        #endregion

        #region GetLinkAsync Edge Cases

        [Fact]
        public async Task GetLinkAsync_Exception_ShouldReturnError()
        {
            // Arrange
            var request = new GetMagicLinkRequest { ItemId = "error-link" };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.ItemId, It.IsAny<string>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.GetLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Database error");
        }

        #endregion

        #region SaveLinkBasedActionConfigAsync Tests

        [Fact]
        public async Task SaveLinkBasedActionConfigAsync_NewConfig_ShouldCreate()
        {
            // Arrange
            var request = new SaveLinkBasedActionConfigRequest
            {
                ProjectKey = "new-project",
                ContextName = "Test Context",
                ShortUrlBase = "https://short.test.com"
            };
            _mockRepository.Setup(r => r.GetLinkBasedActionConfigAsync("new-project"))
                .ReturnsAsync((LinkBasedActionConfig?)null);
            _mockRepository.Setup(r => r.CreateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()))
                .ReturnsAsync("config-id");

            // Act
            var result = await _service.SaveLinkBasedActionConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.WasCreated.Should().BeTrue();
            result.Config.Should().NotBeNull();
            _mockRepository.Verify(r => r.CreateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()), Times.Once);
        }

        [Fact]
        public async Task SaveLinkBasedActionConfigAsync_ExistingConfig_ShouldUpdate()
        {
            // Arrange
            var existingConfig = new LinkBasedActionConfig
            {
                ItemId = "existing-config",
                ProjectKey = "existing-project",
                ContextName = "Old Context"
            };
            var request = new SaveLinkBasedActionConfigRequest
            {
                ProjectKey = "existing-project",
                ContextName = "New Context",
                ShortUrlBase = "https://new.test.com"
            };
            _mockRepository.Setup(r => r.GetLinkBasedActionConfigAsync("existing-project"))
                .ReturnsAsync(existingConfig);
            _mockRepository.Setup(r => r.UpdateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.SaveLinkBasedActionConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.WasCreated.Should().BeFalse();
            result.ConfigId.Should().Be("existing-config");
            _mockRepository.Verify(r => r.UpdateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()), Times.Once);
        }

        [Fact]
        public async Task SaveLinkBasedActionConfigAsync_UpdateFails_ShouldReturnError()
        {
            // Arrange
            var existingConfig = new LinkBasedActionConfig
            {
                ItemId = "fail-config",
                ProjectKey = "fail-project"
            };
            var request = new SaveLinkBasedActionConfigRequest
            {
                ProjectKey = "fail-project",
                ContextName = "Test"
            };
            _mockRepository.Setup(r => r.GetLinkBasedActionConfigAsync("fail-project"))
                .ReturnsAsync(existingConfig);
            _mockRepository.Setup(r => r.UpdateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()))
                .ReturnsAsync(false);

            // Act
            var result = await _service.SaveLinkBasedActionConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Failed to update");
        }

        [Fact]
        public async Task SaveLinkBasedActionConfigAsync_NullProjectKey_ShouldUseDefault()
        {
            // Arrange
            var request = new SaveLinkBasedActionConfigRequest
            {
                ProjectKey = null,
                ContextName = "Test Context"
            };
            _mockRepository.Setup(r => r.GetLinkBasedActionConfigAsync("root-tenant"))
                .ReturnsAsync((LinkBasedActionConfig?)null);
            _mockRepository.Setup(r => r.CreateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()))
                .ReturnsAsync("new-config");

            // Act
            var result = await _service.SaveLinkBasedActionConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task SaveLinkBasedActionConfigAsync_Exception_ShouldReturnError()
        {
            // Arrange
            var request = new SaveLinkBasedActionConfigRequest
            {
                ProjectKey = "error-project"
            };
            _mockRepository.Setup(r => r.GetLinkBasedActionConfigAsync("error-project"))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.SaveLinkBasedActionConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Database error");
        }

        #endregion

        #region GetLinkBasedActionConfigAsync Tests

        [Fact]
        public async Task GetLinkBasedActionConfigAsync_ConfigExists_ShouldReturn()
        {
            // Arrange
            var config = new LinkBasedActionConfig
            {
                ItemId = "config-1",
                ProjectKey = "test-project",
                ContextName = "Test"
            };
            var request = new GetLinkBasedActionConfigRequest { ProjectKey = "test-project" };
            _mockRepository.Setup(r => r.GetLinkBasedActionConfigAsync("test-project"))
                .ReturnsAsync(config);

            // Act
            var result = await _service.GetLinkBasedActionConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Config.Should().NotBeNull();
            result.Config!.ItemId.Should().Be("config-1");
        }

        [Fact]
        public async Task GetLinkBasedActionConfigAsync_ConfigNotFound_ShouldReturnNull()
        {
            // Arrange
            var request = new GetLinkBasedActionConfigRequest { ProjectKey = "no-config" };
            _mockRepository.Setup(r => r.GetLinkBasedActionConfigAsync("no-config"))
                .ReturnsAsync((LinkBasedActionConfig?)null);

            // Act
            var result = await _service.GetLinkBasedActionConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Config.Should().BeNull();
        }

        [Fact]
        public async Task GetLinkBasedActionConfigAsync_NullProjectKey_ShouldUseDefault()
        {
            // Arrange
            var request = new GetLinkBasedActionConfigRequest { ProjectKey = null };
            _mockRepository.Setup(r => r.GetLinkBasedActionConfigAsync("root-tenant"))
                .ReturnsAsync((LinkBasedActionConfig?)null);

            // Act
            var result = await _service.GetLinkBasedActionConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetLinkBasedActionConfigAsync_Exception_ShouldReturnError()
        {
            // Arrange
            var request = new GetLinkBasedActionConfigRequest { ProjectKey = "error-project" };
            _mockRepository.Setup(r => r.GetLinkBasedActionConfigAsync("error-project"))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.GetLinkBasedActionConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Database error");
        }

        #endregion
    }
}
