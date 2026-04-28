using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Users
{
    public class UserManagementQueryServiceTests : IDisposable
    {
        private readonly Mock<ILogger<UserManagementQueryService>> _loggerMock;
        private readonly Mock<IUserRepository> _userRepositoryMock;
        private readonly UserManagementQueryService _service;

        public UserManagementQueryServiceTests()
        {
            _loggerMock = new Mock<ILogger<UserManagementQueryService>>();
            _userRepositoryMock = new Mock<IUserRepository>();
            _service = new UserManagementQueryService(_loggerMock.Object, _userRepositoryMock.Object);
        }

        public void Dispose()
        {
            BlocksContext.ClearContext();
        }

        private static void SetupBlocksContext(string userId = "user-123", string tenantId = "tenant-123", string orgId = "org-123")
        {
            var createMethods = typeof(BlocksContext).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .ToList();

            var create15Method = createMethods.FirstOrDefault(m => m.GetParameters().Length == 15);

            if (create15Method != null)
            {
                var context = (BlocksContext)create15Method.Invoke(null, new object[]
                {
                    tenantId, Array.Empty<string>(), userId, true, string.Empty, orgId,
                    DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                    "testuser", string.Empty, "Test User", string.Empty, tenantId, string.Empty
                });
                BlocksContext.SetContext(context, true);
            }
            else
            {
                var create14Method = createMethods.FirstOrDefault(m => m.GetParameters().Length == 14);

                if (create14Method != null)
                {
                    var context = (BlocksContext)create14Method.Invoke(null, new object[]
                    {
                        tenantId, Array.Empty<string>(), userId, true, string.Empty, string.Empty,
                        DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                        "testuser", string.Empty, "Test User", string.Empty, tenantId
                    });
                    BlocksContext.SetContext(context, true);
                }
            }
        }

        #region GetAccountsAsync Tests

        [Fact]
        public async Task GetAccountsAsync_WithValidRequest_ReturnsAccountsResponse()
        {
            // Arrange
            var request = new GetAccountsRequest
            {
                Page = 0,
                PageSize = 10
            };
            var accounts = new[] { new GetAccounts { ItemId = "acc-1" } }.AsQueryable();
            var totalCount = 15L;

            _userRepositoryMock.Setup(x => x.GetUsersAsync<GetAccounts, GetAccountsRequest>(request))
                .ReturnsAsync((accounts, totalCount));

            // Act
            var result = await _service.GetAccountsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeSameAs(accounts);
            result.TotalCount.Should().Be(totalCount);
            _userRepositoryMock.Verify(x => x.GetUsersAsync<GetAccounts, GetAccountsRequest>(request), Times.Once);
        }

        [Fact]
        public async Task GetAccountsAsync_LogsInformationMessages()
        {
            // Arrange
            var request = new GetAccountsRequest();
            _userRepositoryMock.Setup(x => x.GetUsersAsync<GetAccounts, GetAccountsRequest>(request))
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            // Act
            await _service.GetAccountsAsync(request);

            // Assert
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Accounts get start")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Accounts get end")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        [Theory]
        [InlineData(0, 10, 5L)]
        [InlineData(1, 20, 100L)]
        [InlineData(5, 50, 0L)]
        public async Task GetAccountsAsync_WithDifferentPagination_ReturnsCorrectly(int page, int pageSize, long totalCount)
        {
            // Arrange
            var request = new GetAccountsRequest { Page = page, PageSize = pageSize };
            var accounts = Enumerable.Empty<GetAccounts>().AsQueryable();
            _userRepositoryMock.Setup(x => x.GetUsersAsync<GetAccounts, GetAccountsRequest>(request))
                .ReturnsAsync((accounts, totalCount));

            // Act
            var result = await _service.GetAccountsAsync(request);

            // Assert
            result.TotalCount.Should().Be(totalCount);
        }

        #endregion

        #region GetAccountAsync Tests

        [Fact]
        public async Task GetAccountAsync_WithContextUserId_ReturnsCurrentUserAccount()
        {
            // Arrange
            var userId = "context-user-123";
            SetupBlocksContext(userId);
            var user = new GetUser { ItemId = userId, Email = "user@example.com" };
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync<GetUser>(userId))
                .ReturnsAsync(user);

            // Act
            var result = await _service.GetAccountAsync();

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeSameAs(user);
            result.Data.ItemId.Should().Be(userId);
            _userRepositoryMock.Verify(x => x.GetUserByIdAsync<GetUser>(userId), Times.Once);
        }

        [Fact]
        public async Task GetAccountAsync_LogsInformationMessages()
        {
            // Arrange
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync<GetUser>(It.IsAny<string>()))
                .ReturnsAsync(new GetUser());

            // Act
            await _service.GetAccountAsync();

            // Assert
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Account get start")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Account get end")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        #endregion

        #region IsUserAvailableAsync Tests

        [Fact]
        public async Task IsUserAvailableAsync_WithNonExistentEmail_ReturnsTrue()
        {
            // Arrange
            var request = new IsEmailAvaiableRequest { Email = "NEW@EXAMPLE.COM" };
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync("new@example.com"))
                .ReturnsAsync((User)null);

            // Act
            var result = await _service.IsUserAvailableAsync(request);

            // Assert
            result.Should().BeTrue();
            _userRepositoryMock.Verify(x => x.GetUserByEmailAsync("new@example.com"), Times.Once);
        }

        [Fact]
        public async Task IsUserAvailableAsync_WithExistingEmail_ReturnsFalse()
        {
            // Arrange
            var request = new IsEmailAvaiableRequest { Email = "EXISTING@EXAMPLE.COM" };
            var existingUser = new User { ItemId = "user-1", Email = "existing@example.com" };
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync("existing@example.com"))
                .ReturnsAsync(existingUser);

            // Act
            var result = await _service.IsUserAvailableAsync(request);

            // Assert
            result.Should().BeFalse();
        }

        [Theory]
        [InlineData("TEST@EXAMPLE.COM", "test@example.com")]
        [InlineData("User@Domain.COM", "user@domain.com")]
        [InlineData("ADMIN@TEST.ORG", "admin@test.org")]
        public async Task IsUserAvailableAsync_ConvertsEmailToLowerCase(string inputEmail, string expectedEmail)
        {
            // Arrange
            var request = new IsEmailAvaiableRequest { Email = inputEmail };
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync(expectedEmail))
                .ReturnsAsync((User)null);

            // Act
            await _service.IsUserAvailableAsync(request);

            // Assert
            _userRepositoryMock.Verify(x => x.GetUserByEmailAsync(expectedEmail), Times.Once);
        }

        [Fact]
        public async Task IsUserAvailableAsync_LogsInformationMessages()
        {
            // Arrange
            var request = new IsEmailAvaiableRequest { Email = "test@example.com" };
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync((User)null);

            // Act
            await _service.IsUserAvailableAsync(request);

            // Assert
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("User existance search start")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("User existance search end")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        #endregion

        #region GetUsersAsync Tests

        [Fact]
        public async Task GetUsersAsync_WithValidRequest_ReturnsUsersResponse()
        {
            // Arrange
            var request = new GetUsersRequest { Page = 0, PageSize = 10 };
            var users = new[] { new GetUser { ItemId = "user-1" }, new GetUser { ItemId = "user-2" } }.AsQueryable();
            var totalCount = 25L;

            _userRepositoryMock.Setup(x => x.GetUsersAsync<GetUser, GetUsersRequest>(request))
                .ReturnsAsync((users, totalCount));

            // Act
            var result = await _service.GetUsersAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeSameAs(users);
            result.TotalCount.Should().Be(totalCount);
            _userRepositoryMock.Verify(x => x.GetUsersAsync<GetUser, GetUsersRequest>(request), Times.Once);
        }

        [Fact]
        public async Task GetUsersAsync_LogsInformationMessages()
        {
            // Arrange
            var request = new GetUsersRequest();
            _userRepositoryMock.Setup(x => x.GetUsersAsync<GetUser, GetUsersRequest>(request))
                .ReturnsAsync((Enumerable.Empty<GetUser>().AsQueryable(), 0L));

            // Act
            await _service.GetUsersAsync(request);

            // Assert
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("User get start")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("User get end")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        #endregion

        #region GetUserAsync Tests

        [Fact]
        public async Task GetUserAsync_WithSpecificId_ReturnsSpecificUser()
        {
            // Arrange
            var userId = "specific-user-123";
            var user = new GetUser { ItemId = userId, Email = "specific@example.com" };
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync<GetUser>(userId))
                .ReturnsAsync(user);

            // Act
            var result = await _service.GetUserAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeSameAs(user);
            result.Data.ItemId.Should().Be(userId);
            _userRepositoryMock.Verify(x => x.GetUserByIdAsync<GetUser>(userId), Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task GetUserAsync_WithEmptyOrNullId_UsesContextUserId(string emptyId)
        {
            // Arrange
            var contextUserId = "context-user-456";
            SetupBlocksContext(contextUserId);
            var user = new GetUser { ItemId = contextUserId };
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync<GetUser>(contextUserId))
                .ReturnsAsync(user);

            // Act
            var result = await _service.GetUserAsync(emptyId);

            // Assert
            result.Data.ItemId.Should().Be(contextUserId);
            _userRepositoryMock.Verify(x => x.GetUserByIdAsync<GetUser>(contextUserId), Times.Once);
        }

        [Fact]
        public async Task GetUserAsync_LogsInformationMessages()
        {
            // Arrange
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync<GetUser>(It.IsAny<string>()))
                .ReturnsAsync(new GetUser());

            // Act
            await _service.GetUserAsync("user-123");

            // Assert
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("User get start")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("User get end")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        #endregion

        #region GetAccountRolesAsync Tests

        [Fact]
        public async Task GetAccountRolesAsync_WithContextUserId_ReturnsUserRoles()
        {
            // Arrange
            var userId = "user-with-roles";
            SetupBlocksContext(userId);
            var roles = new List<GetUserRole>
            {
                new GetUserRole { Slug = "admin" },
                new GetUserRole { Slug = "editor" }
            };
            _userRepositoryMock.Setup(x => x.GetRolesBySlugsAsync(userId))
                .ReturnsAsync(roles);

            // Act
            var result = await _service.GetAccountRolesAsync();

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeSameAs(roles);
            result.Data.Should().HaveCount(2);
            _userRepositoryMock.Verify(x => x.GetRolesBySlugsAsync(userId), Times.Once);
        }

        [Fact]
        public async Task GetAccountRolesAsync_WithNoRoles_ReturnsEmptyList()
        {
            // Arrange
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetRolesBySlugsAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<GetUserRole>());

            // Act
            var result = await _service.GetAccountRolesAsync();

            // Assert
            result.Data.Should().BeEmpty();
        }

        #endregion

        #region GetAccountPermissionsAsync Tests

        [Fact]
        public async Task GetAccountPermissionsAsync_WithContextUserId_ReturnsUserPermissions()
        {
            // Arrange
            var userId = "user-with-permissions";
            SetupBlocksContext(userId);
            var permissions = new List<GetUserPermission>
            {
                new GetUserPermission { Resource = "users:read" },
                new GetUserPermission { Resource = "users:write" }
            };
            _userRepositoryMock.Setup(x => x.GetPermissionsByResourcesAsync(userId))
                .ReturnsAsync(permissions);

            // Act
            var result = await _service.GetAccountPermissionsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeSameAs(permissions);
            result.Data.Should().HaveCount(2);
            _userRepositoryMock.Verify(x => x.GetPermissionsByResourcesAsync(userId), Times.Once);
        }

        [Fact]
        public async Task GetAccountPermissionsAsync_WithNoPermissions_ReturnsEmptyList()
        {
            // Arrange
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetPermissionsByResourcesAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<GetUserPermission>());

            // Act
            var result = await _service.GetAccountPermissionsAsync();

            // Assert
            result.Data.Should().BeEmpty();
        }

        #endregion

        #region GetUserRolesAsync Tests

        [Fact]
        public async Task GetUserRolesAsync_WithSpecificId_ReturnsSpecificUserRoles()
        {
            // Arrange
            var userId = "specific-user-roles";
            SetupBlocksContext();
            var roles = new List<GetUserRole> { new GetUserRole { Slug = "manager" } };
            _userRepositoryMock.Setup(x => x.GetRolesBySlugsAsync(userId))
                .ReturnsAsync(roles);

            // Act
            var result = await _service.GetUserRolesAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeSameAs(roles);
            _userRepositoryMock.Verify(x => x.GetRolesBySlugsAsync(userId), Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task GetUserRolesAsync_WithEmptyOrNullId_UsesContextUserId(string emptyId)
        {
            // Arrange
            var contextUserId = "context-user-789";
            SetupBlocksContext(contextUserId);
            var roles = new List<GetUserRole>();
            _userRepositoryMock.Setup(x => x.GetRolesBySlugsAsync(contextUserId))
                .ReturnsAsync(roles);

            // Act
            var result = await _service.GetUserRolesAsync(emptyId);

            // Assert
            _userRepositoryMock.Verify(x => x.GetRolesBySlugsAsync(contextUserId), Times.Once);
        }

        #endregion

        #region GetUserPermissionsAsync Tests

        [Fact]
        public async Task GetUserPermissionsAsync_WithSpecificId_ReturnsSpecificUserPermissions()
        {
            // Arrange
            var userId = "specific-user-permissions";
            SetupBlocksContext();
            var permissions = new List<GetUserPermission> { new GetUserPermission { Resource = "admin:access" } };
            _userRepositoryMock.Setup(x => x.GetPermissionsByResourcesAsync(userId))
                .ReturnsAsync(permissions);

            // Act
            var result = await _service.GetUserPermissionsAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeSameAs(permissions);
            _userRepositoryMock.Verify(x => x.GetPermissionsByResourcesAsync(userId), Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task GetUserPermissionsAsync_WithEmptyOrNullId_UsesContextUserId(string emptyId)
        {
            // Arrange
            var contextUserId = "context-user-permissions";
            SetupBlocksContext(contextUserId);
            var permissions = new List<GetUserPermission>();
            _userRepositoryMock.Setup(x => x.GetPermissionsByResourcesAsync(contextUserId))
                .ReturnsAsync(permissions);

            // Act
            var result = await _service.GetUserPermissionsAsync(emptyId);

            // Assert
            _userRepositoryMock.Verify(x => x.GetPermissionsByResourcesAsync(contextUserId), Times.Once);
        }

        #endregion

        #region GetUserTimelinesAsync Tests

        [Fact]
        public async Task GetUserTimelinesAsync_WithValidRequest_ReturnsTimelines()
        {
            // Arrange
            var request = new GetUserTimeLineRequest();
            var timelines = new List<UserTimeline>
            {
                new UserTimeline { ItemId = "timeline-1", Event = "USER_CREATED" },
                new UserTimeline { ItemId = "timeline-2", Event = "USER_UPDATED" }
            };
            _userRepositoryMock.Setup(x => x.GetUserTimelinesAsync(request))
                .ReturnsAsync(timelines);

            // Act
            var result = await _service.GetUserTimelinesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeSameAs(timelines);
            result.Should().HaveCount(2);
            _userRepositoryMock.Verify(x => x.GetUserTimelinesAsync(request), Times.Once);
        }

        [Fact]
        public async Task GetUserTimelinesAsync_WithEmptyResult_ReturnsEmptyList()
        {
            // Arrange
            var request = new GetUserTimeLineRequest();
            _userRepositoryMock.Setup(x => x.GetUserTimelinesAsync(request))
                .ReturnsAsync(new List<UserTimeline>());

            // Act
            var result = await _service.GetUserTimelinesAsync(request);

            // Assert
            result.Should().BeEmpty();
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task AllMethods_WithLogging_LogCorrectly()
        {
            // Arrange
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetUsersAsync<GetAccounts, GetAccountsRequest>(It.IsAny<GetAccountsRequest>()))
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));
            _userRepositoryMock.Setup(x => x.GetUsersAsync<GetUser, GetUsersRequest>(It.IsAny<GetUsersRequest>()))
                .ReturnsAsync((Enumerable.Empty<GetUser>().AsQueryable(), 0L));
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync<GetUser>(It.IsAny<string>()))
                .ReturnsAsync(new GetUser());
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync((User)null);

            // Act
            await _service.GetAccountsAsync(new GetAccountsRequest());
            await _service.GetAccountAsync();
            await _service.IsUserAvailableAsync(new IsEmailAvaiableRequest { Email = "test@test.com" });
            await _service.GetUsersAsync(new GetUsersRequest());
            await _service.GetUserAsync("user-1");

            // Assert - Verify logging occurred for all methods
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.AtLeast(10));
        }

        [Fact]
        public async Task RoleAndPermissionMethods_UseCorrectUserId()
        {
            // Arrange
            var contextUserId = "integration-user";
            var specificUserId = "specific-user";
            SetupBlocksContext(contextUserId);
            _userRepositoryMock.Setup(x => x.GetRolesBySlugsAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<GetUserRole>());
            _userRepositoryMock.Setup(x => x.GetPermissionsByResourcesAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<GetUserPermission>());

            // Act - Methods that should use context user ID
            await _service.GetAccountRolesAsync();
            await _service.GetAccountPermissionsAsync();

            // Act - Methods that should use specific user ID
            await _service.GetUserRolesAsync(specificUserId);
            await _service.GetUserPermissionsAsync(specificUserId);

            // Act - Methods that should fall back to context user ID (null and whitespace)
            await _service.GetUserRolesAsync(null);
            await _service.GetUserPermissionsAsync("   ");

            // Assert - Each Account* method + one Get*Async with fallback = 2 calls each
            _userRepositoryMock.Verify(x => x.GetRolesBySlugsAsync(contextUserId), Times.Exactly(2));
            _userRepositoryMock.Verify(x => x.GetRolesBySlugsAsync(specificUserId), Times.Once);
            _userRepositoryMock.Verify(x => x.GetPermissionsByResourcesAsync(contextUserId), Times.Exactly(2));
            _userRepositoryMock.Verify(x => x.GetPermissionsByResourcesAsync(specificUserId), Times.Once);
        }

        #endregion
    }
}
