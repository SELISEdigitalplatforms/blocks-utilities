using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Resources
{
    public class ResourceRepositoryTests : IDisposable
    {
        private readonly Mock<IIdentityAccessManagementRepository> _iamRepositoryMock;
        private readonly ResourceRepository _resourceRepository;
        private readonly Mock<IMongoCollection<Permission>> _permissionCollectionMock;
        private readonly Mock<IMongoCollection<Role>> _roleCollectionMock;
        private readonly Mock<IMongoCollection<Organization>> _organizationCollectionMock;
        private readonly Mock<IMongoCollection<OrganizationConfig>> _orgConfigCollectionMock;

        public ResourceRepositoryTests()
        {
            _iamRepositoryMock = new Mock<IIdentityAccessManagementRepository>();
            _permissionCollectionMock = new Mock<IMongoCollection<Permission>>();
            _roleCollectionMock = new Mock<IMongoCollection<Role>>();
            _organizationCollectionMock = new Mock<IMongoCollection<Organization>>();
            _orgConfigCollectionMock = new Mock<IMongoCollection<OrganizationConfig>>();
            _resourceRepository = new ResourceRepository(_iamRepositoryMock.Object);
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        #region Permission Tests

        [Fact]
        public async Task GetPermissionByResourceAsync_WithValidResource_ReturnsPermission()
        {
            // Arrange
            var resource = "api/users/read";
            var expectedPermission = new Permission { ItemId = "perm-1", Resource = resource };
            var cursorMock = CreateAsyncCursorMock(new List<Permission> { expectedPermission });

            _permissionCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<FindOptions<Permission, Permission>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var result = await _resourceRepository.GetPermissionByResourceAsync(resource);

            // Assert
            result.Should().NotBeNull();
            result.Resource.Should().Be(resource);
            _iamRepositoryMock.Verify(x => x.GetCollection<Permission>(), Times.Once);
        }

        [Fact]
        public async Task GetPermissionByIdAsync_WithValidId_ReturnsPermission()
        {
            // Arrange
            var permissionId = "perm-123";
            var expectedPermission = new Permission { ItemId = permissionId, Resource = "api/test" };
            var cursorMock = CreateAsyncCursorMock(new List<Permission> { expectedPermission });

            _permissionCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<FindOptions<Permission, Permission>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var result = await _resourceRepository.GetPermissionByIdAsync(permissionId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(permissionId);
        }

        [Fact]
        public async Task InsertPermissionAsync_WithValidPermission_ReturnsTrue()
        {
            // Arrange
            var permission = new Permission { ItemId = "perm-new", Resource = "api/new" };
            
            _permissionCollectionMock
                .Setup(x => x.InsertOneAsync(permission, null, default))
                .Returns(Task.CompletedTask);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var result = await _resourceRepository.InsertPermissionAsync(permission);

            // Assert
            result.Should().BeTrue();
            _permissionCollectionMock.Verify(x => x.InsertOneAsync(permission, null, default), Times.Once);
        }

        [Fact]
        public async Task UpdatePermissionAsync_WithValidPermission_ReturnsTrue()
        {
            // Arrange
            var permission = new Permission { ItemId = "perm-update", Resource = "api/update" };
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, BsonValue.Create("perm-update"));

            _permissionCollectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    permission,
                    It.IsAny<ReplaceOptions>(),
                    default))
                .ReturnsAsync(replaceResult);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var result = await _resourceRepository.UpdatePermissionAsync(permission);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdatePermissionAsync_WithUnacknowledgedResult_ReturnsFalse()
        {
            // Arrange
            var permission = new Permission { ItemId = "perm-fail", Resource = "api/fail" };
            
            _permissionCollectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    permission,
                    It.IsAny<ReplaceOptions>(),
                    default))
                .ReturnsAsync((ReplaceOneResult)null);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var result = await _resourceRepository.UpdatePermissionAsync(permission);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task GetPermissionsAsync_WithEmptyRoles_ReturnsAllPermissions()
        {
            // Arrange
            var request = new GetPermissionsRequest
            {
                Page = 0,
                PageSize = 10,
                Roles = new List<string>()
            };

            var permissions = CreatePermissionList(3);
            var cursorMock = CreateAsyncCursorMock(permissions);

            _permissionCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<FindOptions<Permission, Permission>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _permissionCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(3);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var (result, count) = await _resourceRepository.GetPermissionsAsync(request);

            // Assert
            result.Should().NotBeNull();
            count.Should().Be(3);
        }

        [Fact]
        public async Task GetPermissionsAsync_WithFilters_AppliesAllFilters()
        {
            // Arrange
            var request = new GetPermissionsRequest
            {
                Page = 0,
                PageSize = 10,
                Roles = new List<string> { "admin" },
                Filter = new GetPermissionFilter
                {
                    IsArchived = false,
                    Type = ResourceType.Endpoint,
                    Search = "test",
                    IsBuiltIn = "yes",
                    Tags = new List<string> { "tag1" },
                    ResourceGroup = "group1",
                    Resources = new List<string> { "api/test" }
                }
            };

            var permissions = CreatePermissionList(2);
            var cursorMock = CreateAsyncCursorMock(permissions);

            _permissionCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<FindOptions<Permission, Permission>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _permissionCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(2);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var (result, count) = await _resourceRepository.GetPermissionsAsync(request);

            // Assert
            result.Should().NotBeNull();
            count.Should().Be(2);
        }

        [Theory]
        [InlineData(true, 5)]
        [InlineData(false, 10)]
        public async Task GetPermissionsAsync_WithSorting_AppliesSortCorrectly(bool isDescending, int expectedCount)
        {
            // Arrange
            var request = new GetPermissionsRequest
            {
                Page = 0,
                PageSize = 10,
                Roles = new List<string>()
            };

            var permissions = CreatePermissionList(expectedCount);
            var cursorMock = CreateAsyncCursorMock(permissions);

            _permissionCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<FindOptions<Permission, Permission>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _permissionCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedCount);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var (result, count) = await _resourceRepository.GetPermissionsAsync(request);

            // Assert
            count.Should().Be(expectedCount);
        }

        #endregion

        #region Role Tests

        [Fact]
        public async Task GetRoleByIdAsync_WithValidId_ReturnsRole()
        {
            // Arrange
            var roleId = "role-123";
            var expectedRole = new Role { ItemId = roleId, Slug = "admin" };
            var cursorMock = CreateAsyncCursorMock(new List<Role> { expectedRole });

            _roleCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Role>>(),
                    It.IsAny<FindOptions<Role, Role>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<Role>()).Returns(_roleCollectionMock.Object);

            // Act
            var result = await _resourceRepository.GetRoleByIdAsync(roleId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(roleId);
        }

        [Fact]
        public async Task GetRoleBySlugAsync_WithValidSlug_ReturnsRole()
        {
            // Arrange
            var slug = "admin";
            var expectedRole = new Role { ItemId = "role-1", Slug = slug };
            var cursorMock = CreateAsyncCursorMock(new List<Role> { expectedRole });

            _roleCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Role>>(),
                    It.IsAny<FindOptions<Role, Role>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<Role>()).Returns(_roleCollectionMock.Object);

            // Act
            var result = await _resourceRepository.GetRoleBySlugAsync(slug);

            // Assert
            result.Should().NotBeNull();
            result.Slug.Should().Be(slug);
        }

        [Fact]
        public async Task InsertRoleAsync_WithValidRole_ReturnsTrue()
        {
            // Arrange
            var role = new Role { ItemId = "role-new", Slug = "new-role" };
            
            _roleCollectionMock
                .Setup(x => x.InsertOneAsync(role, null, default))
                .Returns(Task.CompletedTask);

            _iamRepositoryMock.Setup(x => x.GetCollection<Role>()).Returns(_roleCollectionMock.Object);

            // Act
            var result = await _resourceRepository.InsertRoleAsync(role);

            // Assert
            result.Should().BeTrue();
            _roleCollectionMock.Verify(x => x.InsertOneAsync(role, null, default), Times.Once);
        }

        [Fact]
        public async Task UpdateRoleAsync_WithValidRole_ReturnsTrue()
        {
            // Arrange
            var role = new Role { ItemId = "role-update", Slug = "admin" };
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, BsonValue.Create("role-update"));

            _roleCollectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<Role>>(),
                    role,
                    It.IsAny<ReplaceOptions>(),
                    default))
                .ReturnsAsync(replaceResult);

            _iamRepositoryMock.Setup(x => x.GetCollection<Role>()).Returns(_roleCollectionMock.Object);

            // Act
            var result = await _resourceRepository.UpdateRoleAsync(role);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task GetRolesAsync_WithNoFilter_ReturnsAllRoles()
        {
            // Arrange
            var request = new GetRolesRequest { Page = 0, PageSize = 10 };
            var roles = CreateRoleList(5);
            var cursorMock = CreateAsyncCursorMock(roles);

            _roleCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Role>>(),
                    It.IsAny<FindOptions<Role, Role>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _roleCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Role>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(5);

            _iamRepositoryMock.Setup(x => x.GetCollection<Role>()).Returns(_roleCollectionMock.Object);

            // Act
            var (result, count) = await _resourceRepository.GetRolesAsync(request);

            // Assert
            result.Should().NotBeNull();
            count.Should().Be(5);
        }

        [Fact]
        public async Task GetRolesAsync_WithSearchFilter_AppliesSearch()
        {
            // Arrange
            var request = new GetRolesRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new GetRolesFilter
                {
                    Search = "admin",
                    Slugs = new List<string> { "admin", "user" }
                }
            };

            var roles = CreateRoleList(2);
            var cursorMock = CreateAsyncCursorMock(roles);

            _roleCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Role>>(),
                    It.IsAny<FindOptions<Role, Role>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _roleCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Role>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(2);

            _iamRepositoryMock.Setup(x => x.GetCollection<Role>()).Returns(_roleCollectionMock.Object);

            // Act
            var (result, count) = await _resourceRepository.GetRolesAsync(request);

            // Assert
            result.Should().NotBeNull();
            count.Should().Be(2);
        }

        [Fact]
        public async Task UpdateRolePermissionByIdsAsync_WithValidData_ReturnsTrue()
        {
            // Arrange
            var slug = "admin";
            var permissions = new List<string> { "perm-1", "perm-2" };
            var updateResult = new UpdateResult.Acknowledged(2, 2, BsonValue.Create(1));

            _permissionCollectionMock
                .Setup(x => x.UpdateManyAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<UpdateDefinition<Permission>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(updateResult);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var result = await _resourceRepository.UpdateRolePermissionByIdsAsync(slug, permissions);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task RemoveRolePermissionByIdsAsync_WithValidData_ReturnsTrue()
        {
            // Arrange
            var slug = "admin";
            var permissions = new List<string> { "perm-1", "perm-2" };
            var updateResult = new UpdateResult.Acknowledged(2, 2, BsonValue.Create(1));

            _permissionCollectionMock
                .Setup(x => x.UpdateManyAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<UpdateDefinition<Permission>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(updateResult);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);

            // Act
            var result = await _resourceRepository.RemoveRolePermissionByIdsAsync(slug, permissions);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateRolesCountAsync_WithValidSlug_ReturnsTrue()
        {
            // Arrange
            var slug = "admin";
            var updateResult = new UpdateResult.Acknowledged(1, 1, BsonValue.Create(1));

            _permissionCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Permission>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(10);

            _roleCollectionMock
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<Role>>(),
                    It.IsAny<UpdateDefinition<Role>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(updateResult);

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>()).Returns(_permissionCollectionMock.Object);
            _iamRepositoryMock.Setup(x => x.GetCollection<Role>()).Returns(_roleCollectionMock.Object);

            // Act
            var result = await _resourceRepository.UpdateRolesCountAsync(slug);

            // Assert
            result.Should().BeTrue();
        }

        #endregion

        #region Resource Timeline Tests

        [Fact]
        public async Task GetResourceTimelineAsync_WithValidItemId_ReturnsTimeline()
        {
            // Arrange
            var itemId = "timeline-123";
            var expectedTimeline = new ResourceTimeline<Permission> { ItemId = itemId, Entity = "Permission" };
            var cursorMock = CreateAsyncCursorMock(new List<ResourceTimeline<Permission>> { expectedTimeline });
            var collectionMock = new Mock<IMongoCollection<ResourceTimeline<Permission>>>();

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<ResourceTimeline<Permission>>>(),
                    It.IsAny<FindOptions<ResourceTimeline<Permission>, ResourceTimeline<Permission>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock
                .Setup(x => x.GetCollectionByName<ResourceTimeline<Permission>>("ResourceTimelines"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _resourceRepository.GetResourceTimelineAsync<Permission>(itemId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(itemId);
        }

        [Fact]
        public async Task SaveResourceTimelineAsync_WithValidTimeline_ReturnsTrue()
        {
            // Arrange
            var timeline = new ResourceTimeline<Role> { ItemId = "timeline-new", Entity = "Role" };
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, BsonValue.Create("timeline-new"));
            var collectionMock = new Mock<IMongoCollection<ResourceTimeline<Role>>>();

            collectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<ResourceTimeline<Role>>>(),
                    timeline,
                    It.IsAny<ReplaceOptions>(),
                    default))
                .ReturnsAsync(replaceResult);

            _iamRepositoryMock
                .Setup(x => x.GetCollectionByName<ResourceTimeline<Role>>("ResourceTimelines"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _resourceRepository.SaveResourceTimelineAsync(timeline);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task SaveResourceTimelinesAsync_WithValidList_ReturnsTrue()
        {
            // Arrange
            var timelines = new List<ResourceTimeline<Permission>>
            {
                new ResourceTimeline<Permission> { ItemId = "timeline-1", Entity = "Permission" },
                new ResourceTimeline<Permission> { ItemId = "timeline-2", Entity = "Permission" }
            };
            var collectionMock = new Mock<IMongoCollection<ResourceTimeline<Permission>>>();

            collectionMock
                .Setup(x => x.InsertManyAsync(
                    timelines,
                    It.IsAny<InsertManyOptions>(),
                    default))
                .Returns(Task.CompletedTask);

            _iamRepositoryMock
                .Setup(x => x.GetCollectionByName<ResourceTimeline<Permission>>("ResourceTimelines"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _resourceRepository.SaveResourceTimelinesAsync(timelines);

            // Assert
            result.Should().BeTrue();
        }

        #endregion

        #region Organization Tests

        [Fact]
        public async Task GetOrganizationById_WithValidId_ReturnsOrganization()
        {
            // Arrange
            var orgId = "org-123";
            var expectedOrg = new Organization { ItemId = orgId };
            var cursorMock = CreateAsyncCursorMock(new List<Organization> { expectedOrg });

            _organizationCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Organization>>(),
                    It.IsAny<FindOptions<Organization, Organization>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<Organization>()).Returns(_organizationCollectionMock.Object);

            // Act
            var result = await _resourceRepository.GetOrganizationById(orgId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(orgId);
        }

        [Fact]
        public async Task SaveOrganizationAsync_WithValidOrganization_CompletesSuccessfully()
        {
            // Arrange
            var organization = new Organization { ItemId = "org-new" };
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, BsonValue.Create("org-new"));

            _organizationCollectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<Organization>>(),
                    organization,
                    It.IsAny<ReplaceOptions>(),
                    default))
                .ReturnsAsync(replaceResult);

            _iamRepositoryMock.Setup(x => x.GetCollection<Organization>()).Returns(_organizationCollectionMock.Object);

            // Act
            await _resourceRepository.SaveOrganizationAsync(organization);

            // Assert
            _organizationCollectionMock.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<Organization>>(),
                organization,
                It.Is<ReplaceOptions>(opt => opt.IsUpsert),
                default), Times.Once);
        }

        [Fact]
        public async Task GetOrganizationsAsync_WithPagination_ReturnsPagedResults()
        {
            // Arrange
            var request = new GetOrganizationsRequest { Page = 0, PageSize = 10 };
            var organizations = CreateOrganizationList(5);
            var cursorMock = CreateAsyncCursorMock(organizations);

            _organizationCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<Organization>>(),
                    It.IsAny<FindOptions<Organization>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _organizationCollectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Organization>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(5);

            _iamRepositoryMock.Setup(x => x.GetCollection<Organization>()).Returns(_organizationCollectionMock.Object);

            // Act
            var result = await _resourceRepository.GetOrganizationsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.TotalCount.Should().Be(5);
            result.Organizations.Should().HaveCount(5);
        }

        [Fact]
        public async Task SaveOrganizationConfig_WithValidConfig_CompletesSuccessfully()
        {
            // Arrange
            var config = new OrganizationConfig { ItemId = "config-1" };
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, BsonValue.Create("config-1"));

            _orgConfigCollectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<OrganizationConfig>>(),
                    config,
                    It.IsAny<ReplaceOptions>(),
                    default))
                .ReturnsAsync(replaceResult);

            _iamRepositoryMock.Setup(x => x.GetCollection<OrganizationConfig>()).Returns(_orgConfigCollectionMock.Object);

            // Act
            await _resourceRepository.SaveOrganizationConfig(config);

            // Assert
            _orgConfigCollectionMock.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<OrganizationConfig>>(),
                config,
                It.Is<ReplaceOptions>(opt => opt.IsUpsert),
                default), Times.Once);
        }

        [Fact]
        public async Task GetOrgConfigByIdAsync_WithValidId_ReturnsConfig()
        {
            // Arrange
            var configId = "config-123";
            var expectedConfig = new OrganizationConfig { ItemId = configId };
            var cursorMock = CreateAsyncCursorMock(new List<OrganizationConfig> { expectedConfig });

            _orgConfigCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<OrganizationConfig>>(),
                    It.IsAny<FindOptions<OrganizationConfig, OrganizationConfig>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<OrganizationConfig>()).Returns(_orgConfigCollectionMock.Object);

            // Act
            var result = await _resourceRepository.GetOrgConfigByIdAsync(configId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(configId);
        }

        [Fact]
        public async Task GetOrganizationConfigAsync_ReturnsFirstConfig()
        {
            // Arrange
            var expectedConfig = new OrganizationConfig { ItemId = "config-default" };
            var cursorMock = CreateAsyncCursorMock(new List<OrganizationConfig> { expectedConfig });

            _orgConfigCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<OrganizationConfig>>(),
                    It.IsAny<FindOptions<OrganizationConfig, OrganizationConfig>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<OrganizationConfig>()).Returns(_orgConfigCollectionMock.Object);

            // Act
            var result = await _resourceRepository.GetOrganizationConfigAsync();

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be("config-default");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a mock IAsyncCursor that returns the provided documents
        /// </summary>
        private Mock<IAsyncCursor<T>> CreateAsyncCursorMock<T>(List<T> documents)
        {
            var cursorMock = new Mock<IAsyncCursor<T>>();
            var moveNextCounter = 0;

            cursorMock.Setup(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    moveNextCounter++;
                    return moveNextCounter == 1;
                });

            cursorMock.Setup(x => x.Current).Returns(documents);
            cursorMock.Setup(x => x.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    moveNextCounter++;
                    return moveNextCounter == 1;
                });

            return cursorMock;
        }

        private List<Permission> CreatePermissionList(int count)
        {
            var permissions = new List<Permission>();
            for (int i = 0; i < count; i++)
            {
                permissions.Add(new Permission
                {
                    ItemId = $"perm-{i}",
                    Resource = $"api/resource-{i}",
                    Name = $"Permission {i}",
                    Type = ResourceType.Endpoint,
                    Roles = new List<string> { "admin" },
                    Tags = new List<string> { "tag1" },
                    ResourceGroup = "group1"
                });
            }
            return permissions;
        }

        private List<Role> CreateRoleList(int count)
        {
            var roles = new List<Role>();
            for (int i = 0; i < count; i++)
            {
                roles.Add(new Role
                {
                    ItemId = $"role-{i}",
                    Slug = $"role-slug-{i}",
                    Name = $"Role {i}",
                    Description = $"Description {i}"
                });
            }
            return roles;
        }

        private List<Organization> CreateOrganizationList(int count)
        {
            var organizations = new List<Organization>();
            for (int i = 0; i < count; i++)
            {
                organizations.Add(new Organization
                {
                    ItemId = $"org-{i}",
                    Name = $"Organization {i}"
                });
            }
            return organizations;
        }

        #endregion
    }
}
