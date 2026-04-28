using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Services
{
    public class BaseRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProviderMock;
        private readonly TestRepository _repository;

        public BaseRepositoryTests()
        {
            _dbContextProviderMock = new Mock<IDbContextProvider>();
            _repository = new TestRepository(_dbContextProviderMock.Object);
        }

        #region GetCollection (No Parameters) Tests

        [Fact]
        public void GetCollection_WithNoParameters_CallsProviderWithPluralizedTypeName()
        {
            // Arrange
            var expectedCollectionName = "Permissions";
            var mockCollection = new Mock<IMongoCollection<Permission>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<Permission>(expectedCollectionName))
                .Returns(mockCollection.Object);

            // Act
            var result = _repository.GetCollection<Permission>();

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(mockCollection.Object);
            _dbContextProviderMock.Verify(x => x.GetCollection<Permission>(expectedCollectionName), Times.Once);
        }

        [Fact]
        public void GetCollection_WithRoleType_PluralizesCorrectly()
        {
            // Arrange
            var expectedCollectionName = "Roles";
            var mockCollection = new Mock<IMongoCollection<Role>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<Role>(expectedCollectionName))
                .Returns(mockCollection.Object);

            // Act
            var result = _repository.GetCollection<Role>();

            // Assert
            result.Should().NotBeNull();
            _dbContextProviderMock.Verify(x => x.GetCollection<Role>(expectedCollectionName), Times.Once);
        }

        [Fact]
        public void GetCollection_WithUserType_PluralizesCorrectly()
        {
            // Arrange
            var expectedCollectionName = "Users";
            var mockCollection = new Mock<IMongoCollection<User>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<User>(expectedCollectionName))
                .Returns(mockCollection.Object);

            // Act
            var result = _repository.GetCollection<User>();

            // Assert
            result.Should().NotBeNull();
            _dbContextProviderMock.Verify(x => x.GetCollection<User>(expectedCollectionName), Times.Once);
        }

        #endregion

        #region GetCollection (With TenantId) Tests

        [Fact]
        public void GetCollection_WithTenantId_CallsProviderWithTenantIdAndPluralizedTypeName()
        {
            // Arrange
            var tenantId = "tenant-123";
            var expectedCollectionName = "Permissions";
            var mockCollection = new Mock<IMongoCollection<Permission>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<Permission>(tenantId, expectedCollectionName))
                .Returns(mockCollection.Object);

            // Act
            var result = _repository.GetCollection<Permission>(tenantId);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(mockCollection.Object);
            _dbContextProviderMock.Verify(x => x.GetCollection<Permission>(tenantId, expectedCollectionName), Times.Once);
        }

        [Theory]
        [InlineData("tenant-001")]
        [InlineData("tenant-002")]
        [InlineData("multi-tenant-org")]
        public void GetCollection_WithDifferentTenantIds_PassesTenantIdCorrectly(string tenantId)
        {
            // Arrange
            var mockCollection = new Mock<IMongoCollection<Role>>();
            _dbContextProviderMock
                .Setup(x => x.GetCollection<Role>(tenantId, "Roles"))
                .Returns(mockCollection.Object);

            // Act
            var result = _repository.GetCollection<Role>(tenantId);

            // Assert
            result.Should().NotBeNull();
            _dbContextProviderMock.Verify(x => x.GetCollection<Role>(tenantId, "Roles"), Times.Once);
        }

        #endregion

        #region GetCollectionByName Tests

        [Fact]
        public void GetCollectionByName_WithCustomName_CallsProviderWithExactName()
        {
            // Arrange
            var collectionName = "CustomCollectionName";
            var mockCollection = new Mock<IMongoCollection<Permission>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<Permission>(collectionName))
                .Returns(mockCollection.Object);

            // Act
            var result = _repository.GetCollectionByName<Permission>(collectionName);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(mockCollection.Object);
            _dbContextProviderMock.Verify(x => x.GetCollection<Permission>(collectionName), Times.Once);
        }

        [Theory]
        [InlineData("UserTimelines")]
        [InlineData("ResourceTimelines")]
        [InlineData("CustomEntityCollection")]
        public void GetCollectionByName_WithDifferentNames_UsesExactName(string collectionName)
        {
            // Arrange
            var mockCollection = new Mock<IMongoCollection<object>>();
            _dbContextProviderMock
                .Setup(x => x.GetCollection<object>(collectionName))
                .Returns(mockCollection.Object);

            // Act
            var result = _repository.GetCollectionByName<object>(collectionName);

            // Assert
            result.Should().NotBeNull();
            _dbContextProviderMock.Verify(x => x.GetCollection<object>(collectionName), Times.Once);
        }

        #endregion

        #region UpdatePartialAsync Tests

        [Fact]
        public async Task UpdatePartialAsync_WithDefaultCollectionName_UsesPluralizedTypeName()
        {
            // Arrange
            var id = "item-123";
            var updates = new Dictionary<string, object>
            {
                { "Name", "Updated Name" },
                { "Status", "Active" }
            };
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            mockCollection
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<TestEntity>>(),
                    It.IsAny<UpdateDefinition<TestEntity>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, BsonValue.Create(id)));

            // Act
            await _repository.UpdatePartialAsync<TestEntity>(id, updates);

            // Assert
            _dbContextProviderMock.Verify(x => x.GetCollection<TestEntity>("TestEntitys"), Times.Once);
            mockCollection.Verify(x => x.UpdateOneAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<UpdateDefinition<TestEntity>>(),
                It.IsAny<UpdateOptions>(),
                default), Times.Once);
        }

        [Fact]
        public async Task UpdatePartialAsync_WithCustomCollectionName_UsesProvidedName()
        {
            // Arrange
            var id = "item-456";
            var updates = new Dictionary<string, object>
            {
                { "Field1", "Value1" }
            };
            var customCollectionName = "CustomCollection";
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<TestEntity>(customCollectionName))
                .Returns(mockCollection.Object);

            mockCollection
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<TestEntity>>(),
                    It.IsAny<UpdateDefinition<TestEntity>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, BsonValue.Create(id)));

            // Act
            await _repository.UpdatePartialAsync<TestEntity>(id, updates, customCollectionName);

            // Assert
            _dbContextProviderMock.Verify(x => x.GetCollection<TestEntity>(customCollectionName), Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task UpdatePartialAsync_WithEmptyOrWhitespaceCollectionName_UsesPluralizedTypeName(string collectionName)
        {
            // Arrange
            var id = "item-789";
            var updates = new Dictionary<string, object> { { "Field", "Value" } };
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            mockCollection
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<TestEntity>>(),
                    It.IsAny<UpdateDefinition<TestEntity>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, BsonValue.Create(id)));

            // Act
            await _repository.UpdatePartialAsync<TestEntity>(id, updates, collectionName);

            // Assert
            _dbContextProviderMock.Verify(x => x.GetCollection<TestEntity>("TestEntitys"), Times.Once);
        }

        [Fact]
        public async Task UpdatePartialAsync_WithMultipleUpdates_CombinesAllUpdates()
        {
            // Arrange
            var id = "item-multi";
            var updates = new Dictionary<string, object>
            {
                { "Field1", "Value1" },
                { "Field2", 42 },
                { "Field3", true },
                { "Field4", DateTime.UtcNow }
            };
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            UpdateDefinition<TestEntity> capturedUpdate = null;

            _dbContextProviderMock
                .Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            mockCollection
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<TestEntity>>(),
                    It.IsAny<UpdateDefinition<TestEntity>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .Callback<FilterDefinition<TestEntity>, UpdateDefinition<TestEntity>, UpdateOptions, CancellationToken>(
                    (filter, update, options, token) => capturedUpdate = update)
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, BsonValue.Create(id)));

            // Act
            await _repository.UpdatePartialAsync<TestEntity>(id, updates);

            // Assert
            capturedUpdate.Should().NotBeNull();
            mockCollection.Verify(x => x.UpdateOneAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<UpdateDefinition<TestEntity>>(),
                It.IsAny<UpdateOptions>(),
                default), Times.Once);
        }

        [Fact]
        public async Task UpdatePartialAsync_WithEmptyUpdates_StillCallsUpdateOne()
        {
            // Arrange
            var id = "item-empty";
            var updates = new Dictionary<string, object>();
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            mockCollection
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<TestEntity>>(),
                    It.IsAny<UpdateDefinition<TestEntity>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 0, null));

            // Act
            await _repository.UpdatePartialAsync<TestEntity>(id, updates);

            // Assert
            mockCollection.Verify(x => x.UpdateOneAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<UpdateDefinition<TestEntity>>(),
                It.IsAny<UpdateOptions>(),
                default), Times.Once);
        }

        [Theory]
        [InlineData("id-1", 1)]
        [InlineData("id-2", 3)]
        [InlineData("id-3", 5)]
        public async Task UpdatePartialAsync_WithDifferentUpdateCounts_WorksCorrectly(string id, int updateCount)
        {
            // Arrange
            var updates = new Dictionary<string, object>();
            for (int i = 0; i < updateCount; i++)
            {
                updates[$"Field{i}"] = $"Value{i}";
            }

            var mockCollection = new Mock<IMongoCollection<TestEntity>>();

            _dbContextProviderMock
                .Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            mockCollection
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<TestEntity>>(),
                    It.IsAny<UpdateDefinition<TestEntity>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, BsonValue.Create(id)));

            // Act
            await _repository.UpdatePartialAsync<TestEntity>(id, updates);

            // Assert
            mockCollection.Verify(x => x.UpdateOneAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<UpdateDefinition<TestEntity>>(),
                It.IsAny<UpdateOptions>(),
                default), Times.Once);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task AllMethods_WithSameType_UseConsistentCollectionNaming()
        {
            // Arrange
            var mockCollection = new Mock<IMongoCollection<Permission>>();
            var expectedCollectionName = "Permissions";

            _dbContextProviderMock
                .Setup(x => x.GetCollection<Permission>(expectedCollectionName))
                .Returns(mockCollection.Object);

            _dbContextProviderMock
                .Setup(x => x.GetCollection<Permission>("tenant-1", expectedCollectionName))
                .Returns(mockCollection.Object);

            mockCollection
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<UpdateDefinition<Permission>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            // Act
            _repository.GetCollection<Permission>();
            _repository.GetCollection<Permission>("tenant-1");
            _repository.GetCollectionByName<Permission>(expectedCollectionName);
            await _repository.UpdatePartialAsync<Permission>("id-1", new Dictionary<string, object> { { "Name", "Test" } });

            // Assert
            // GetCollection() is called 3 times with same collection name:
            // 1. GetCollection<Permission>()
            // 2. GetCollectionByName<Permission>("Permissions")
            // 3. UpdatePartialAsync<Permission>() (internally calls GetCollection)
            _dbContextProviderMock.Verify(x => x.GetCollection<Permission>(expectedCollectionName), Times.Exactly(3));
            _dbContextProviderMock.Verify(x => x.GetCollection<Permission>("tenant-1", expectedCollectionName), Times.Once);
        }

        #endregion

        #region Test Helper Classes

        // Concrete implementation for testing abstract repository
        public class TestRepository : BaseRepository
        {
            public TestRepository(IDbContextProvider dbContextProvider) : base(dbContextProvider)
            {
            }

            // Expose protected methods as public for testing
            public new IMongoCollection<T> GetCollection<T>()
            {
                return base.GetCollection<T>();
            }

            public new IMongoCollection<T> GetCollection<T>(string tenantId)
            {
                return base.GetCollection<T>(tenantId);
            }

            public new IMongoCollection<T> GetCollectionByName<T>(string collectionName)
            {
                return base.GetCollectionByName<T>(collectionName);
            }

            public new Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "")
            {
                return base.UpdatePartialAsync<T>(id, updates, collectionName);
            }
        }

        public class TestEntity
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Status { get; set; }
        }

        #endregion
    }
}
