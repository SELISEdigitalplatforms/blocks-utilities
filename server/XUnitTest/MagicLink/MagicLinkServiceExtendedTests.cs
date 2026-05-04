using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.MagicLink;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Service;

namespace XUnitTest.MagicLink
{
    public class MagicLinkServiceExtendedTests
    {
        private readonly Mock<ILogger<MagicLinkService>> _mockLogger;
        private readonly Mock<IMagicLinkRepository> _mockRepository;
        private readonly Mock<ICacheClient> _mockCacheClient;
        private readonly Mock<IMessageClient> _mockMessageClient;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly MagicLinkService _service;

        public MagicLinkServiceExtendedTests()
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

        #region CreateLinkAsync Exception Tests

        [Fact]
        public async Task CreateLinkAsync_ShouldReturnError_WhenRepositoryThrowsException()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com"
            };
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ThrowsAsync(new Exception("Database error"));
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Error creating link");
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldUseProjectKey_WhenProvided()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ProjectKey = "custom-project"
            };
            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            capturedLink.Should().NotBeNull();
            capturedLink!.ProjectKey.Should().Be("custom-project");
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldUseFallbackProjectKey_WhenNotProvided()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ProjectKey = null
            };
            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);
            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            capturedLink.Should().NotBeNull();
            capturedLink!.ProjectKey.Should().Be("root-tenant");
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldSetNoExpiry_WhenLifeSpanIsZero()
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
            await _service.CreateLinkAsync(request);

            // Assert
            capturedLink.Should().NotBeNull();
            capturedLink!.ExpiryDate.Should().BeNull();
        }

        [Fact]
        public async Task CreateLinkAsync_ShouldSetExpiryDate_WhenLifeSpanIsPositive()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = 3600000 // 1 hour
            };
            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            await _service.CreateLinkAsync(request);

            // Assert
            capturedLink.Should().NotBeNull();
            capturedLink!.ExpiryDate.Should().NotBeNull();
            capturedLink.ExpiryDate.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));
        }

        #endregion

        #region CreateLinksAsync Tests

        [Fact]
        public async Task CreateLinksAsync_ShouldReturnSuccess_WithMultipleRequests()
        {
            // Arrange
            var request = new CreateMagicLinksRequest
            {
                ProjectKey = "bulk-project",
                Requests = new List<CreateMagicLinkRequest>
                {
                    new() { Type = MagicLinkType.Redirect, Uri = "https://example1.com" },
                    new() { Type = MagicLinkType.Redirect, Uri = "https://example2.com" }
                }
            };
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Links.Should().HaveCount(2);
            result.TotalSuccessCount.Should().Be(2);
        }

        [Fact]
        public async Task CreateLinksAsync_ShouldCountPartialSuccess()
        {
            // Arrange
            var request = new CreateMagicLinksRequest
            {
                Requests = new List<CreateMagicLinkRequest>
                {
                    new() { Type = MagicLinkType.Redirect, Uri = "https://example1.com" },
                    new() { Type = MagicLinkType.Redirect, Uri = "https://example2.com" }
                }
            };
            var callCount = 0;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 2)
                        throw new Exception("Second request failed");
                    return "link-id-1";
                });
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _service.CreateLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue(); // At least one success
            result.TotalSuccessCount.Should().Be(1);
            result.Links.Should().HaveCount(2);
        }

        [Fact]
        public async Task CreateLinksAsync_ShouldReturnError_WhenExceptionOccurs()
        {
            // Arrange
            var request = new CreateMagicLinksRequest
            {
                Requests = null! // Force an exception
            };

            // Act
            var result = await _service.CreateLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Error creating links");
        }

        [Fact]
        public async Task CreateLinksAsync_ShouldUseParentProjectKey_WhenChildHasNone()
        {
            // Arrange
            var request = new CreateMagicLinksRequest
            {
                ProjectKey = "parent-project",
                Requests = new List<CreateMagicLinkRequest>
                {
                    new() { Type = MagicLinkType.Redirect, Uri = "https://example.com", ProjectKey = null }
                }
            };
            Utility.DomainService.MagicLink.Models.MagicLink? capturedLink = null;
            _mockRepository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .Callback<Utility.DomainService.MagicLink.Models.MagicLink>(link => capturedLink = link)
                .ReturnsAsync("link-id");
            _mockRepository.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            await _service.CreateLinksAsync(request);

            // Assert
            capturedLink.Should().NotBeNull();
            capturedLink!.ProjectKey.Should().Be("parent-project");
        }

        #endregion

        #region RemoveLinksAsync Tests

        [Fact]
        public async Task RemoveLinksAsync_ShouldReturnZero_WhenNoLinkIds()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string>()
            };

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.RemovedCount.Should().Be(0);
        }

        [Fact]
        public async Task RemoveLinksAsync_ShouldReturnZero_WhenLinkIdsIsNull()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = null!
            };

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.RemovedCount.Should().Be(0);
        }

        [Fact]
        public async Task RemoveLinksAsync_ShouldReturnError_WhenExceptionOccurs()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "link-1" },
                ProjectKey = "test-project"
            };
            _mockRepository.Setup(r => r.GetMagicLinksByIdsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Error removing links");
        }

        [Fact]
        public async Task RemoveLinksAsync_ShouldRemoveFromCache_WhenKeyExists()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "link-1" },
                ProjectKey = "test-project"
            };
            var links = new List<Utility.DomainService.MagicLink.Models.MagicLink>
            {
                new() { ItemId = "link-1" }
            };
            _mockRepository.Setup(r => r.GetMagicLinksByIdsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
                .ReturnsAsync(links);
            _mockCacheClient.Setup(c => c.KeyExistsAsync("link-1"))
                .ReturnsAsync(true);
            _mockCacheClient.Setup(c => c.RemoveKeyAsync("link-1"))
                .ReturnsAsync(true);
            _mockRepository.Setup(r => r.UpdateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _mockCacheClient.Verify(c => c.RemoveKeyAsync("link-1"), Times.Once);
        }

        [Fact]
        public async Task RemoveLinksAsync_ShouldNotRemoveFromCache_WhenKeyDoesNotExist()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "link-1" },
                ProjectKey = "test-project"
            };
            var links = new List<Utility.DomainService.MagicLink.Models.MagicLink>
            {
                new() { ItemId = "link-1" }
            };
            _mockRepository.Setup(r => r.GetMagicLinksByIdsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
                .ReturnsAsync(links);
            _mockCacheClient.Setup(c => c.KeyExistsAsync("link-1"))
                .ReturnsAsync(false);
            _mockRepository.Setup(r => r.UpdateMagicLinkAsync(It.IsAny<Utility.DomainService.MagicLink.Models.MagicLink>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.RemoveLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _mockCacheClient.Verify(c => c.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region GetLinkAsync Tests

        [Fact]
        public async Task GetLinkAsync_ShouldReturnError_WhenExceptionOccurs()
        {
            // Arrange
            var request = new GetMagicLinkRequest { ItemId = "link-1" };
            _mockRepository.Setup(r => r.GetMagicLinkAsync(request.ItemId, It.IsAny<string>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.GetLinkAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Failed to get link");
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
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("not found");
        }

        #endregion

        #region GetLinksAsync Tests

        [Fact]
        public async Task GetLinksAsync_ShouldReturnPaginatedList()
        {
            // Arrange
            var request = new GetMagicLinksRequest
            {
                ProjectKey = "test-project",
                PageSize = 10,
                PageNumber = 0
            };
            var links = new List<Utility.DomainService.MagicLink.Models.MagicLink>
            {
                new() { ItemId = "link-1", Uri = "https://example1.com" },
                new() { ItemId = "link-2", Uri = "https://example2.com" }
            };
            _mockRepository.Setup(r => r.GetMagicLinksAsync(request))
                .ReturnsAsync((links, 2));

            // Act
            var result = await _service.GetLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Data.Should().HaveCount(2);
            result.TotalCount.Should().Be(2);
        }

        [Fact]
        public async Task GetLinksAsync_ShouldReturnError_WhenExceptionOccurs()
        {
            // Arrange
            var request = new GetMagicLinksRequest { ProjectKey = "test-project" };
            _mockRepository.Setup(r => r.GetMagicLinksAsync(request))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.GetLinksAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Failed to get links");
        }

        #endregion
    }
}
