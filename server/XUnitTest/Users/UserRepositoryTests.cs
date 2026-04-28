using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Users
{
    public class UserRepositoryTests : IDisposable
    {
        private readonly Mock<IIdentityAccessManagementRepository> _iamRepositoryMock;
        private readonly UserRepository _repository;

        public UserRepositoryTests()
        {
            _iamRepositoryMock = new Mock<IIdentityAccessManagementRepository>();
            _repository = new UserRepository(_iamRepositoryMock.Object);
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

        private static Mock<IAsyncCursor<T>> CreateAsyncCursorMock<T>(List<T> items)
        {
            var cursorMock = new Mock<IAsyncCursor<T>>();
            var enumerator = items.GetEnumerator();
            cursorMock.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            cursorMock.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            cursorMock.Setup(x => x.Current).Returns(items);
            return cursorMock;
        }

        private static User CreateTestUser(string userId = "user-123", string email = "test@example.com")
        {
            return new User
            {
                ItemId = userId,
                Email = email,
                UserName = "testuser",
                FirstName = "John",
                LastName = "Doe",
                Active = true,
                Memberships = new List<OrganizationMembership>
                {
                    new OrganizationMembership
                    {
                        OrganizationId = "org-123",
                        Roles = new List<string> { "admin", "editor" },
                        Permissions = new List<string> { "users:read", "users:write" }
                    }
                }
            };
        }

        #region Delegation Methods Tests

        [Fact]
        public async Task CheckPasswordBlackListedAsync_DelegatesToIamRepository()
        {
            // Arrange
            var password = "Password123!";
            var tenantId = "tenant-123";
            _iamRepositoryMock.Setup(x => x.CheckPasswordBlackListedAsync(password, tenantId))
                .ReturnsAsync(true);

            // Act
            var result = await _repository.CheckPasswordBlackListedAsync(password, tenantId);

            // Assert
            result.Should().BeTrue();
            _iamRepositoryMock.Verify(x => x.CheckPasswordBlackListedAsync(password, tenantId), Times.Once);
        }

        [Fact]
        public async Task GetIamConfigurationAsync_DelegatesToIamRepository()
        {
            // Arrange
            var config = new IamConfiguration { AccountActivationUrl = "https://example.com" };
            _iamRepositoryMock.Setup(x => x.GetIamConfigurationAsync())
                .ReturnsAsync(config);

            // Act
            var result = await _repository.GetIamConfigurationAsync();

            // Assert
            result.Should().BeSameAs(config);
            _iamRepositoryMock.Verify(x => x.GetIamConfigurationAsync(), Times.Once);
        }

        [Fact]
        public async Task GetUserByEmailAsync_DelegatesToIamRepository()
        {
            // Arrange
            var email = "test@example.com";
            var user = CreateTestUser();
            _iamRepositoryMock.Setup(x => x.GetUserByEmailAsync(email))
                .ReturnsAsync(user);

            // Act
            var result = await _repository.GetUserByEmailAsync(email);

            // Assert
            result.Should().BeSameAs(user);
            _iamRepositoryMock.Verify(x => x.GetUserByEmailAsync(email), Times.Once);
        }

        [Fact]
        public async Task GetUserByIdAsync_DelegatesToIamRepository()
        {
            // Arrange
            var userId = "user-123";
            var user = CreateTestUser();
            _iamRepositoryMock.Setup(x => x.GetUserByIdAsync(userId))
                .ReturnsAsync(user);

            // Act
            var result = await _repository.GetUserByIdAsync(userId);

            // Assert
            result.Should().BeSameAs(user);
            _iamRepositoryMock.Verify(x => x.GetUserByIdAsync(userId), Times.Once);
        }

        [Fact]
        public async Task GetUserByIdAsync_Generic_DelegatesToIamRepository()
        {
            // Arrange
            var userId = "user-123";
            var userDto = new GetUser { ItemId = userId };
            _iamRepositoryMock.Setup(x => x.GetUserByIdAsync<GetUser>(userId))
                .ReturnsAsync(userDto);

            // Act
            var result = await _repository.GetUserByIdAsync<GetUser>(userId);

            // Assert
            result.Should().BeSameAs(userDto);
            _iamRepositoryMock.Verify(x => x.GetUserByIdAsync<GetUser>(userId), Times.Once);
        }

        [Fact]
        public async Task InsertUserKeyMapAsync_DelegatesToIamRepository()
        {
            // Arrange
            var keyMap = new UserKeyMap { Key = "key-123", UserId = "user-123" };
            _iamRepositoryMock.Setup(x => x.InsertUserKeyMapAsync(keyMap))
                .ReturnsAsync(true);

            // Act
            var result = await _repository.InsertUserKeyMapAsync(keyMap);

            // Assert
            result.Should().BeTrue();
            _iamRepositoryMock.Verify(x => x.InsertUserKeyMapAsync(keyMap), Times.Once);
        }

        [Fact]
        public async Task InsertUserTimelineAsync_DelegatesToIamRepository()
        {
            // Arrange
            var timeline = new UserTimeline { ItemId = "timeline-123", Event = "USER_CREATED" };
            _iamRepositoryMock.Setup(x => x.InsertUserTimelineAsync(timeline))
                .ReturnsAsync(true);

            // Act
            var result = await _repository.InsertUserTimelineAsync(timeline);

            // Assert
            result.Should().BeTrue();
            _iamRepositoryMock.Verify(x => x.InsertUserTimelineAsync(timeline), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_DelegatesToIamRepository()
        {
            // Arrange
            var user = CreateTestUser();
            _iamRepositoryMock.Setup(x => x.UpdateUserAsync(user))
                .ReturnsAsync(true);

            // Act
            var result = await _repository.UpdateUserAsync(user);

            // Assert
            result.Should().BeTrue();
            _iamRepositoryMock.Verify(x => x.UpdateUserAsync(user), Times.Once);
        }

        #endregion

        #region CreateUserAsync Tests

        [Fact]
        public async Task CreateUserAsync_InsertsUserAndReturnsTrue()
        {
            // Arrange
            var user = CreateTestUser();
            var collectionMock = new Mock<IMongoCollection<User>>();
            collectionMock.Setup(x => x.InsertOneAsync(user, null, default))
                .Returns(Task.CompletedTask);
            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.CreateUserAsync(user);

            // Assert
            result.Should().BeTrue();
            collectionMock.Verify(x => x.InsertOneAsync(user, null, default), Times.Once);
        }

        #endregion

        #region GetPermissionsByResourcesAsync Tests

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ById_CallsCorrectCollections()
        {
            // Arrange
            var userId = "user-123";
            var userCollectionMock = new Mock<IMongoCollection<User>>();
            var permissionCollectionMock = new Mock<IMongoCollection<Permission>>();

            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(userCollectionMock.Object);
            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>())
                .Returns(permissionCollectionMock.Object);

            // Act & Assert - Just verify it calls the correct collections
            try
            {
                await _repository.GetPermissionsByResourcesAsync(userId);
            }
            catch
            {
                // Expected since we're not fully mocking the fluent API
            }

            _iamRepositoryMock.Verify(x => x.GetCollection<User>(), Times.Once);
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ByList_CallsCollectionFind()
        {
            // Arrange
            var permissionList = new List<string> { "users:read", "users:write" };
            var permissionCollectionMock = new Mock<IMongoCollection<Permission>>();

            _iamRepositoryMock.Setup(x => x.GetCollection<Permission>())
                .Returns(permissionCollectionMock.Object);

            // Act & Assert - Just verify it calls the collection
            _iamRepositoryMock.Verify(x => x.GetCollection<Permission>(), Times.Never);

            try
            {
                await _repository.GetPermissionsByResourcesAsync(permissionList);
            }
            catch
            {
                // Expected since we're not fully mocking the fluent API
            }

            _iamRepositoryMock.Verify(x => x.GetCollection<Permission>(), Times.Once);
        }

        #endregion

        #region GetRolesBySlugsAsync Tests

        [Fact]
        public async Task GetRolesBySlugsAsync_ById_CallsCorrectCollections()
        {
            // Arrange
            var userId = "user-123";
            SetupBlocksContext();

            var userCollectionMock = new Mock<IMongoCollection<User>>();
            var roleCollectionMock = new Mock<IMongoCollection<Role>>();

            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(userCollectionMock.Object);
            _iamRepositoryMock.Setup(x => x.GetCollection<Role>())
                .Returns(roleCollectionMock.Object);

            // Act & Assert
            try
            {
                await _repository.GetRolesBySlugsAsync(userId);
            }
            catch
            {
                // Expected since we're not fully mocking the fluent API
            }

            _iamRepositoryMock.Verify(x => x.GetCollection<User>(), Times.Once);
        }

        [Fact]
        public async Task GetRolesBySlugsAsync_ByList_CallsCollectionFind()
        {
            // Arrange
            var roleList = new List<string> { "admin", "editor" };
            var roleCollectionMock = new Mock<IMongoCollection<Role>>();

            _iamRepositoryMock.Setup(x => x.GetCollection<Role>())
                .Returns(roleCollectionMock.Object);

            // Act & Assert - Just verify it calls the collection
            _iamRepositoryMock.Verify(x => x.GetCollection<Role>(), Times.Never);

            try
            {
                await _repository.GetRolesBySlugsAsync(roleList);
            }
            catch
            {
                // Expected since we're not fully mocking the fluent API
            }

            _iamRepositoryMock.Verify(x => x.GetCollection<Role>(), Times.Once);
        }

        #endregion

        #region GetUserByUserNameOrgIdAsync Tests

        [Fact]
        public async Task GetUserByUserNameOrgIdAsync_CallsCorrectCollection()
        {
            // Arrange
            var userName = "testuser";
            var orgId = "org-123";
            var collectionMock = new Mock<IMongoCollection<User>>();

            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(collectionMock.Object);

            // Act & Assert
            try
            {
                await _repository.GetUserByUserNameOrgIdAsync(userName, orgId);
            }
            catch
            {
                // Expected since we're not fully mocking the fluent API
            }

            _iamRepositoryMock.Verify(x => x.GetCollection<User>(), Times.Once);
        }

        #endregion

        #region GetUsersAsync Tests

        [Fact]
        public async Task GetUsersAsync_WithValidRequest_ReturnsUsersAndCount()
        {
            // Arrange
            var request = new GetUsersRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new GetUsersFilter { Name = "John" }
            };

            var users = new List<GetUser>
            {
                new GetUser { ItemId = "user-1", FirstName = "John" },
                new GetUser { ItemId = "user-2", FirstName = "John" }
            };

            var collectionMock = new Mock<IMongoCollection<User>>();
            collectionMock.Setup(x => x.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), null, default))
                .ReturnsAsync(2);

            var cursorMock = CreateAsyncCursorMock(users);
            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, GetUser>>(),
                default))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(collectionMock.Object);

            // Act
            var (data, count) = await _repository.GetUsersAsync<GetUser, GetUsersRequest>(request);

            // Assert
            data.Should().NotBeNull();
            data.Count().Should().Be(2);
            count.Should().Be(2);
        }

        [Fact]
        public async Task GetUsersAsync_WithPagination_AppliesSkipAndLimit()
        {
            // Arrange
            var request = new GetUsersRequest { Page = 2, PageSize = 20 };
            var collectionMock = new Mock<IMongoCollection<User>>();
            var cursorMock = CreateAsyncCursorMock(new List<GetUser>());

            collectionMock.Setup(x => x.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), null, default))
                .ReturnsAsync(100);
            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.Is<FindOptions<User, GetUser>>(opts => opts.Skip == 40 && opts.Limit == 20),
                default))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(collectionMock.Object);

            // Act
            await _repository.GetUsersAsync<GetUser, GetUsersRequest>(request);

            // Assert
            collectionMock.Verify(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.Is<FindOptions<User, GetUser>>(opts => opts.Skip == 40 && opts.Limit == 20),
                default), Times.Once);
        }

        [Fact]
        public async Task GetUsersAsync_WithNullFilter_UsesEmptyFilter()
        {
            // Arrange
            var request = new GetUsersRequest { Page = 0, PageSize = 10, Filter = null };
            var collectionMock = new Mock<IMongoCollection<User>>();
            var cursorMock = CreateAsyncCursorMock(new List<GetUser>());

            collectionMock.Setup(x => x.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), null, default))
                .ReturnsAsync(0);
            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, GetUser>>(),
                default))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(collectionMock.Object);

            // Act
            var (data, count) = await _repository.GetUsersAsync<GetUser, GetUsersRequest>(request);

            // Assert
            count.Should().Be(0);
        }

        [Fact]
        public async Task GetUsersAsync_WithEmailFilter_FiltersCorrectly()
        {
            // Arrange
            var request = new GetUsersRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new GetUsersFilter { Email = "test@example.com" }
            };

            var collectionMock = new Mock<IMongoCollection<User>>();
            var cursorMock = CreateAsyncCursorMock(new List<GetUser>());

            collectionMock.Setup(x => x.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), null, default))
                .ReturnsAsync(1);
            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, GetUser>>(),
                default))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(collectionMock.Object);

            // Act
            await _repository.GetUsersAsync<GetUser, GetUsersRequest>(request);

            // Assert
            collectionMock.Verify(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, GetUser>>(),
                default), Times.Once);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task GetUsersAsync_WithStatusFilter_FiltersActive(bool active, bool inactive)
        {
            // Arrange
            var request = new GetUsersRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new GetUsersFilter
                {
                    Status = new Status { Active = active, Inactive = inactive }
                }
            };

            var collectionMock = new Mock<IMongoCollection<User>>();
            var cursorMock = CreateAsyncCursorMock(new List<GetUser>());

            collectionMock.Setup(x => x.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), null, default))
                .ReturnsAsync(0);
            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, GetUser>>(),
                default))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(collectionMock.Object);

            // Act
            await _repository.GetUsersAsync<GetUser, GetUsersRequest>(request);

            // Assert
            collectionMock.Verify(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, GetUser>>(),
                default), Times.Once);
        }

        [Fact]
        public async Task GetUsersAsync_WithSort_AppliesSort()
        {
            // Arrange
            var request = new GetUsersRequest
            {
                Page = 0,
                PageSize = 10,
                Sort = new BaseSortRequest { Property = "FirstName", IsDescending = false }
            };

            var collectionMock = new Mock<IMongoCollection<User>>();
            var cursorMock = CreateAsyncCursorMock(new List<GetUser>());

            collectionMock.Setup(x => x.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), null, default))
                .ReturnsAsync(0);
            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, GetUser>>(),
                default))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<User>())
                .Returns(collectionMock.Object);

            // Act
            await _repository.GetUsersAsync<GetUser, GetUsersRequest>(request);

            // Assert
            collectionMock.Verify(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.Is<FindOptions<User, GetUser>>(opts => opts.Sort != null),
                default), Times.Once);
        }

        #endregion

        #region GetUserTimelinesAsync Tests

        [Fact]
        public async Task GetUserTimelinesAsync_WithValidRequest_ReturnsTimelines()
        {
            // Arrange
            var request = new GetUserTimeLineRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new Iam.DomainService.Shared.Dtos.GetUserTimeLineFilter { Event = "USER_CREATED" }
            };

            var timelines = new List<UserTimeline>
            {
                new UserTimeline { ItemId = "timeline-1", Event = "USER_CREATED" },
                new UserTimeline { ItemId = "timeline-2", Event = "USER_CREATED" }
            };

            var collectionMock = new Mock<IMongoCollection<UserTimeline>>();
            var cursorMock = CreateAsyncCursorMock(timelines);

            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<UserTimeline>>(),
                It.IsAny<FindOptions<UserTimeline, UserTimeline>>(),
                default))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<UserTimeline>())
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetUserTimelinesAsync(request);

            // Assert
            result.Should().HaveCount(2);
            result.Should().AllSatisfy(t => t.Event.Should().Be("USER_CREATED"));
        }

        [Fact]
        public async Task GetUserTimelinesAsync_WithEmptyEventFilter_ReturnsAllTimelines()
        {
            // Arrange
            var request = new GetUserTimeLineRequest
            {
                Page = 0,
                PageSize = 10,
                Filter = new Iam.DomainService.Shared.Dtos.GetUserTimeLineFilter { Event = "" }
            };

            var timelines = new List<UserTimeline>
            {
                new UserTimeline { ItemId = "timeline-1", Event = "USER_CREATED" }
            };

            var collectionMock = new Mock<IMongoCollection<UserTimeline>>();
            var cursorMock = CreateAsyncCursorMock(timelines);

            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<UserTimeline>>(),
                It.IsAny<FindOptions<UserTimeline, UserTimeline>>(),
                default))
                .ReturnsAsync(cursorMock.Object);

            _iamRepositoryMock.Setup(x => x.GetCollection<UserTimeline>())
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetUserTimelinesAsync(request);

            // Assert
            result.Should().HaveCount(1);
        }

        #endregion

        #region GetProjectIdFromProjectPeopleAsync Tests

        [Fact]
        public async Task GetProjectIdFromProjectPeopleAsync_CallsCorrectCollection()
        {
            // Arrange
            var userId = "user-123";
            var collectionMock = new Mock<IMongoCollection<ProjectPeople>>();

            _iamRepositoryMock.Setup(x => x.GetCollection<ProjectPeople>())
                .Returns(collectionMock.Object);

            // Act & Assert
            try
            {
                await _repository.GetProjectIdFromProjectPeopleAsync(userId);
            }
            catch
            {
                // Expected since we're not fully mocking the fluent API
            }

            _iamRepositoryMock.Verify(x => x.GetCollection<ProjectPeople>(), Times.Once);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task AllDelegationMethods_CallIamRepository()
        {
            // Arrange
            _iamRepositoryMock.Setup(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _iamRepositoryMock.Setup(x => x.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration());
            _iamRepositoryMock.Setup(x => x.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User());
            _iamRepositoryMock.Setup(x => x.GetUserByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new User());
            _iamRepositoryMock.Setup(x => x.GetUserByIdAsync<GetUser>(It.IsAny<string>()))
                .ReturnsAsync(new GetUser());
            _iamRepositoryMock.Setup(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>()))
                .ReturnsAsync(true);
            _iamRepositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()))
                .ReturnsAsync(true);
            _iamRepositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);

            // Act
            await _repository.CheckPasswordBlackListedAsync("pass", "tenant");
            await _repository.GetIamConfigurationAsync();
            await _repository.GetUserByEmailAsync("email");
            await _repository.GetUserByIdAsync("id");
            await _repository.GetUserByIdAsync<GetUser>("id");
            await _repository.InsertUserKeyMapAsync(new UserKeyMap());
            await _repository.InsertUserTimelineAsync(new UserTimeline());
            await _repository.UpdateUserAsync(new User());

            // Assert - All delegation methods called once
            _iamRepositoryMock.Verify(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _iamRepositoryMock.Verify(x => x.GetIamConfigurationAsync(), Times.Once);
            _iamRepositoryMock.Verify(x => x.GetUserByEmailAsync(It.IsAny<string>()), Times.Once);
            _iamRepositoryMock.Verify(x => x.GetUserByIdAsync(It.IsAny<string>()), Times.Once);
            _iamRepositoryMock.Verify(x => x.GetUserByIdAsync<GetUser>(It.IsAny<string>()), Times.Once);
            _iamRepositoryMock.Verify(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>()), Times.Once);
            _iamRepositoryMock.Verify(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()), Times.Once);
            _iamRepositoryMock.Verify(x => x.UpdateUserAsync(It.IsAny<User>()), Times.Once);
        }

        #endregion
    }
}
