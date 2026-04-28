using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Services
{
    public class IdentityAccessManagementRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProviderMock;
        private readonly IdentityAccessManagementRepository _repository;

        public IdentityAccessManagementRepositoryTests()
        {
            _dbContextProviderMock = new Mock<IDbContextProvider>();
            _repository = new IdentityAccessManagementRepository(_dbContextProviderMock.Object);
        }

        #region GetIamConfigurationAsync Tests

        [Fact]
        public async Task GetIamConfigurationAsync_ReturnsFirstConfiguration()
        {
            // Arrange
            var expectedConfig = new IamConfiguration { ItemId = ObjectId.Parse("507f1f77bcf86cd799439011") };
            var collectionMock = new Mock<IMongoCollection<IamConfiguration>>();
            var cursorMock = CreateAsyncCursorMock(new List<IamConfiguration> { expectedConfig });

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<IamConfiguration>>(),
                    It.IsAny<FindOptions<IamConfiguration, IamConfiguration>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<IamConfiguration>("IamConfigurations"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetIamConfigurationAsync();

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(ObjectId.Parse("507f1f77bcf86cd799439011"));
        }

        [Fact]
        public async Task GetIamConfigurationAsync_WithNoConfig_ReturnsNull()
        {
            // Arrange
            var collectionMock = new Mock<IMongoCollection<IamConfiguration>>();
            var cursorMock = CreateAsyncCursorMock(new List<IamConfiguration>());

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<IamConfiguration>>(),
                    It.IsAny<FindOptions<IamConfiguration, IamConfiguration>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<IamConfiguration>("IamConfigurations"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetIamConfigurationAsync();

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region User Retrieval Tests

        [Fact]
        public async Task GetUserByEmailAsync_WithValidEmail_ReturnsUser()
        {
            // Arrange
            var email = "test@example.com";
            var expectedUser = new User { ItemId = "user-1", Email = email };
            var collectionMock = new Mock<IMongoCollection<User>>();
            var cursorMock = CreateAsyncCursorMock(new List<User> { expectedUser });

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<FindOptions<User, User>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<User>("Users"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetUserByEmailAsync(email);

            // Assert
            result.Should().NotBeNull();
            result.Email.Should().Be(email);
        }

        [Fact]
        public async Task GetUserByIdAsync_WithValidId_ReturnsUser()
        {
            // Arrange
            var userId = "user-123";
            var expectedUser = new User { ItemId = userId, Email = "user@test.com" };
            var collectionMock = new Mock<IMongoCollection<User>>();
            var cursorMock = CreateAsyncCursorMock(new List<User> { expectedUser });

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<FindOptions<User, User>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<User>("Users"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetUserByIdAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(userId);
        }

        [Fact]
        public async Task GetUserByIdAsync_Generic_WithValidId_ReturnsProjectedType()
        {
            // Arrange
            var userId = "user-456";
            var userDto = new UserDto { ItemId = userId, Email = "dto@test.com" };
            var collectionMock = new Mock<IMongoCollection<User>>();
            var cursorMock = CreateAsyncCursorMock(new List<UserDto> { userDto });

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<FindOptions<User, UserDto>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<User>("Users"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetUserByIdAsync<UserDto>(userId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(userId);
        }

        #endregion

        #region Password BlackList Tests

        [Fact]
        public async Task CheckPasswordBlackListedAsync_WithBlacklistedPassword_ReturnsTrue()
        {
            // Arrange
            var password = "blacklisted123";
            var tenantId = "tenant-1";
            var collectionMock = new Mock<IMongoCollection<BlackListInformation>>();

            collectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BlackListInformation>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            _dbContextProviderMock.Setup(x => x.GetCollection<BlackListInformation>(tenantId, "BlackListInformations"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.CheckPasswordBlackListedAsync(password, tenantId);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_WithNonBlacklistedPassword_ReturnsFalse()
        {
            // Arrange
            var password = "safe-password";
            var tenantId = "tenant-2";
            var collectionMock = new Mock<IMongoCollection<BlackListInformation>>();

            collectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BlackListInformation>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _dbContextProviderMock.Setup(x => x.GetCollection<BlackListInformation>(tenantId, "BlackListInformations"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.CheckPasswordBlackListedAsync(password, tenantId);

            // Assert
            result.Should().BeFalse();
        }

        [Theory]
        [InlineData("tenant-001")]
        [InlineData("tenant-002")]
        [InlineData("multi-org-tenant")]
        public async Task CheckPasswordBlackListedAsync_WithDifferentTenants_UsesCorrectTenant(string tenantId)
        {
            // Arrange
            var password = "test-password";
            var collectionMock = new Mock<IMongoCollection<BlackListInformation>>();

            collectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BlackListInformation>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _dbContextProviderMock.Setup(x => x.GetCollection<BlackListInformation>(tenantId, "BlackListInformations"))
                .Returns(collectionMock.Object);

            // Act
            await _repository.CheckPasswordBlackListedAsync(password, tenantId);

            // Assert
            _dbContextProviderMock.Verify(x => x.GetCollection<BlackListInformation>(tenantId, "BlackListInformations"), Times.Once);
        }

        #endregion

        #region UserKeyMap Tests

        [Fact]
        public async Task InsertUserKeyMapAsync_WithValidKeyMap_ReturnsTrue()
        {
            // Arrange
            var userKeyMap = new UserKeyMap { UserId = "user-1", Key = "key-123" };
            var collectionMock = new Mock<IMongoCollection<UserKeyMap>>();

            collectionMock
                .Setup(x => x.InsertOneAsync(userKeyMap, null, default))
                .Returns(Task.CompletedTask);

            _dbContextProviderMock.Setup(x => x.GetCollection<UserKeyMap>("UserKeyMaps"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.InsertUserKeyMapAsync(userKeyMap);

            // Assert
            result.Should().BeTrue();
            collectionMock.Verify(x => x.InsertOneAsync(userKeyMap, null, default), Times.Once);
        }

        [Fact]
        public async Task UpdateUserKeyMapActivationAsync_WithValidUserId_ReturnsTrue()
        {
            // Arrange
            var userId = "user-789";
            var collectionMock = new Mock<IMongoCollection<UserKeyMap>>();
            var updateResult = new UpdateResult.Acknowledged(1, 1, BsonValue.Create(1));

            collectionMock
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<UserKeyMap>>(),
                    It.IsAny<UpdateDefinition<UserKeyMap>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .ReturnsAsync(updateResult);

            _dbContextProviderMock.Setup(x => x.GetCollection<UserKeyMap>("UserKeyMaps"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.UpdateUserKeyMapActivationAsync(userId);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task GetActiveUserKeyMapAsync_WithValidUserId_ReturnsActiveMaps()
        {
            // Arrange
            var userId = "user-active";
            var activeKeyMaps = new List<UserKeyMap>
            {
                new UserKeyMap { UserId = userId, Key = "key-1", Activated = false },
                new UserKeyMap { UserId = userId, Key = "key-2", Activated = false }
            };
            var collectionMock = new Mock<IMongoCollection<UserKeyMap>>();
            var cursorMock = CreateAsyncCursorMock(activeKeyMaps);

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<UserKeyMap>>(),
                    It.IsAny<FindOptions<UserKeyMap, UserKeyMap>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<UserKeyMap>("UserKeyMaps"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetActiveUserKeyMapAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetUserIdFromKeyMapByKeyAsync_WithActiveUser_ReturnsEmptyString()
        {
            // Arrange
            var key = "activation-key-1";
            var userId = "user-active";
            var activeUser = new User { ItemId = userId, Active = true };
            
            var keyMapCollectionMock = new Mock<IMongoCollection<UserKeyMap>>();
            var userCollectionMock = new Mock<IMongoCollection<User>>();
            var keyMapCursorMock = CreateAsyncCursorMock(new List<string> { userId });
            var userCursorMock = CreateAsyncCursorMock(new List<User> { activeUser });

            keyMapCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<UserKeyMap>>(),
                    It.IsAny<FindOptions<UserKeyMap, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(keyMapCursorMock.Object);

            userCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<FindOptions<User, User>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(userCursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<UserKeyMap>("UserKeyMaps"))
                .Returns(keyMapCollectionMock.Object);
            _dbContextProviderMock.Setup(x => x.GetCollection<User>("Users"))
                .Returns(userCollectionMock.Object);

            // Act
            var result = await _repository.GetUserIdFromKeyMapByKeyAsync(key);

            // Assert
            result.Should().Be("");
        }

        [Fact]
        public async Task GetUserIdFromKeyMapByKeyAsync_WithInactiveUser_ReturnsUserId()
        {
            // Arrange
            var key = "activation-key-2";
            var userId = "user-inactive";
            var inactiveUser = new User { ItemId = userId, Active = false };
            
            var keyMapCollectionMock = new Mock<IMongoCollection<UserKeyMap>>();
            var userCollectionMock = new Mock<IMongoCollection<User>>();
            var keyMapCursorMock = CreateAsyncCursorMock(new List<string> { userId });
            var userCursorMock = CreateAsyncCursorMock(new List<User> { inactiveUser });

            keyMapCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<UserKeyMap>>(),
                    It.IsAny<FindOptions<UserKeyMap, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(keyMapCursorMock.Object);

            userCollectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<FindOptions<User, User>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(userCursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<UserKeyMap>("UserKeyMaps"))
                .Returns(keyMapCollectionMock.Object);
            _dbContextProviderMock.Setup(x => x.GetCollection<User>("Users"))
                .Returns(userCollectionMock.Object);

            // Act
            var result = await _repository.GetUserIdFromKeyMapByKeyAsync(key);

            // Assert
            result.Should().Be(userId);
        }
        #endregion

        #region User Mutation Tests

        [Fact]
        public async Task InsertUserTimelineAsync_WithValidTimeline_ReturnsTrue()
        {
            // Arrange
            var timeline = new UserTimeline { ItemId = "timeline-1" };
            var collectionMock = new Mock<IMongoCollection<UserTimeline>>();

            collectionMock
                .Setup(x => x.InsertOneAsync(timeline, null, default))
                .Returns(Task.CompletedTask);

            _dbContextProviderMock.Setup(x => x.GetCollection<UserTimeline>("UserTimelines"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.InsertUserTimelineAsync(timeline);

            // Assert
            result.Should().BeTrue();
            collectionMock.Verify(x => x.InsertOneAsync(timeline, null, default), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_WithValidUser_ReturnsTrue()
        {
            // Arrange
            var user = new User { ItemId = "user-update", Email = "update@test.com" };
            var collectionMock = new Mock<IMongoCollection<User>>();
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, BsonValue.Create("user-update"));

            collectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    user,
                    It.IsAny<ReplaceOptions>(),
                    default))
                .ReturnsAsync(replaceResult);

            _dbContextProviderMock.Setup(x => x.GetCollection<User>("Users"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.UpdateUserAsync(user);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateUserAsync_WithUnacknowledgedResult_ReturnsFalse()
        {
            // Arrange
            var user = new User { ItemId = "user-fail", Email = "fail@test.com" };
            var collectionMock = new Mock<IMongoCollection<User>>();
            var replaceResult = ReplaceOneResult.Unacknowledged.Instance;

            collectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    user,
                    It.IsAny<ReplaceOptions>(),
                    default))
                .ReturnsAsync(replaceResult);

            _dbContextProviderMock.Setup(x => x.GetCollection<User>("Users"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.UpdateUserAsync(user);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region SignUpSetting Tests

        [Fact]
        public async Task GetSingUpSettingByIdAsync_WithValidId_ReturnsSetting()
        {
            // Arrange
            var settingId = "setting-123";
            var expectedSetting = new SignUpSetting { ItemId = settingId };
            var collectionMock = new Mock<IMongoCollection<SignUpSetting>>();
            var cursorMock = CreateAsyncCursorMock(new List<SignUpSetting> { expectedSetting });

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SignUpSetting>>(),
                    It.IsAny<FindOptions<SignUpSetting, SignUpSetting>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<SignUpSetting>("SignUpSettings"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetSingUpSettingByIdAsync(settingId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(settingId);
        }

        [Fact]
        public async Task SaveSingUpSettingAsync_WithValidSetting_UpsertsSetting()
        {
            // Arrange
            var setting = new SignUpSetting { ItemId = "setting-new" };
            var collectionMock = new Mock<IMongoCollection<SignUpSetting>>();
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, BsonValue.Create("setting-new"));

            collectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<SignUpSetting>>(),
                    setting,
                    It.IsAny<ReplaceOptions>(),
                    default))
                .ReturnsAsync(replaceResult);

            _dbContextProviderMock.Setup(x => x.GetCollection<SignUpSetting>("SignUpSettings"))
                .Returns(collectionMock.Object);

            // Act
            await _repository.SaveSingUpSettingAsync(setting);

            // Assert
            collectionMock.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<SignUpSetting>>(),
                setting,
                It.Is<ReplaceOptions>(opt => opt.IsUpsert),
                default), Times.Once);
        }

        [Fact]
        public async Task GetSignUpSettingAsync_WithItemId_ReturnsSpecificSetting()
        {
            // Arrange
            var itemId = "setting-456";
            var expectedSetting = new SignUpSetting { ItemId = itemId };
            var collectionMock = new Mock<IMongoCollection<SignUpSetting>>();
            var cursorMock = CreateAsyncCursorMock(new List<SignUpSetting> { expectedSetting });

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SignUpSetting>>(),
                    It.IsAny<FindOptions<SignUpSetting, SignUpSetting>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<SignUpSetting>("SignUpSettings"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetSignUpSettingAsync(itemId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(itemId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetSignUpSettingAsync_WithNullOrEmptyItemId_ReturnsFirstSetting(string itemId)
        {
            // Arrange
            var expectedSetting = new SignUpSetting { ItemId = "default-setting" };
            var collectionMock = new Mock<IMongoCollection<SignUpSetting>>();
            var cursorMock = CreateAsyncCursorMock(new List<SignUpSetting> { expectedSetting });

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SignUpSetting>>(),
                    It.IsAny<FindOptions<SignUpSetting, SignUpSetting>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<SignUpSetting>("SignUpSettings"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetSignUpSettingAsync(itemId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be("default-setting");
        }

        [Fact]
        public async Task SingnUpSettingAlreadyExist_WithExistingSettings_ReturnsTrue()
        {
            // Arrange
            var collectionMock = new Mock<IMongoCollection<SignUpSetting>>();

            collectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<SignUpSetting>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            _dbContextProviderMock.Setup(x => x.GetCollection<SignUpSetting>("SignUpSettings"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.SingnUpSettingAlreadyExist();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task SingnUpSettingAlreadyExist_WithNoSettings_ReturnsFalse()
        {
            // Arrange
            var collectionMock = new Mock<IMongoCollection<SignUpSetting>>();

            collectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<SignUpSetting>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _dbContextProviderMock.Setup(x => x.GetCollection<SignUpSetting>("SignUpSettings"))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.SingnUpSettingAlreadyExist();

            // Assert
            result.Should().BeFalse();
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

            return cursorMock;
        }

        // Test DTO for projection tests
        public class UserDto
        {
            public string ItemId { get; set; }
            public string Email { get; set; }
        }

        #endregion
    }
}
