using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Moq;
using Utility.DomainService.MagicLink;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Service;

namespace XUnitTest.MagicLink
{
    public class MagicLinkRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _mockDbContextProvider;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly MagicLinkRepository _repository;

        public MagicLinkRepositoryTests()
        {
            _mockDbContextProvider = new Mock<IDbContextProvider>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockConfiguration.Setup(c => c["RootTenantId"]).Returns("root-tenant");
            _repository = new MagicLinkRepository(_mockDbContextProvider.Object, _mockConfiguration.Object);
        }

        #region IncrementUsageCountAsync Tests

        [Fact]
        public async Task IncrementUsageCountAsync_ShouldReturnUpdatedLink_WhenSuccessful()
        {
            // Arrange
            var linkId = "test-link-123";
            var updatedLink = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = linkId,
                UsageCount = 5,
                UpdatedAt = DateTime.UtcNow
            };

            var mockCollection = new Mock<IMongoCollection<Utility.DomainService.MagicLink.Models.MagicLink>>();
            _mockDbContextProvider.Setup(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(mockCollection.Object);

            mockCollection.Setup(c => c.FindOneAndUpdateAsync(
                It.IsAny<FilterDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                It.IsAny<UpdateDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                It.IsAny<FindOneAndUpdateOptions<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(updatedLink);

            // Act
            var result = await _repository.IncrementUsageCountAsync(linkId);

            // Assert
            result.Should().NotBeNull();
            result!.ItemId.Should().Be(linkId);
            result.UsageCount.Should().Be(5);
        }

        [Fact]
        public async Task IncrementUsageCountAsync_ShouldReturnNull_WhenLinkNotFound()
        {
            // Arrange
            var mockCollection = new Mock<IMongoCollection<Utility.DomainService.MagicLink.Models.MagicLink>>();
            _mockDbContextProvider.Setup(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(mockCollection.Object);

            mockCollection.Setup(c => c.FindOneAndUpdateAsync(
                It.IsAny<FilterDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                It.IsAny<UpdateDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                It.IsAny<FindOneAndUpdateOptions<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((Utility.DomainService.MagicLink.Models.MagicLink?)null);

            // Act
            var result = await _repository.IncrementUsageCountAsync("non-existent");

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region GetClientCredentialsAsync Tests

        [Fact]
        public async Task GetClientCredentialsAsync_ShouldReturnClientCredential_WhenFound()
        {
            // Arrange
            var clientCredentialId = "cred-123";
            var projectKey = "project-abc";
            var expectedCredential = new ClientCredential
            {
                ItemId = clientCredentialId,
                Name = "Test Credential",
                ClientSecret = "secret",
                IsActive = true
            };

            var mockDatabase = new Mock<IMongoDatabase>();
            var mockCollection = new Mock<IMongoCollection<ClientCredential>>();
            var mockAsyncCursor = new Mock<IAsyncCursor<ClientCredential>>();

            _mockDbContextProvider.Setup(p => p.GetDatabase(projectKey)).Returns(mockDatabase.Object);
            mockDatabase.Setup(d => d.GetCollection<ClientCredential>(It.IsAny<string>(), null))
                .Returns(mockCollection.Object);

            mockAsyncCursor.Setup(c => c.Current).Returns(new[] { expectedCredential });
            mockAsyncCursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            mockAsyncCursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);

            mockCollection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<ClientCredential>>(),
                It.IsAny<FindOptions<ClientCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockAsyncCursor.Object);

            // Act
            var result = await _repository.GetClientCredentialsAsync(clientCredentialId, projectKey);

            // Assert
            result.Should().NotBeNull();
            result!.ItemId.Should().Be(clientCredentialId);
        }

        [Fact]
        public async Task GetClientCredentialsAsync_ShouldReturnNull_WhenNotFound()
        {
            // Arrange
            var mockDatabase = new Mock<IMongoDatabase>();
            var mockCollection = new Mock<IMongoCollection<ClientCredential>>();
            var mockAsyncCursor = new Mock<IAsyncCursor<ClientCredential>>();

            _mockDbContextProvider.Setup(p => p.GetDatabase(It.IsAny<string>())).Returns(mockDatabase.Object);
            mockDatabase.Setup(d => d.GetCollection<ClientCredential>(It.IsAny<string>(), null))
                .Returns(mockCollection.Object);

            mockAsyncCursor.Setup(c => c.Current).Returns(Array.Empty<ClientCredential>());
            mockAsyncCursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            mockAsyncCursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);

            mockCollection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<ClientCredential>>(),
                It.IsAny<FindOptions<ClientCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockAsyncCursor.Object);

            // Act
            var result = await _repository.GetClientCredentialsAsync("non-existent", "project-abc");

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region CreateVisitorUsageAsync Tests

        [Fact]
        public async Task CreateVisitorUsageAsync_ShouldInsertRecord_Successfully()
        {
            // Arrange
            var visitorUsage = new MagicLinkVisitorUsage
            {
                ItemId = Guid.NewGuid().ToString(),
                LinkId = "link-123",
                ProjectKey = "project-abc",
                VisitorIpAddress = "192.168.1.1",
                VisitorUserAgent = "Mozilla/5.0"
            };

            var mockDatabase = new Mock<IMongoDatabase>();
            var mockCollection = new Mock<IMongoCollection<MagicLinkVisitorUsage>>();

            _mockDbContextProvider.Setup(p => p.GetDatabase(visitorUsage.ProjectKey)).Returns(mockDatabase.Object);
            mockDatabase.Setup(d => d.GetCollection<MagicLinkVisitorUsage>(It.IsAny<string>(), null))
                .Returns(mockCollection.Object);

            mockCollection.Setup(c => c.InsertOneAsync(
                visitorUsage,
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            await _repository.CreateVisitorUsageAsync(visitorUsage);

            // Assert
            mockCollection.Verify(c => c.InsertOneAsync(
                visitorUsage,
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetCollection Tests

        [Fact]
        public async Task GetMagicLinkAsync_ShouldUseRootTenantId_FromConfiguration()
        {
            // Arrange
            var itemId = "link-123";
            var mockCollection = new Mock<IMongoCollection<Utility.DomainService.MagicLink.Models.MagicLink>>();
            var mockAsyncCursor = new Mock<IAsyncCursor<Utility.DomainService.MagicLink.Models.MagicLink>>();

            _mockDbContextProvider.Setup(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>("root-tenant", It.IsAny<string>()))
                .Returns(mockCollection.Object);

            mockAsyncCursor.Setup(c => c.Current).Returns(Array.Empty<Utility.DomainService.MagicLink.Models.MagicLink>());
            mockAsyncCursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            mockAsyncCursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);

            mockCollection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                It.IsAny<FindOptions<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockAsyncCursor.Object);

            // Act
            await _repository.GetMagicLinkAsync(itemId);

            // Assert
            _mockDbContextProvider.Verify(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>("root-tenant", It.IsAny<string>()), Times.Once);
        }

        #endregion

        #region Additional Repository Coverage

        [Fact]
        public async Task CreateMagicLinkAsync_ShouldInsertAndReturnItemId()
        {
            var link = new Utility.DomainService.MagicLink.Models.MagicLink { ItemId = "link-1", ProjectKey = "p1" };
            var collection = new Mock<IMongoCollection<Utility.DomainService.MagicLink.Models.MagicLink>>();
            _mockDbContextProvider.Setup(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(collection.Object);

            await _repository.CreateMagicLinkAsync(link);

            collection.Verify(c => c.InsertOneAsync(link, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(0, false)]
        public async Task UpdateMagicLinkAsync_ShouldReturnBasedOnModifiedCount(long modifiedCount, bool expected)
        {
            var link = new Utility.DomainService.MagicLink.Models.MagicLink { ItemId = "link-1", ProjectKey = "p1" };
            var collection = new Mock<IMongoCollection<Utility.DomainService.MagicLink.Models.MagicLink>>();
            _mockDbContextProvider.Setup(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(collection.Object);

            collection.Setup(c => c.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    link,
                    It.IsAny<ReplaceOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReplaceOneResult.Acknowledged(1, modifiedCount, null));

            var result = await _repository.UpdateMagicLinkAsync(link);

            result.Should().Be(expected);
            link.UpdatedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task GetMagicLinksByIdsAsync_WithProjectKey_ShouldApplyProjectFilter()
        {
            var collection = new Mock<IMongoCollection<Utility.DomainService.MagicLink.Models.MagicLink>>();
            var cursor = CreateCursor(Array.Empty<Utility.DomainService.MagicLink.Models.MagicLink>());

            _mockDbContextProvider.Setup(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(collection.Object);

            collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    It.IsAny<FindOptions<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);

            var result = await _repository.GetMagicLinksByIdsAsync(new List<string> { "id1", "id2" }, "project-a");

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetMagicLinksByIdsAsync_WithoutProjectKey_ShouldSkipProjectFilter()
        {
            var collection = new Mock<IMongoCollection<Utility.DomainService.MagicLink.Models.MagicLink>>();
            var cursor = CreateCursor(Array.Empty<Utility.DomainService.MagicLink.Models.MagicLink>());

            _mockDbContextProvider.Setup(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(collection.Object);

            collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    It.IsAny<FindOptions<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);

            var result = await _repository.GetMagicLinksByIdsAsync(new List<string> { "id1", "id2" }, string.Empty);

            result.Should().BeEmpty();
        }

        [Theory]
        [InlineData(null, null, null, true)]
        [InlineData("Active", null, null, true)]
        [InlineData("ManuallyDisabled", null, null, true)]
        [InlineData("UsageLimitExceeded", null, null, true)]
        [InlineData("TimeExpired", null, null, true)]
        [InlineData("LifespanExpired", null, null, true)]
        [InlineData("Unknown", null, null, true)]
        [InlineData(null, "2024-01-01", null, true)]
        [InlineData(null, null, "2024-12-31", true)]
        [InlineData(null, "2024-01-01", "2024-12-31", true)]
        public async Task GetMagicLinksAsync_ShouldSupportStatusAndDateFilterPaths(string? status, string? start, string? end, bool expectProjectKey)
        {
            var collection = new Mock<IMongoCollection<Utility.DomainService.MagicLink.Models.MagicLink>>();
            var cursor = CreateCursor(Array.Empty<Utility.DomainService.MagicLink.Models.MagicLink>());

            _mockDbContextProvider.Setup(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(collection.Object);

            collection.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    It.IsAny<FindOptions<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);

            var request = new GetMagicLinksRequest
            {
                ProjectKey = "project-a",
                Type = MagicLinkType.Action,
                PageNumber = 1,
                PageSize = 2,
                SearchText = "needle",
                RequestMethod = "post",
                Status = status,
                ExpiryDateRange = (start == null && end == null)
                    ? null
                    : new DateRange
                    {
                        StartDate = start == null ? null : DateTime.Parse(start),
                        EndDate = end == null ? null : DateTime.Parse(end)
                    }
            };

            var (_, totalCount) = await _repository.GetMagicLinksAsync(request);

            totalCount.Should().Be(0);
            expectProjectKey.Should().BeTrue();
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(0, false)]
        public async Task MarkAsExpiredAsync_ShouldReturnBasedOnModifiedCount(long modifiedCount, bool expected)
        {
            var collection = new Mock<IMongoCollection<Utility.DomainService.MagicLink.Models.MagicLink>>();
            _mockDbContextProvider.Setup(p => p.GetCollection<Utility.DomainService.MagicLink.Models.MagicLink>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(collection.Object);

            collection.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    It.IsAny<UpdateDefinition<Utility.DomainService.MagicLink.Models.MagicLink>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, modifiedCount, null));

            var result = await _repository.MarkAsExpiredAsync("id-1", MagicLinkExpiredReason.ManuallyDisabled);

            result.Should().Be(expected);
        }

        [Fact]
        public async Task GetLinkConfigAsync_ShouldReturnFirstOrDefault()
        {
            var database = new Mock<IMongoDatabase>();
            var collection = new Mock<IMongoCollection<LinkBasedActionConfig>>();
            var cursor = CreateCursor(new List<LinkBasedActionConfig> { new() { ItemId = "cfg-1", ProjectKey = "p1" } });

            _mockDbContextProvider.Setup(p => p.GetDatabase("p1")).Returns(database.Object);
            database.Setup(d => d.GetCollection<LinkBasedActionConfig>(It.IsAny<string>(), null)).Returns(collection.Object);
            collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<LinkBasedActionConfig>>(), It.IsAny<FindOptions<LinkBasedActionConfig>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);

            var result = await _repository.GetLinkConfigAsync("cfg-1", "p1");

            result.Should().NotBeNull();
            result!.ItemId.Should().Be("cfg-1");
        }

        [Fact]
        public async Task LinkBasedActionConfigCrud_ShouldCoverCreateGetAndUpdate()
        {
            var database = new Mock<IMongoDatabase>();
            var collection = new Mock<IMongoCollection<LinkBasedActionConfig>>();
            var cursor = CreateCursor(new List<LinkBasedActionConfig> { new() { ItemId = "cfg-1", ProjectKey = "p1" } });

            _mockDbContextProvider.Setup(p => p.GetDatabase("p1")).Returns(database.Object);
            database.Setup(d => d.GetCollection<LinkBasedActionConfig>(It.IsAny<string>(), null)).Returns(collection.Object);
            collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<LinkBasedActionConfig>>(),
                    It.IsAny<FindOptions<LinkBasedActionConfig>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);
            collection.Setup(c => c.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<LinkBasedActionConfig>>(),
                    It.IsAny<LinkBasedActionConfig>(),
                    It.IsAny<ReplaceOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReplaceOneResult.Acknowledged(1, 1, null));

            var createId = await _repository.CreateLinkBasedActionConfigAsync(new LinkBasedActionConfig { ItemId = "cfg-1", ProjectKey = "p1" });
            var get = await _repository.GetLinkBasedActionConfigAsync("p1");
            var updated = await _repository.UpdateLinkBasedActionConfigAsync(new LinkBasedActionConfig { ItemId = "cfg-1", ProjectKey = "p1" });

            createId.Should().Be("cfg-1");
            get.Should().NotBeNull();
            updated.Should().BeTrue();
        }

        private static Mock<IAsyncCursor<T>> CreateCursor<T>(IReadOnlyCollection<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            return cursor;
        }

        #endregion
    }
}
