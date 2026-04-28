using Blocks.Genesis;
using FluentAssertions;
using Mfa.DomainService.Services;
using MongoDB.Driver;
using Moq;
using System.Linq.Expressions;

namespace XUnitTest.DomainService.Mfa
{
    public class MfaManagementRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider;
        private readonly MfaManagementRepository _repository;

        public MfaManagementRepositoryTests()
        {
            _dbContextProvider = new Mock<IDbContextProvider>();
            _repository = new MfaManagementRepository(_dbContextProvider.Object);
        }

        #region DeleteItemsAsync

        [Fact]
        public async Task DeleteItemsAsync_WithValidFilter_DeletesItems()
        {
            // Arrange
            Expression<Func<TestEntity, bool>> filter = x => x.Id == "test-id";
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            var deleteResult = new DeleteResult.Acknowledged(1);

            mockCollection.Setup(x => x.DeleteManyAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(deleteResult);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.DeleteItemsAsync(filter);

            // Assert
            mockCollection.Verify(x => x.DeleteManyAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _dbContextProvider.Verify(x => x.GetCollection<TestEntity>("TestEntitys"), Times.Once);
        }

        #endregion

        #region GetItemsAsync

        [Fact]
        public async Task GetItemsAsync_WithValidFilter_ReturnsItems()
        {
            // Arrange
            Expression<Func<TestEntity, bool>> filter = x => x.Name == "test";
            var expectedItems = new List<TestEntity>
            {
                new TestEntity { Id = "1", Name = "test" },
                new TestEntity { Id = "2", Name = "test" }
            };
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            var mockCursor = new Mock<IAsyncCursor<TestEntity>>();

            mockCursor.Setup(x => x.Current).Returns(expectedItems);
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<FindOptions<TestEntity>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetItemsAsync(filter);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result.Should().BeEquivalentTo(expectedItems);
        }

        [Fact]
        public async Task GetItemsAsync_WithCustomCollectionName_UsesCustomCollection()
        {
            // Arrange
            var customCollection = "CustomCollection";
            Expression<Func<TestEntity, bool>> filter = x => x.Name == "test";
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            var mockCursor = new Mock<IAsyncCursor<TestEntity>>();

            mockCursor.Setup(x => x.Current).Returns(new List<TestEntity>());
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<FindOptions<TestEntity>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>(customCollection))
                .Returns(mockCollection.Object);

            // Act
            await _repository.GetItemsAsync(filter, customCollection);

            // Assert
            _dbContextProvider.Verify(x => x.GetCollection<TestEntity>(customCollection), Times.Once);
        }

        [Fact]
        public async Task GetItemsAsync_WithEmptyCollectionName_UsesDefaultCollection()
        {
            // Arrange
            Expression<Func<TestEntity, bool>> filter = x => x.Name == "test";
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            var mockCursor = new Mock<IAsyncCursor<TestEntity>>();

            mockCursor.Setup(x => x.Current).Returns(new List<TestEntity>());
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<FindOptions<TestEntity>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.GetItemsAsync(filter, "");

            // Assert
            _dbContextProvider.Verify(x => x.GetCollection<TestEntity>("TestEntitys"), Times.Once);
        }

        #endregion

        #region GetItemAsync

        [Fact]
        public async Task GetItemAsync_WithValidFilter_ReturnsItem()
        {
            // Arrange
            Expression<Func<TestEntity, bool>> filter = x => x.Id == "test-id";
            var expectedItem = new TestEntity { Id = "test-id", Name = "test" };
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            var mockCursor = new Mock<IAsyncCursor<TestEntity>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedItem });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<FindOptions<TestEntity>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetItemAsync(filter);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be("test-id");
            result.Name.Should().Be("test");
        }

        [Fact]
        public async Task GetItemAsync_WithCustomCollectionName_UsesCustomCollection()
        {
            // Arrange
            var customCollection = "CustomCollection";
            Expression<Func<TestEntity, bool>> filter = x => x.Id == "test-id";
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            var mockCursor = new Mock<IAsyncCursor<TestEntity>>();

            mockCursor.Setup(x => x.Current).Returns(new List<TestEntity>());
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<FindOptions<TestEntity>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>(customCollection))
                .Returns(mockCollection.Object);

            // Act
            await _repository.GetItemAsync(filter, customCollection);

            // Assert
            _dbContextProvider.Verify(x => x.GetCollection<TestEntity>(customCollection), Times.Once);
        }

        [Fact]
        public async Task GetItemAsync_WithNoMatch_ReturnsNull()
        {
            // Arrange
            Expression<Func<TestEntity, bool>> filter = x => x.Id == "nonexistent";
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            var mockCursor = new Mock<IAsyncCursor<TestEntity>>();

            mockCursor.Setup(x => x.Current).Returns(new List<TestEntity>());
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<FindOptions<TestEntity>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetItemAsync(filter);

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region SaveAsync (Single Item)

        [Fact]
        public async Task SaveAsync_WithSingleItem_InsertsItem()
        {
            // Arrange
            var testEntity = new TestEntity { Id = "test-id", Name = "test" };
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();

            mockCollection.Setup(x => x.InsertOneAsync(
                It.IsAny<TestEntity>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.SaveAsync(testEntity);

            // Assert
            mockCollection.Verify(x => x.InsertOneAsync(
                testEntity,
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveAsync_WithSingleItemAndCustomCollection_UsesCustomCollection()
        {
            // Arrange
            var customCollection = "CustomCollection";
            var testEntity = new TestEntity { Id = "test-id", Name = "test" };
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();

            mockCollection.Setup(x => x.InsertOneAsync(
                It.IsAny<TestEntity>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>(customCollection))
                .Returns(mockCollection.Object);

            // Act
            await _repository.SaveAsync(testEntity, customCollection);

            // Assert
            _dbContextProvider.Verify(x => x.GetCollection<TestEntity>(customCollection), Times.Once);
        }

        #endregion

        #region SaveAsync (List)

        [Fact]
        public async Task SaveAsync_WithList_InsertsAllItems()
        {
            // Arrange
            var testEntities = new List<TestEntity>
            {
                new TestEntity { Id = "1", Name = "test1" },
                new TestEntity { Id = "2", Name = "test2" }
            };
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();

            mockCollection.Setup(x => x.InsertManyAsync(
                It.IsAny<IEnumerable<TestEntity>>(),
                It.IsAny<InsertManyOptions>(),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.SaveAsync(testEntities);

            // Assert
            mockCollection.Verify(x => x.InsertManyAsync(
                testEntities,
                It.IsAny<InsertManyOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region UpsertAsync

        [Fact]
        public async Task UpsertAsync_WithValidData_UpsertsItem()
        {
            // Arrange
            var testEntity = new TestEntity { Id = "test-id", Name = "test" };
            Expression<Func<TestEntity, bool>> filter = x => x.Id == "test-id";
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, null);

            mockCollection.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<TestEntity>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(replaceResult);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>("TestEntitys"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.UpsertAsync(testEntity, filter);

            // Assert
            mockCollection.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                testEntity,
                It.Is<ReplaceOptions>(o => o.IsUpsert == true),
                It.IsAny<CancellationToken>()), Times.Once);
            _dbContextProvider.Verify(x => x.GetCollection<TestEntity>("TestEntitys"), Times.Once);
        }

        [Fact]
        public async Task UpsertAsync_WithCustomCollectionName_UsesCustomCollection()
        {
            // Arrange
            var customCollection = "CustomCollection";
            var testEntity = new TestEntity { Id = "test-id", Name = "test" };
            Expression<Func<TestEntity, bool>> filter = x => x.Id == "test-id";
            var mockCollection = new Mock<IMongoCollection<TestEntity>>();
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, null);

            mockCollection.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                It.IsAny<TestEntity>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(replaceResult);

            _dbContextProvider.Setup(x => x.GetCollection<TestEntity>(customCollection))
                .Returns(mockCollection.Object);

            // Act
            await _repository.UpsertAsync(testEntity, filter, customCollection);

            // Assert
            _dbContextProvider.Verify(x => x.GetCollection<TestEntity>(customCollection), Times.Once);
            mockCollection.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<TestEntity>>(),
                testEntity,
                It.Is<ReplaceOptions>(o => o.IsUpsert == true),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Test Helper Class

        public class TestEntity
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }

        #endregion
    }
}
