using FluentAssertions;
using Iam.DomainService.Activities;
using Moq;

namespace XUnitTest.Activities
{
    public class UserActivityServiceTests : IDisposable
    {
        private readonly Mock<IUserActivityRepository> _repositoryMock;
        private readonly UserActivityService _userActivityService;

        public UserActivityServiceTests()
        {
            _repositoryMock = new Mock<IUserActivityRepository>();
            _userActivityService = new UserActivityService(_repositoryMock.Object);
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        #region GetHistoriesAsync Tests

        [Fact]
        public async Task GetHistoriesAsync_WithValidRequest_ReturnsHistoriesResponse()
        {
            // Arrange
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = "user-123" }
            };

            var mockHistories = new[] { "history1", "history2", "history3" }.AsQueryable<object>();
            var expectedCount = 15L;

            _repositoryMock
                .Setup(x => x.GetHistorysByUserIdDevicesAsync(request))
                .ReturnsAsync((mockHistories, expectedCount));

            // Act
            var result = await _userActivityService.GetHistoriesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.Should().BeSameAs(mockHistories);
            result.TotalCount.Should().Be(expectedCount);
            _repositoryMock.Verify(x => x.GetHistorysByUserIdDevicesAsync(request), Times.Once);
        }

        [Fact]
        public async Task GetHistoriesAsync_WithEmptyResult_ReturnsEmptyResponse()
        {
            // Arrange
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = "user-no-history" }
            };

            var emptyHistories = Enumerable.Empty<object>().AsQueryable();
            var expectedCount = 0L;

            _repositoryMock
                .Setup(x => x.GetHistorysByUserIdDevicesAsync(request))
                .ReturnsAsync((emptyHistories, expectedCount));

            // Act
            var result = await _userActivityService.GetHistoriesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
            _repositoryMock.Verify(x => x.GetHistorysByUserIdDevicesAsync(request), Times.Once);
        }

        [Fact]
        public async Task GetHistoriesAsync_WithPaginationRequest_PassesRequestCorrectly()
        {
            // Arrange
            var request = new BaseActivityRequest
            {
                Page = 2,
                PageSize = 20,
                Filter = new BaseActivityFilter { UserId = "user-456" }
            };

            var mockHistories = new[] { "history1" }.AsQueryable<object>();
            _repositoryMock
                .Setup(x => x.GetHistorysByUserIdDevicesAsync(request))
                .ReturnsAsync((mockHistories, 50L));

            // Act
            await _userActivityService.GetHistoriesAsync(request);

            // Assert
            _repositoryMock.Verify(x => x.GetHistorysByUserIdDevicesAsync(
                It.Is<BaseActivityRequest>(r => 
                    r.Page == 2 && 
                    r.PageSize == 20 && 
                    r.Filter.UserId == "user-456")), 
                Times.Once);
        }

        [Theory]
        [InlineData("user-1", 10)]
        [InlineData("user-2", 25)]
        [InlineData("user-3", 100)]
        public async Task GetHistoriesAsync_WithDifferentCounts_ReturnsCorrectTotalCount(string userId, long count)
        {
            // Arrange
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var mockHistories = Enumerable.Empty<object>().AsQueryable();
            _repositoryMock
                .Setup(x => x.GetHistorysByUserIdDevicesAsync(request))
                .ReturnsAsync((mockHistories, count));

            // Act
            var result = await _userActivityService.GetHistoriesAsync(request);

            // Assert
            result.TotalCount.Should().Be(count);
        }

        #endregion

        #region GetSessionsAsync Tests

        [Fact]
        public async Task GetSessionsAsync_WithValidRequest_ReturnsSessionsResponse()
        {
            // Arrange
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = "user-789" }
            };

            var mockSessions = new[] { "session1", "session2", "session3", "session4" }.AsQueryable<object>();
            var expectedCount = 20L;

            _repositoryMock
                .Setup(x => x.GetActiveSessionByUserIdDevicesAsync(request))
                .ReturnsAsync((mockSessions, expectedCount));

            // Act
            var result = await _userActivityService.GetSessionsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.Should().BeSameAs(mockSessions);
            result.TotalCount.Should().Be(expectedCount);
            _repositoryMock.Verify(x => x.GetActiveSessionByUserIdDevicesAsync(request), Times.Once);
        }

        [Fact]
        public async Task GetSessionsAsync_WithEmptyResult_ReturnsEmptyResponse()
        {
            // Arrange
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = "user-no-sessions" }
            };

            var emptySessions = Enumerable.Empty<object>().AsQueryable();
            var expectedCount = 0L;

            _repositoryMock
                .Setup(x => x.GetActiveSessionByUserIdDevicesAsync(request))
                .ReturnsAsync((emptySessions, expectedCount));

            // Act
            var result = await _userActivityService.GetSessionsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
            _repositoryMock.Verify(x => x.GetActiveSessionByUserIdDevicesAsync(request), Times.Once);
        }

        [Fact]
        public async Task GetSessionsAsync_WithPaginationRequest_PassesRequestCorrectly()
        {
            // Arrange
            var request = new BaseActivityRequest
            {
                Page = 3,
                PageSize = 15,
                Filter = new BaseActivityFilter { UserId = "user-999" }
            };

            var mockSessions = new[] { "session1" }.AsQueryable<object>();
            _repositoryMock
                .Setup(x => x.GetActiveSessionByUserIdDevicesAsync(request))
                .ReturnsAsync((mockSessions, 100L));

            // Act
            await _userActivityService.GetSessionsAsync(request);

            // Assert
            _repositoryMock.Verify(x => x.GetActiveSessionByUserIdDevicesAsync(
                It.Is<BaseActivityRequest>(r => 
                    r.Page == 3 && 
                    r.PageSize == 15 && 
                    r.Filter.UserId == "user-999")), 
                Times.Once);
        }

        [Theory]
        [InlineData("user-a", 5)]
        [InlineData("user-b", 50)]
        [InlineData("user-c", 500)]
        public async Task GetSessionsAsync_WithDifferentCounts_ReturnsCorrectTotalCount(string userId, long count)
        {
            // Arrange
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = userId }
            };

            var mockSessions = Enumerable.Empty<object>().AsQueryable();
            _repositoryMock
                .Setup(x => x.GetActiveSessionByUserIdDevicesAsync(request))
                .ReturnsAsync((mockSessions, count));

            // Act
            var result = await _userActivityService.GetSessionsAsync(request);

            // Assert
            result.TotalCount.Should().Be(count);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task BothMethods_WithSameRequest_CallDifferentRepositoryMethods()
        {
            // Arrange
            var request = new BaseActivityRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new BaseActivityFilter { UserId = "user-integration" }
            };

            var mockData = new[] { "data1", "data2" }.AsQueryable<object>();
            _repositoryMock
                .Setup(x => x.GetHistorysByUserIdDevicesAsync(request))
                .ReturnsAsync((mockData, 10L));
            _repositoryMock
                .Setup(x => x.GetActiveSessionByUserIdDevicesAsync(request))
                .ReturnsAsync((mockData, 5L));

            // Act
            var historiesResult = await _userActivityService.GetHistoriesAsync(request);
            var sessionsResult = await _userActivityService.GetSessionsAsync(request);

            // Assert
            historiesResult.TotalCount.Should().Be(10L);
            sessionsResult.TotalCount.Should().Be(5L);
            _repositoryMock.Verify(x => x.GetHistorysByUserIdDevicesAsync(request), Times.Once);
            _repositoryMock.Verify(x => x.GetActiveSessionByUserIdDevicesAsync(request), Times.Once);
        }

        #endregion
    }
}
