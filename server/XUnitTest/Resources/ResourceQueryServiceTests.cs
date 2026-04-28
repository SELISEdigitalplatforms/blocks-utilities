using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Resources
{
    public class ResourceQueryServiceTests : IDisposable
    {
        private readonly Mock<ILogger<ResourceQueryService>> _loggerMock;
        private readonly Mock<IResourceRepository> _resourceRepositoryMock;
        private readonly ResourceQueryService _resourceQueryService;

        public ResourceQueryServiceTests()
        {
            _loggerMock = new Mock<ILogger<ResourceQueryService>>();
            _resourceRepositoryMock = new Mock<IResourceRepository>();
            _resourceQueryService = new ResourceQueryService(
                _loggerMock.Object,
                _resourceRepositoryMock.Object
            );
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        #region GetPermissionAsync Tests

        [Fact]
        public async Task GetPermissionAsync_WithValidId_ReturnsPermissionResponse()
        {
            // Arrange
            var permissionId = "perm-123";
            var expectedPermission = new Permission 
            { 
                ItemId = permissionId, 
                Resource = "api/users/read",
                Name = "Read Users"
            };

            _resourceRepositoryMock
                .Setup(x => x.GetPermissionByIdAsync(permissionId))
                .ReturnsAsync(expectedPermission);

            // Act
            var result = await _resourceQueryService.GetPermissionAsync(permissionId);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.ItemId.Should().Be(permissionId);
            result.Data.Resource.Should().Be("api/users/read");
            _resourceRepositoryMock.Verify(x => x.GetPermissionByIdAsync(permissionId), Times.Once);
        }

        [Fact]
        public async Task GetPermissionAsync_LogsStartAndEnd()
        {
            // Arrange
            var permissionId = "perm-456";
            var permission = new Permission { ItemId = permissionId, Resource = "api/test" };

            _resourceRepositoryMock
                .Setup(x => x.GetPermissionByIdAsync(permissionId))
                .ReturnsAsync(permission);

            // Act
            await _resourceQueryService.GetPermissionAsync(permissionId);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Permission get start")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Permission get end")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetPermissionAsync_WithNullResult_ReturnsResponseWithNullData()
        {
            // Arrange
            var permissionId = "non-existent";

            _resourceRepositoryMock
                .Setup(x => x.GetPermissionByIdAsync(permissionId))
                .ReturnsAsync((Permission)null);

            // Act
            var result = await _resourceQueryService.GetPermissionAsync(permissionId);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeNull();
        }

        #endregion

        #region GetPermissionsAsync Tests

        [Fact]
        public async Task GetPermissionsAsync_WithValidRequest_ReturnsPermissionsResponse()
        {
            // Arrange
            var request = new GetPermissionsRequest
            {
                Page = 0,
                PageSize = 10,
                Roles = new List<string> { "admin" }
            };

            var permissions = new List<Permission>
            {
                new Permission { ItemId = "perm1", Resource = "api/test1" },
                new Permission { ItemId = "perm2", Resource = "api/test2" },
                new Permission { ItemId = "perm3", Resource = "api/test3" }
            }.AsQueryable();
            var totalCount = 15L;

            _resourceRepositoryMock
                .Setup(x => x.GetPermissionsAsync(request))
                .ReturnsAsync((permissions, totalCount));

            // Act
            var result = await _resourceQueryService.GetPermissionsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.Count().Should().Be(3);
            result.TotalCount.Should().Be(totalCount);
            _resourceRepositoryMock.Verify(x => x.GetPermissionsAsync(request), Times.Once);
        }

        [Fact]
        public async Task GetPermissionsAsync_LogsStartAndEnd()
        {
            // Arrange
            var request = new GetPermissionsRequest { Page = 0, PageSize = 10 };
            var permissions = Enumerable.Empty<Permission>().AsQueryable();

            _resourceRepositoryMock
                .Setup(x => x.GetPermissionsAsync(request))
                .ReturnsAsync((permissions, 0L));

            // Act
            await _resourceQueryService.GetPermissionsAsync(request);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Permissions get start")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Permissions get end")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetPermissionsAsync_WithEmptyResult_ReturnsEmptyResponse()
        {
            // Arrange
            var request = new GetPermissionsRequest { Page = 0, PageSize = 10 };
            var emptyPermissions = Enumerable.Empty<Permission>().AsQueryable();

            _resourceRepositoryMock
                .Setup(x => x.GetPermissionsAsync(request))
                .ReturnsAsync((emptyPermissions, 0L));

            // Act
            var result = await _resourceQueryService.GetPermissionsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
        }

        [Theory]
        [InlineData(0, 10, 25)]
        [InlineData(1, 20, 50)]
        [InlineData(2, 15, 100)]
        public async Task GetPermissionsAsync_WithDifferentPagination_ReturnsCorrectCount(
            int page, int pageSize, long expectedCount)
        {
            // Arrange
            var request = new GetPermissionsRequest { Page = page, PageSize = pageSize };
            var permissions = Enumerable.Empty<Permission>().AsQueryable();

            _resourceRepositoryMock
                .Setup(x => x.GetPermissionsAsync(request))
                .ReturnsAsync((permissions, expectedCount));

            // Act
            var result = await _resourceQueryService.GetPermissionsAsync(request);

            // Assert
            result.TotalCount.Should().Be(expectedCount);
        }

        #endregion

        #region GetRoleAsync Tests

        [Fact]
        public async Task GetRoleAsync_WithValidId_ReturnsRoleResponse()
        {
            // Arrange
            var roleId = "role-123";
            var expectedRole = new Role 
            { 
                ItemId = roleId, 
                Slug = "admin",
                Name = "Administrator"
            };

            _resourceRepositoryMock
                .Setup(x => x.GetRoleByIdAsync(roleId))
                .ReturnsAsync(expectedRole);

            // Act
            var result = await _resourceQueryService.GetRoleAsync(roleId);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.ItemId.Should().Be(roleId);
            result.Data.Slug.Should().Be("admin");
            _resourceRepositoryMock.Verify(x => x.GetRoleByIdAsync(roleId), Times.Once);
        }

        [Fact]
        public async Task GetRoleAsync_LogsStartAndEnd()
        {
            // Arrange
            var roleId = "role-456";
            var role = new Role { ItemId = roleId, Slug = "user" };

            _resourceRepositoryMock
                .Setup(x => x.GetRoleByIdAsync(roleId))
                .ReturnsAsync(role);

            // Act
            await _resourceQueryService.GetRoleAsync(roleId);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Role get start")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Role get end")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetRoleAsync_WithNullResult_ReturnsResponseWithNullData()
        {
            // Arrange
            var roleId = "non-existent";

            _resourceRepositoryMock
                .Setup(x => x.GetRoleByIdAsync(roleId))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _resourceQueryService.GetRoleAsync(roleId);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeNull();
        }

        #endregion

        #region GetRolesAsync Tests

        [Fact]
        public async Task GetRolesAsync_WithValidRequest_ReturnsRolesResponse()
        {
            // Arrange
            var request = new GetRolesRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new GetRolesFilter { Search = "admin" }
            };

            var roles = new List<Role>
            {
                new Role { ItemId = "role1", Slug = "admin" },
                new Role { ItemId = "role2", Slug = "user" },
                new Role { ItemId = "role3", Slug = "guest" }
            }.AsQueryable();
            var totalCount = 20L;

            _resourceRepositoryMock
                .Setup(x => x.GetRolesAsync(request))
                .ReturnsAsync((roles, totalCount));

            // Act
            var result = await _resourceQueryService.GetRolesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.Count().Should().Be(3);
            result.TotalCount.Should().Be(totalCount);
            _resourceRepositoryMock.Verify(x => x.GetRolesAsync(request), Times.Once);
        }

        [Fact]
        public async Task GetRolesAsync_LogsStartAndEnd()
        {
            // Arrange
            var request = new GetRolesRequest { Page = 0, PageSize = 10 };
            var roles = Enumerable.Empty<Role>().AsQueryable();

            _resourceRepositoryMock
                .Setup(x => x.GetRolesAsync(request))
                .ReturnsAsync((roles, 0L));

            // Act
            await _resourceQueryService.GetRolesAsync(request);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Roles get start")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Roles get end")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetRolesAsync_WithEmptyResult_ReturnsEmptyResponse()
        {
            // Arrange
            var request = new GetRolesRequest { Page = 0, PageSize = 10 };
            var emptyRoles = Enumerable.Empty<Role>().AsQueryable();

            _resourceRepositoryMock
                .Setup(x => x.GetRolesAsync(request))
                .ReturnsAsync((emptyRoles, 0L));

            // Act
            var result = await _resourceQueryService.GetRolesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().NotBeNull();
            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
        }

        [Theory]
        [InlineData(0, 10, 5)]
        [InlineData(1, 25, 30)]
        [InlineData(3, 50, 200)]
        public async Task GetRolesAsync_WithDifferentPagination_ReturnsCorrectCount(
            int page, int pageSize, long expectedCount)
        {
            // Arrange
            var request = new GetRolesRequest { Page = page, PageSize = pageSize };
            var roles = Enumerable.Empty<Role>().AsQueryable();

            _resourceRepositoryMock
                .Setup(x => x.GetRolesAsync(request))
                .ReturnsAsync((roles, expectedCount));

            // Act
            var result = await _resourceQueryService.GetRolesAsync(request);

            // Assert
            result.TotalCount.Should().Be(expectedCount);
        }

        #endregion

        #region GetResourceGroupsAsync Tests

        [Fact]
        public async Task GetResourceGroupsAsync_ReturnsResourceGroups()
        {
            // Arrange
            var expectedGroups = new List<GetResourceGroupResponse>
            {
                new GetResourceGroupResponse { ResourceGroup = "group1", Count = 5 },
                new GetResourceGroupResponse { ResourceGroup = "group2", Count = 10 },
                new GetResourceGroupResponse { ResourceGroup = "group3", Count = 3 }
            };

            _resourceRepositoryMock
                .Setup(x => x.GetResourceGroupsAsync())
                .ReturnsAsync(expectedGroups);

            // Act
            var result = await _resourceQueryService.GetResourceGroupsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(3);
            result.Should().BeSameAs(expectedGroups);
            _resourceRepositoryMock.Verify(x => x.GetResourceGroupsAsync(), Times.Once);
        }

        [Fact]
        public async Task GetResourceGroupsAsync_WithEmptyResult_ReturnsEmptyList()
        {
            // Arrange
            var emptyGroups = new List<GetResourceGroupResponse>();

            _resourceRepositoryMock
                .Setup(x => x.GetResourceGroupsAsync())
                .ReturnsAsync(emptyGroups);

            // Act
            var result = await _resourceQueryService.GetResourceGroupsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetResourceGroupsAsync_DoesNotLog()
        {
            // Arrange
            var groups = new List<GetResourceGroupResponse>
            {
                new GetResourceGroupResponse { ResourceGroup = "group1", Count = 5 }
            };

            _resourceRepositoryMock
                .Setup(x => x.GetResourceGroupsAsync())
                .ReturnsAsync(groups);

            // Act
            await _resourceQueryService.GetResourceGroupsAsync();

            // Assert - No logging should occur for this method
            _loggerMock.Verify(
                x => x.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task AllMethods_CallRepositoryCorrectly()
        {
            // Arrange
            var permissionId = "perm-test";
            var roleId = "role-test";
            var permissionsRequest = new GetPermissionsRequest { Page = 0, PageSize = 10 };
            var rolesRequest = new GetRolesRequest { Page = 0, PageSize = 10 };

            var permission = new Permission { ItemId = permissionId, Resource = "api/test" };
            var role = new Role { ItemId = roleId, Slug = "test" };
            var permissions = new List<Permission> { new Permission { ItemId = "perm1", Resource = "api/test" } }.AsQueryable();
            var roles = new List<Role> { new Role { ItemId = "role1", Slug = "test" } }.AsQueryable();
            var groups = new List<GetResourceGroupResponse> 
            { 
                new GetResourceGroupResponse { ResourceGroup = "group1", Count = 1 } 
            };

            _resourceRepositoryMock.Setup(x => x.GetPermissionByIdAsync(permissionId)).ReturnsAsync(permission);
            _resourceRepositoryMock.Setup(x => x.GetRoleByIdAsync(roleId)).ReturnsAsync(role);
            _resourceRepositoryMock.Setup(x => x.GetPermissionsAsync(permissionsRequest)).ReturnsAsync((permissions, 1L));
            _resourceRepositoryMock.Setup(x => x.GetRolesAsync(rolesRequest)).ReturnsAsync((roles, 1L));
            _resourceRepositoryMock.Setup(x => x.GetResourceGroupsAsync()).ReturnsAsync(groups);

            // Act
            await _resourceQueryService.GetPermissionAsync(permissionId);
            await _resourceQueryService.GetRoleAsync(roleId);
            await _resourceQueryService.GetPermissionsAsync(permissionsRequest);
            await _resourceQueryService.GetRolesAsync(rolesRequest);
            await _resourceQueryService.GetResourceGroupsAsync();

            // Assert
            _resourceRepositoryMock.Verify(x => x.GetPermissionByIdAsync(permissionId), Times.Once);
            _resourceRepositoryMock.Verify(x => x.GetRoleByIdAsync(roleId), Times.Once);
            _resourceRepositoryMock.Verify(x => x.GetPermissionsAsync(permissionsRequest), Times.Once);
            _resourceRepositoryMock.Verify(x => x.GetRolesAsync(rolesRequest), Times.Once);
            _resourceRepositoryMock.Verify(x => x.GetResourceGroupsAsync(), Times.Once);
        }

        #endregion
    }
}
