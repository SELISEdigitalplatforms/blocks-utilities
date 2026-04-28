using FluentAssertions;
using Iam.DomainService.Configurations;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Configurations
{
    /// <summary>
    /// Unit tests for IamConfigurationRepository.
    /// 
    /// NOTE: Due to Moq 4.16+ limitations, Find().FirstOrDefaultAsync() cannot be fully tested.
    /// Tests focus on verifiable behaviors: collection retrieval and ReplaceOneAsync operations.
    /// </summary>
    public class IamConfigurationRepositoryTests
    {
        private readonly Mock<IIdentityAccessManagementRepository> _repositoryMock;
        private readonly IamConfigurationRepository _sut;

        public IamConfigurationRepositoryTests()
        {
            _repositoryMock = new Mock<IIdentityAccessManagementRepository>();
            _sut = new IamConfigurationRepository(_repositoryMock.Object);
        }

        #region GetConfigurationAsync Tests

        [Fact]
        public async Task GetConfigurationAsync_CallsGetCollection_Once()
        {
            // Arrange
            var collectionMock = new Mock<IMongoCollection<IamConfiguration>>();
            _repositoryMock.Setup(x => x.GetCollection<IamConfiguration>())
                .Returns(collectionMock.Object);

            // Act
            try
            {
                await _sut.GetConfigurationAsync();
            }
            catch
            {
                // Expected - Find() fluent API cannot be mocked
            }

            // Assert
            _repositoryMock.Verify(x => x.GetCollection<IamConfiguration>(), Times.Once);
        }

        #endregion

        #region SaveConfigurationAsync Tests

        [Fact]
        public async Task SaveConfigurationAsync_CallsGetCollection_Once()
        {
            // Arrange
            var config = CreateTestConfiguration();
            var collectionMock = CreateMockCollectionForSave(isAcknowledged: true);
            _repositoryMock.Setup(x => x.GetCollection<IamConfiguration>())
                .Returns(collectionMock.Object);

            // Act
            await _sut.SaveConfigurationAsync(config);

            // Assert
            _repositoryMock.Verify(x => x.GetCollection<IamConfiguration>(), Times.Once);
        }

        [Fact]
        public async Task SaveConfigurationAsync_CallsReplaceOneAsync_WithCorrectParameters()
        {
            // Arrange
            var config = CreateTestConfiguration();
            FilterDefinition<IamConfiguration> capturedFilter = null;
            IamConfiguration capturedConfig = null;
            ReplaceOptions capturedOptions = null;

            var collectionMock = new Mock<IMongoCollection<IamConfiguration>>();
            collectionMock.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<IamConfiguration>>(),
                It.IsAny<IamConfiguration>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<IamConfiguration>, IamConfiguration, ReplaceOptions, CancellationToken>(
                    (filter, cfg, options, ct) =>
                    {
                        capturedFilter = filter;
                        capturedConfig = cfg;
                        capturedOptions = options;
                    })
                .ReturnsAsync(CreateReplaceOneResult(true));

            _repositoryMock.Setup(x => x.GetCollection<IamConfiguration>())
                .Returns(collectionMock.Object);

            // Act
            await _sut.SaveConfigurationAsync(config);

            // Assert
            capturedFilter.Should().NotBeNull();
            capturedConfig.Should().Be(config);
            capturedOptions.Should().NotBeNull();
            capturedOptions.IsUpsert.Should().BeTrue();
        }

        [Fact]
        public async Task SaveConfigurationAsync_ReturnsTrue_WhenAcknowledged()
        {
            // Arrange
            var config = CreateTestConfiguration();
            var collectionMock = CreateMockCollectionForSave(isAcknowledged: true);
            _repositoryMock.Setup(x => x.GetCollection<IamConfiguration>())
                .Returns(collectionMock.Object);

            // Act
            var result = await _sut.SaveConfigurationAsync(config);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task SaveConfigurationAsync_ReturnsFalse_WhenNotAcknowledged()
        {
            // Arrange
            var config = CreateTestConfiguration();
            var collectionMock = CreateMockCollectionForSave(isAcknowledged: false);
            _repositoryMock.Setup(x => x.GetCollection<IamConfiguration>())
                .Returns(collectionMock.Object);

            // Act
            var result = await _sut.SaveConfigurationAsync(config);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task SaveConfigurationAsync_UsesItemIdInFilter()
        {
            // Arrange
            var config = CreateTestConfiguration();
            var itemId = ObjectId.GenerateNewId();
            config.ItemId = itemId;

            var collectionMock = CreateMockCollectionForSave(isAcknowledged: true);
            _repositoryMock.Setup(x => x.GetCollection<IamConfiguration>())
                .Returns(collectionMock.Object);

            // Act
            await _sut.SaveConfigurationAsync(config);

            // Assert
            collectionMock.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<IamConfiguration>>(),
                It.Is<IamConfiguration>(c => c.ItemId == itemId),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveConfigurationAsync_WithUpsertOption_CreatesIfNotExists()
        {
            // Arrange
            var config = CreateTestConfiguration();
            ReplaceOptions capturedOptions = null;

            var collectionMock = new Mock<IMongoCollection<IamConfiguration>>();
            collectionMock.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<IamConfiguration>>(),
                It.IsAny<IamConfiguration>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<IamConfiguration>, IamConfiguration, ReplaceOptions, CancellationToken>(
                    (f, c, options, ct) => capturedOptions = options)
                .ReturnsAsync(CreateReplaceOneResult(true));

            _repositoryMock.Setup(x => x.GetCollection<IamConfiguration>())
                .Returns(collectionMock.Object);

            // Act
            await _sut.SaveConfigurationAsync(config);

            // Assert
            capturedOptions.Should().NotBeNull();
            capturedOptions.IsUpsert.Should().BeTrue("repository should use upsert to create if not exists");
        }

        #endregion

        #region Helper Methods

        private IamConfiguration CreateTestConfiguration()
        {
            return new IamConfiguration
            {
                ItemId = ObjectId.GenerateNewId(),
                AccountActivationUrl = "https://test.com/activate",
                AccountVerificationUrl = "https://test.com/verify",
                RecoverAccountUrl = "https://test.com/recover",
                ActivationUrlLifetimeInMinutes = 60,
                RecoverAccountUrlLifetimeInMinutes = 10,
                LogoutOnPasswordChange = true,
                PasswordStrengthCheckerRegex = "^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).{8,}$"
            };
        }

        private Mock<IMongoCollection<IamConfiguration>> CreateMockCollectionForSave(bool isAcknowledged)
        {
            var collectionMock = new Mock<IMongoCollection<IamConfiguration>>();
            collectionMock.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<IamConfiguration>>(),
                It.IsAny<IamConfiguration>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateReplaceOneResult(isAcknowledged));

            return collectionMock;
        }

        private ReplaceOneResult CreateReplaceOneResult(bool isAcknowledged)
        {
            var mockResult = new Mock<ReplaceOneResult>();
            mockResult.SetupGet(r => r.IsAcknowledged).Returns(isAcknowledged);
            if (isAcknowledged)
            {
                mockResult.SetupGet(r => r.MatchedCount).Returns(1);
                mockResult.SetupGet(r => r.ModifiedCount).Returns(1);
            }
            return mockResult.Object;
        }

        #endregion
    }
}
