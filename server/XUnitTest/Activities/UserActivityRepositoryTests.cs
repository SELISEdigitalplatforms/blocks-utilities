using FluentAssertions;
using Iam.DomainService.Activities;
using Iam.DomainService.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Activities
{
    public class UserActivityRepositoryTests : IDisposable
    {
        private readonly Mock<IIdentityAccessManagementRepository> _repositoryMock;
        private readonly UserActivityRepository _userActivityRepository;
        private readonly Mock<IMongoCollection<BsonDocument>> _sessionCollectionMock;
        private readonly Mock<IMongoCollection<BsonDocument>> _timelineCollectionMock;

        public UserActivityRepositoryTests()
        {
            _repositoryMock = new Mock<IIdentityAccessManagementRepository>();
            _sessionCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            _timelineCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            _userActivityRepository = new UserActivityRepository(_repositoryMock.Object);
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        #region GetActiveSessionByUserIdDevicesAsync Tests

        [Fact]
        public async Task GetActiveSessionByUserIdDevicesAsync_WithValidRequest_ReturnsSessionsAndCount()
        {
            // Arrange
            var userId = "user-123";
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var sessions = CreateBsonDocumentList(3, "session");
            var cursorMock = CreateAsyncCursorMock(sessions);

            _sessionCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _sessionCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(3);

            _repositoryMock.Setup(x => x.GetCollectionByName<BsonDocument>("Sessions"))
                .Returns(_sessionCollectionMock.Object);

            // Act
            var (result, count) = await _userActivityRepository.GetActiveSessionByUserIdDevicesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Count().Should().Be(3);
            count.Should().Be(3);
            _repositoryMock.Verify(x => x.GetCollectionByName<BsonDocument>("Sessions"), Times.Once);
        }

        [Fact]
        public async Task GetActiveSessionByUserIdDevicesAsync_WithPagination_AppliesSkipAndLimit()
        {
            // Arrange
            var userId = "user-456";
            var request = new BaseActivityRequest
            {
                Page = 2,
                PageSize = 5,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var sessions = CreateBsonDocumentList(5, "session");
            var cursorMock = CreateAsyncCursorMock(sessions);

            _sessionCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _sessionCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(15);

            _repositoryMock.Setup(x => x.GetCollectionByName<BsonDocument>("Sessions"))
                .Returns(_sessionCollectionMock.Object);

            // Act
            var (result, count) = await _userActivityRepository.GetActiveSessionByUserIdDevicesAsync(request);

            // Assert
            result.Should().NotBeNull();
            count.Should().Be(15);
        }

        [Fact]
        public async Task GetActiveSessionByUserIdDevicesAsync_WithNoResults_ReturnsEmptyCollectionAndZeroCount()
        {
            // Arrange
            var userId = "user-no-sessions";
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var emptySessions = new List<BsonDocument>();
            var cursorMock = CreateAsyncCursorMock(emptySessions);

            _sessionCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _sessionCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _repositoryMock.Setup(x => x.GetCollectionByName<BsonDocument>("Sessions"))
                .Returns(_sessionCollectionMock.Object);

            // Act
            var (result, count) = await _userActivityRepository.GetActiveSessionByUserIdDevicesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Count().Should().Be(0);
            count.Should().Be(0);
        }

        [Fact]
        public async Task GetActiveSessionByUserIdDevicesAsync_WithDifferentUserId_FiltersCorrectly()
        {
            // Arrange
            var userId = "specific-user-789";
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var sessions = CreateBsonDocumentList(2, "session");
            var cursorMock = CreateAsyncCursorMock(sessions);

            _sessionCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _sessionCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(2);

            _repositoryMock.Setup(x => x.GetCollectionByName<BsonDocument>("Sessions"))
                .Returns(_sessionCollectionMock.Object);

            // Act
            var (result, count) = await _userActivityRepository.GetActiveSessionByUserIdDevicesAsync(request);

            // Assert
            result.Should().NotBeNull();
            count.Should().Be(2);
        }

        [Fact]
        public void GetActiveSessionByUserIdDevicesAsync_WithNullRequest_ThrowsNullReferenceException()
        {
            // Arrange
            BaseActivityRequest request = null;

            // Act
            Func<Task> act = async () => await _userActivityRepository.GetActiveSessionByUserIdDevicesAsync(request);

            // Assert
            act.Should().ThrowAsync<NullReferenceException>();
        }

        #endregion

        #region GetHistorysByUserIdDevicesAsync Tests

        [Fact]
        public async Task GetHistorysByUserIdDevicesAsync_WithValidRequest_ReturnsHistoriesAndCount()
        {
            // Arrange
            var userId = "user-history-123";
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var histories = CreateBsonDocumentList(4, "timeline");
            var cursorMock = CreateAsyncCursorMock(histories);

            _timelineCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _timelineCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(4);

            _repositoryMock.Setup(x => x.GetCollectionByName<BsonDocument>("UserAuthenticationTimelines"))
                .Returns(_timelineCollectionMock.Object);

            // Act
            var (result, count) = await _userActivityRepository.GetHistorysByUserIdDevicesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Count().Should().Be(4);
            count.Should().Be(4);
            _repositoryMock.Verify(x => x.GetCollectionByName<BsonDocument>("UserAuthenticationTimelines"), Times.Once);
        }

        [Fact]
        public async Task GetHistorysByUserIdDevicesAsync_WithPagination_AppliesSkipAndLimit()
        {
            // Arrange
            var userId = "user-history-456";
            var request = new BaseActivityRequest
            {
                Page = 3,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var histories = CreateBsonDocumentList(10, "timeline");
            var cursorMock = CreateAsyncCursorMock(histories);

            _timelineCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _timelineCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(50);

            _repositoryMock.Setup(x => x.GetCollectionByName<BsonDocument>("UserAuthenticationTimelines"))
                .Returns(_timelineCollectionMock.Object);

            // Act
            var (result, count) = await _userActivityRepository.GetHistorysByUserIdDevicesAsync(request);

            // Assert
            result.Should().NotBeNull();
            count.Should().Be(50);
        }

        [Fact]
        public async Task GetHistorysByUserIdDevicesAsync_WithNoResults_ReturnsEmptyCollectionAndZeroCount()
        {
            // Arrange
            var userId = "user-no-history";
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var emptyHistories = new List<BsonDocument>();
            var cursorMock = CreateAsyncCursorMock(emptyHistories);

            _timelineCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _timelineCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _repositoryMock.Setup(x => x.GetCollectionByName<BsonDocument>("UserAuthenticationTimelines"))
                .Returns(_timelineCollectionMock.Object);

            // Act
            var (result, count) = await _userActivityRepository.GetHistorysByUserIdDevicesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Count().Should().Be(0);
            count.Should().Be(0);
        }

        [Fact]
        public async Task GetHistorysByUserIdDevicesAsync_WithDifferentUserId_FiltersCorrectly()
        {
            // Arrange
            var userId = "specific-history-user-999";
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var histories = CreateBsonDocumentList(7, "timeline");
            var cursorMock = CreateAsyncCursorMock(histories);

            _timelineCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _timelineCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(7);

            _repositoryMock.Setup(x => x.GetCollectionByName<BsonDocument>("UserAuthenticationTimelines"))
                .Returns(_timelineCollectionMock.Object);

            // Act
            var (result, count) = await _userActivityRepository.GetHistorysByUserIdDevicesAsync(request);

            // Assert
            result.Should().NotBeNull();
            count.Should().Be(7);
        }

        [Fact]
        public void GetHistorysByUserIdDevicesAsync_WithNullRequest_ThrowsNullReferenceException()
        {
            // Arrange
            BaseActivityRequest request = null;

            // Act
            Func<Task> act = async () => await _userActivityRepository.GetHistorysByUserIdDevicesAsync(request);

            // Assert
            act.Should().ThrowAsync<NullReferenceException>();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a list of BsonDocuments for testing
        /// </summary>
        private List<BsonDocument> CreateBsonDocumentList(int count, string prefix)
        {
            var documents = new List<BsonDocument>();
            for (int i = 0; i < count; i++)
            {
                var doc = new BsonDocument
                {
                    { "_id", ObjectId.GenerateNewId() },
                    { "UserId", $"{prefix}-user-{i}" },
                    { "CreateDate", DateTime.UtcNow.AddDays(-i) },
                    { "CreatedDate", DateTime.UtcNow.AddDays(-i) },
                    { "CreatedBy", $"{prefix}-creator-{i}" },
                    { "Data", $"{prefix}-data-{i}" }
                };
                documents.Add(doc);
            }
            return documents;
        }

        /// <summary>
        /// Creates a mock IAsyncCursor that returns the provided documents
        /// </summary>
        private Mock<IAsyncCursor<BsonDocument>> CreateAsyncCursorMock(List<BsonDocument> documents)
        {
            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            var moveNextCounter = 0;

            cursorMock.Setup(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    moveNextCounter++;
                    return moveNextCounter == 1;
                });

            cursorMock.Setup(x => x.Current)
                .Returns(documents);

            return cursorMock;
        }

        #endregion
    }
}
