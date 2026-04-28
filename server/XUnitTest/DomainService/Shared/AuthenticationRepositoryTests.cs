using Blocks.Genesis;
using DomainService.Entities;
using DomainService.RequestModel;
using DomainService.Services;
using DomainService.Shared.RequestModel;
using FluentAssertions;
using Iam.DomainService.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.DomainService.Shared
{
    public class AuthenticationRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider;
        private readonly AuthenticationRepository _repository;

        public AuthenticationRepositoryTests()
        {
            _dbContextProvider = new Mock<IDbContextProvider>();
            _repository = new AuthenticationRepository(_dbContextProvider.Object);
        }

        #region GetUserByEmailAsync

        [Fact]
        public async Task GetUserByEmailAsync_WithValidEmail_ReturnsUser()
        {
            // Arrange
            var email = "test@example.com";
            var expectedUser = new User { ItemId = "user-1", Email = email };
            var mockCollection = new Mock<IMongoCollection<User>>();
            var mockCursor = new Mock<IAsyncCursor<User>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedUser });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<User>("Users"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserByEmailAsync(email);

            // Assert
            result.Should().NotBeNull();
            result.Email.Should().Be(email);
            mockCollection.Verify(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetUserByEmailAsync_WithNonExistentEmail_ReturnsNull()
        {
            // Arrange
            var email = "nonexistent@example.com";
            var mockCollection = new Mock<IMongoCollection<User>>();
            var mockCursor = new Mock<IAsyncCursor<User>>();

            mockCursor.Setup(x => x.Current).Returns(new List<User>());
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<User>("Users"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserByEmailAsync(email);

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region GetUserByUsernameAsync

        [Fact]
        public async Task GetUserByUsernameAsync_WithoutOrganizationId_ReturnsUser()
        {
            // Arrange
            var username = "testuser";
            var expectedUser = new User { ItemId = "user-1", UserName = username };
            var mockCollection = new Mock<IMongoCollection<User>>();
            var mockCursor = new Mock<IAsyncCursor<User>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedUser });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<User>("Users"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserByUsernameAsync(username);

            // Assert
            result.Should().NotBeNull();
            result.UserName.Should().Be(username);
        }

        [Fact]
        public async Task GetUserByUsernameAsync_WithOrganizationId_ReturnsUserFromOrganization()
        {
            // Arrange
            var username = "testuser";
            var organizationId = "org-123";
            var expectedUser = new User 
            { 
                ItemId = "user-1", 
                UserName = username, 
                OrganizationIds = new List<string> { organizationId } 
            };
            var mockCollection = new Mock<IMongoCollection<User>>();
            var mockCursor = new Mock<IAsyncCursor<User>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedUser });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<User>("Users"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserByUsernameAsync(username, organizationId);

            // Assert
            result.Should().NotBeNull();
            result.UserName.Should().Be(username);
        }

        [Fact]
        public async Task GetUserByUsernameAsync_WithEmptyOrganizationId_ReturnsUser()
        {
            // Arrange
            var username = "testuser";
            var expectedUser = new User { ItemId = "user-1", UserName = username };
            var mockCollection = new Mock<IMongoCollection<User>>();
            var mockCursor = new Mock<IAsyncCursor<User>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedUser });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<User>("Users"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserByUsernameAsync(username, "");

            // Assert
            result.Should().NotBeNull();
        }

        #endregion

        #region GetUserByIdAsync

        [Fact]
        public async Task GetUserByIdAsync_WithValidId_ReturnsUser()
        {
            // Arrange
            var userId = "user-123";
            var expectedUser = new User { ItemId = userId, UserName = "testuser" };
            var mockCollection = new Mock<IMongoCollection<User>>();
            var mockCursor = new Mock<IAsyncCursor<User>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedUser });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<User>("Users"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserByIdAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(userId);
        }

        [Fact]
        public async Task GetUserByIdAsync_Generic_WithValidId_ReturnsProjection()
        {
            // Arrange
            var userId = "user-123";
            var expectedProjection = new { ItemId = userId, UserName = "testuser" };
            var mockCollection = new Mock<IMongoCollection<User>>();
            var mockCursor = new Mock<IAsyncCursor<dynamic>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedProjection });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, dynamic>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<User>("Users"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserByIdAsync<dynamic>(userId);

            // Assert
            ((object)result).Should().NotBeNull();
        }

        #endregion

        #region InsertSessionAsync

        [Fact]
        public async Task InsertSessionAsync_WithValidSession_ReturnsTrue()
        {
            // Arrange
            var session = new Session 
            { 
                ItemId = ObjectId.Parse("507f1f77bcf86cd799439011"), 
                UserId = "user-1", 
                RefreshToken = "token-123" 
            };
            var mockCollection = new Mock<IMongoCollection<Session>>();

            mockCollection.Setup(x => x.InsertOneAsync(
                session,
                null,
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _dbContextProvider.Setup(x => x.GetCollection<Session>("Sessions"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.InsertSessionAsync(session);

            // Assert
            result.Should().BeTrue();
            mockCollection.Verify(x => x.InsertOneAsync(
                session,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region InsertUserAuthenticationTimelineAsync

        [Fact]
        public async Task InsertUserAuthenticationTimelineAsync_WithValidTimeline_ReturnsTrue()
        {
            // Arrange
            var timeline = new UserAuthenticationTimeline 
            { 
                ItemId = "timeline-1",
                Event = "login",
                ActionBy = "user-1"
            };
            var mockCollection = new Mock<IMongoCollection<UserAuthenticationTimeline>>();

            mockCollection.Setup(x => x.InsertOneAsync(
                timeline,
                null,
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _dbContextProvider.Setup(x => x.GetCollection<UserAuthenticationTimeline>("UserAuthenticationTimelines"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.InsertUserAuthenticationTimelineAsync(timeline);

            // Assert
            result.Should().BeTrue();
            mockCollection.Verify(x => x.InsertOneAsync(
                timeline,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region UpdateSessionStatusAsync

        [Fact]
        public async Task UpdateSessionStatusAsync_WithValidTokenAndUser_ReturnsTrue()
        {
            // Arrange
            var refreshToken = "token-123";
            var userId = "user-1";
            var mockCollection = new Mock<IMongoCollection<Session>>();
            var updateResult = new UpdateResult.Acknowledged(1, 1, null);

            mockCollection.Setup(x => x.UpdateManyAsync(
                It.IsAny<FilterDefinition<Session>>(),
                It.IsAny<UpdateDefinition<Session>>(),
                null,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(updateResult);

            _dbContextProvider.Setup(x => x.GetCollection<Session>("Sessions"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.UpdateSessionStatusAsync(refreshToken, userId);

            // Assert
            result.Should().BeTrue();
            mockCollection.Verify(x => x.UpdateManyAsync(
                It.IsAny<FilterDefinition<Session>>(),
                It.IsAny<UpdateDefinition<Session>>(),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region UpdateSessionStatusForAllRefreshTokenAsync

        [Fact]
        public async Task UpdateSessionStatusForAllRefreshTokenAsync_WithMultipleTokens_ReturnsTrue()
        {
            // Arrange
            var refreshTokens = new[] { "token-1", "token-2", "token-3" };
            var mockCollection = new Mock<IMongoCollection<Session>>();
            var updateResult = new UpdateResult.Acknowledged(3, 3, null);

            mockCollection.Setup(x => x.UpdateManyAsync(
                It.IsAny<FilterDefinition<Session>>(),
                It.IsAny<UpdateDefinition<Session>>(),
                null,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(updateResult);

            _dbContextProvider.Setup(x => x.GetCollection<Session>("Sessions"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.UpdateSessionStatusForAllRefreshTokenAsync(refreshTokens);

            // Assert
            result.Should().BeTrue();
            mockCollection.Verify(x => x.UpdateManyAsync(
                It.IsAny<FilterDefinition<Session>>(),
                It.IsAny<UpdateDefinition<Session>>(),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetActiveSessionByUserIdAsync

        [Fact]
        public async Task GetActiveSessionByUserIdAsync_WithActiveSessions_ReturnsSessions()
        {
            // Arrange
            var userId = "user-1";
            var expectedSessions = new List<Session>
            {
                new Session { ItemId = ObjectId.Parse("507f1f77bcf86cd799439011"), UserId = userId, IsActive = true },
                new Session { ItemId = ObjectId.Parse("507f1f77bcf86cd799439012"), UserId = userId, IsActive = true }
            };
            var mockCollection = new Mock<IMongoCollection<Session>>();
            var mockCursor = new Mock<IAsyncCursor<Session>>();

            mockCursor.Setup(x => x.Current).Returns(expectedSessions);
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<Session>>(),
                It.IsAny<FindOptions<Session>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<Session>("Sessions"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetActiveSessionByUserIdAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result.Should().OnlyContain(s => s.IsActive);
        }

        #endregion

        #region GetSocialLoginCredentials

        [Fact]
        public async Task GetSocialLoginCredentials_ReturnsAllCredentials()
        {
            // Arrange
            var expectedCredentials = new List<SocialLoginCredential>
            {
                new SocialLoginCredential 
                { 
                    ItemId = "cred-1", 
                    Provider = "google",
                    Audience = "test-app",
                    ClientId = "client-1",
                    ClientSecret = "secret-1",
                    AuthorizationUrl = "https://auth.google.com",
                    TokenUrl = "https://token.google.com",
                    GetProfileUrl = "https://profile.google.com",
                    RedirectUrl = "https://redirect.com",
                    Scope = "openid profile"
                },
                new SocialLoginCredential 
                { 
                    ItemId = "cred-2", 
                    Provider = "facebook",
                    Audience = "test-app",
                    ClientId = "client-2",
                    ClientSecret = "secret-2",
                    AuthorizationUrl = "https://auth.facebook.com",
                    TokenUrl = "https://token.facebook.com",
                    GetProfileUrl = "https://profile.facebook.com",
                    RedirectUrl = "https://redirect.com",
                    Scope = "email"
                }
            };
            var mockCollection = new Mock<IMongoCollection<SocialLoginCredential>>();
            var mockCursor = new Mock<IAsyncCursor<SocialLoginCredential>>();

            mockCursor.Setup(x => x.Current).Returns(expectedCredentials);
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<SocialLoginCredential>>(),
                It.IsAny<FindOptions<SocialLoginCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<SocialLoginCredential>("SocialLoginCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetSocialLoginCredentials();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
        }

        #endregion

        #region GetSocialLoginCredentialByProvideAndAudienceAsync

        [Fact]
        public async Task GetSocialLoginCredentialByProvideAndAudienceAsync_WithValidParameters_ReturnsCredential()
        {
            // Arrange
            var provider = "google";
            var audience = "test-app";
            var expectedCredential = new SocialLoginCredential 
            { 
                ItemId = "cred-1", 
                Provider = provider, 
                Audience = audience,
                ClientId = "client-1",
                ClientSecret = "secret-1",
                AuthorizationUrl = "https://auth.google.com",
                TokenUrl = "https://token.google.com",
                GetProfileUrl = "https://profile.google.com",
                RedirectUrl = "https://redirect.com",
                Scope = "openid profile"
            };
            var mockCollection = new Mock<IMongoCollection<SocialLoginCredential>>();
            var mockCursor = new Mock<IAsyncCursor<SocialLoginCredential>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedCredential });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<SocialLoginCredential>>(),
                It.IsAny<FindOptions<SocialLoginCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<SocialLoginCredential>("SocialLoginCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetSocialLoginCredentialByProvideAndAudienceAsync(provider, audience);

            // Assert
            result.Should().NotBeNull();
            result.Provider.Should().Be(provider);
            result.Audience.Should().Be(audience);
        }

        #endregion

        #region GetSocialLoginCredentialByIdAsync

        [Fact]
        public async Task GetSocialLoginCredentialByIdAsync_WithValidId_ReturnsCredential()
        {
            // Arrange
            var itemId = "cred-123";
            var expectedCredential = new SocialLoginCredential 
            { 
                ItemId = itemId, 
                Provider = "google",
                Audience = "test-app",
                ClientId = "client-1",
                ClientSecret = "secret-1",
                AuthorizationUrl = "https://auth.google.com",
                TokenUrl = "https://token.google.com",
                GetProfileUrl = "https://profile.google.com",
                RedirectUrl = "https://redirect.com",
                Scope = "openid profile"
            };
            var mockCollection = new Mock<IMongoCollection<SocialLoginCredential>>();
            var mockCursor = new Mock<IAsyncCursor<SocialLoginCredential>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedCredential });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<SocialLoginCredential>>(),
                It.IsAny<FindOptions<SocialLoginCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<SocialLoginCredential>("SocialLoginCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetSocialLoginCredentialByIdAsync(itemId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(itemId);
        }

        #endregion

        #region GetSocialLoginCredentialsAsync

        [Fact]
        public async Task GetSocialLoginCredentialsAsync_ReturnsAllCredentials()
        {
            // Arrange
            var expectedCredentials = new List<SocialLoginCredential>
            {
                new SocialLoginCredential 
                { 
                    ItemId = "cred-1", 
                    Provider = "google",
                    Audience = "test-app",
                    ClientId = "client-1",
                    ClientSecret = "secret-1",
                    AuthorizationUrl = "https://auth.google.com",
                    TokenUrl = "https://token.google.com",
                    GetProfileUrl = "https://profile.google.com",
                    RedirectUrl = "https://redirect.com",
                    Scope = "openid profile"
                },
                new SocialLoginCredential 
                { 
                    ItemId = "cred-2", 
                    Provider = "microsoft",
                    Audience = "test-app",
                    ClientId = "client-2",
                    ClientSecret = "secret-2",
                    AuthorizationUrl = "https://auth.microsoft.com",
                    TokenUrl = "https://token.microsoft.com",
                    GetProfileUrl = "https://profile.microsoft.com",
                    RedirectUrl = "https://redirect.com",
                    Scope = "openid profile email"
                }
            };
            var mockCollection = new Mock<IMongoCollection<SocialLoginCredential>>();
            var mockCursor = new Mock<IAsyncCursor<SocialLoginCredential>>();

            mockCursor.Setup(x => x.Current).Returns(expectedCredentials);
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<SocialLoginCredential>>(),
                It.IsAny<FindOptions<SocialLoginCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<SocialLoginCredential>("SocialLoginCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetSocialLoginCredentialsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
        }

        #endregion

        #region SaveSocialLoginCredentialAsync

        [Fact]
        public async Task SaveSocialLoginCredentialAsync_WithValidCredential_ReturnsTrue()
        {
            // Arrange
            var credential = new SocialLoginCredential 
            { 
                ItemId = "cred-1", 
                Provider = "google",
                Audience = "test-app",
                ClientId = "client-1",
                ClientSecret = "secret-1",
                AuthorizationUrl = "https://auth.google.com",
                TokenUrl = "https://token.google.com",
                GetProfileUrl = "https://profile.google.com",
                RedirectUrl = "https://redirect.com",
                Scope = "openid profile"
            };
            var mockCollection = new Mock<IMongoCollection<SocialLoginCredential>>();
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, null);

            mockCollection.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<SocialLoginCredential>>(),
                credential,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(replaceResult);

            _dbContextProvider.Setup(x => x.GetCollection<SocialLoginCredential>("SocialLoginCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.SaveSocialLoginCredentialAsync(credential);

            // Assert
            result.Should().BeTrue();
            mockCollection.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<SocialLoginCredential>>(),
                credential,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region DeleteSocialLoginCredentialAsync

        [Fact]
        public async Task DeleteSocialLoginCredentialAsync_WithValidId_ReturnsTrue()
        {
            // Arrange
            var itemId = "cred-123";
            var mockCollection = new Mock<IMongoCollection<SocialLoginCredential>>();
            var deleteResult = new DeleteResult.Acknowledged(1);

            mockCollection.Setup(x => x.DeleteOneAsync(
                It.IsAny<FilterDefinition<SocialLoginCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(deleteResult);

            _dbContextProvider.Setup(x => x.GetCollection<SocialLoginCredential>("SocialLoginCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.DeleteSocialLoginCredentialAsync(itemId);

            // Assert
            result.Should().BeTrue();
            mockCollection.Verify(x => x.DeleteOneAsync(
                It.IsAny<FilterDefinition<SocialLoginCredential>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetAuthenticationConfigurationAsync

        [Fact]
        public async Task GetAuthenticationConfigurationAsync_ReturnsConfiguration()
        {
            // Arrange
            var expectedConfig = new AuthenticationConfiguration 
            { 
                ItemId = ObjectId.Parse("507f1f77bcf86cd799439011")
            };
            var mockCollection = new Mock<IMongoCollection<AuthenticationConfiguration>>();
            var mockCursor = new Mock<IAsyncCursor<AuthenticationConfiguration>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedConfig });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<AuthenticationConfiguration>>(),
                It.IsAny<FindOptions<AuthenticationConfiguration>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<AuthenticationConfiguration>("AuthenticationConfigurations"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetAuthenticationConfigurationAsync();

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(ObjectId.Parse("507f1f77bcf86cd799439011"));
        }

        #endregion

        #region UpdateAuthenticationConfigurationAsync

        [Fact]
        public async Task UpdateAuthenticationConfigurationAsync_WithValidConfig_UpdatesConfiguration()
        {
            // Arrange
            var config = new AuthenticationConfiguration 
            { 
                ItemId = ObjectId.Parse("507f1f77bcf86cd799439011")
            };
            var mockCollection = new Mock<IMongoCollection<AuthenticationConfiguration>>();
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, null);

            mockCollection.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<AuthenticationConfiguration>>(),
                config,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(replaceResult);

            _dbContextProvider.Setup(x => x.GetCollection<AuthenticationConfiguration>("AuthenticationConfigurations"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.UpdateAuthenticationConfigurationAsync(config);

            // Assert
            mockCollection.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<AuthenticationConfiguration>>(),
                config,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetOIDCClientCredentialAsync

        [Fact]
        public async Task GetOIDCClientCredentialAsync_WithValidClientId_ReturnsCredential()
        {
            // Arrange
            var clientId = "client-123";
            var expectedCredential = new OIDCClientCredential 
            { 
                ItemId = clientId 
            };
            var mockCollection = new Mock<IMongoCollection<OIDCClientCredential>>();
            var mockCursor = new Mock<IAsyncCursor<OIDCClientCredential>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedCredential });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<OIDCClientCredential>>(),
                It.IsAny<FindOptions<OIDCClientCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<OIDCClientCredential>("OIDCClientCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetOIDCClientCredentialAsync(clientId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(clientId);
        }

        #endregion

        #region SaveOIDCClientCredentialAsync

        [Fact]
        public async Task SaveOIDCClientCredentialAsync_WithValidCredential_SavesSuccessfully()
        {
            // Arrange
            var credential = new OIDCClientCredential 
            { 
                ItemId = "client-1" 
            };
            var mockCollection = new Mock<IMongoCollection<OIDCClientCredential>>();
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, null);

            mockCollection.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<OIDCClientCredential>>(),
                credential,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(replaceResult);

            _dbContextProvider.Setup(x => x.GetCollection<OIDCClientCredential>("OIDCClientCredentials"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.SaveOIDCClientCredentialAsync(credential);

            // Assert
            mockCollection.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<OIDCClientCredential>>(),
                credential,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetOIDCCredentialByIdAsync

        [Fact]
        public async Task GetOIDCCredentialByIdAsync_WithValidId_ReturnsCredential()
        {
            // Arrange
            var itemId = "oidc-123";
            var expectedCredential = new OIDCClientCredential 
            { 
                ItemId = itemId 
            };
            var mockCollection = new Mock<IMongoCollection<OIDCClientCredential>>();
            var mockCursor = new Mock<IAsyncCursor<OIDCClientCredential>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedCredential });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<OIDCClientCredential>>(),
                It.IsAny<FindOptions<OIDCClientCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<OIDCClientCredential>("OIDCClientCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetOIDCCredentialByIdAsync(itemId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(itemId);
        }

        #endregion

        #region GetOIDCCredentialsByTenantAsync

        [Fact]
        public async Task GetOIDCCredentialsByTenantAsync_ReturnsAllCredentials()
        {
            // Arrange
            var expectedCredentials = new List<OIDCClientCredential>
            {
                new OIDCClientCredential { ItemId = "oidc-1" },
                new OIDCClientCredential { ItemId = "oidc-2" }
            };
            var mockCollection = new Mock<IMongoCollection<OIDCClientCredential>>();
            var mockCursor = new Mock<IAsyncCursor<OIDCClientCredential>>();

            mockCursor.Setup(x => x.Current).Returns(expectedCredentials);
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<OIDCClientCredential>>(),
                It.IsAny<FindOptions<OIDCClientCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<OIDCClientCredential>("OIDCClientCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetOIDCCredentialsByTenantAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
        }

        #endregion

        #region DeleteOidcCliantAsync

        [Fact]
        public async Task DeleteOidcCliantAsync_WithValidRequest_DeletesClient()
        {
            // Arrange
            var request = new DeleteOIDCClientRequest { ItemId = "oidc-123" };
            var mockCollection = new Mock<IMongoCollection<OIDCClientCredential>>();
            var deleteResult = new DeleteResult.Acknowledged(1);

            mockCollection.Setup(x => x.DeleteOneAsync(
                It.IsAny<FilterDefinition<OIDCClientCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(deleteResult);

            _dbContextProvider.Setup(x => x.GetCollection<OIDCClientCredential>("OIDCClientCredentials"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.DeleteOidcCliantAsync(request);

            // Assert
            mockCollection.Verify(x => x.DeleteOneAsync(
                It.IsAny<FilterDefinition<OIDCClientCredential>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetClientCredentialByIdAsync

        [Fact]
        public async Task GetClientCredentialByIdAsync_WithValidId_ReturnsCredential()
        {
            // Arrange
            var clientId = "client-123";
            var expectedCredential = new ClientCredential 
            { 
                ItemId = clientId 
            };
            var mockCollection = new Mock<IMongoCollection<ClientCredential>>();
            var mockCursor = new Mock<IAsyncCursor<ClientCredential>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedCredential });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<ClientCredential>>(),
                It.IsAny<FindOptions<ClientCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<ClientCredential>("ClientCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetClientCredentialByIdAsync(clientId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(clientId);
        }

        #endregion

        #region AuthenticateBiometricCredentialAsync

        [Fact]
        public async Task AuthenticateBiometricCredentialAsync_WithValidCredentials_ReturnsCredential()
        {
            // Arrange
            var biometricId = "bio-123";
            var biometricKey = "key-abc";
            var expectedCredential = new BiometricCredential 
            { 
                BiometricId = biometricId, 
                BiometriKey = biometricId 
            };
            var mockCollection = new Mock<IMongoCollection<BiometricCredential>>();
            var mockCursor = new Mock<IAsyncCursor<BiometricCredential>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedCredential });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<BiometricCredential>>(),
                It.IsAny<FindOptions<BiometricCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<BiometricCredential>("BiometricCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.AuthenticateBiometricCredentialAsync(biometricId, biometricKey);

            // Assert
            result.Should().NotBeNull();
        }

        #endregion

        #region GetUserCodeAsync

        [Fact]
        public async Task GetUserCodeAsync_WithValidCode_ReturnsUserCode()
        {
            // Arrange
            var code = "ABC123";
            var expectedUserCode = new UserCode 
            { 
                ItemId = "code-1", 
                Code = code 
            };
            var mockCollection = new Mock<IMongoCollection<UserCode>>();
            var mockCursor = new Mock<IAsyncCursor<UserCode>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedUserCode });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<UserCode>>(),
                It.IsAny<FindOptions<UserCode>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<UserCode>("UserCodes"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserCodeAsync(code);

            // Assert
            result.Should().NotBeNull();
            result.Code.Should().Be(code);
        }

        #endregion

        #region GetBlocksClientAsync

        [Fact]
        public async Task GetBlocksClientAsync_WithValidClientId_ReturnsClient()
        {
            // Arrange
            var clientId = "blocks-client-123";
            var expectedClient = new BlocksClientConfig 
            { 
                ItemId = clientId 
            };
            var mockCollection = new Mock<IMongoCollection<BlocksClientConfig>>();
            var mockCursor = new Mock<IAsyncCursor<BlocksClientConfig>>();

            mockCursor.Setup(x => x.Current).Returns(new[] { expectedClient });
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<BlocksClientConfig>>(),
                It.IsAny<FindOptions<BlocksClientConfig>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<BlocksClientConfig>("BlocksClientConfigs"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetBlocksClientAsync(clientId);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(clientId);
        }

        #endregion

        #region SaveUserCodeByClientAsync

        [Fact]
        public async Task SaveUserCodeByClientAsync_WithValidUserCode_SavesSuccessfully()
        {
            // Arrange
            var userCode = new UserCode 
            { 
                ItemId = "code-1", 
                Code = "ABC123" 
            };
            var mockCollection = new Mock<IMongoCollection<UserCode>>();
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, null);

            mockCollection.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<UserCode>>(),
                userCode,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(replaceResult);

            _dbContextProvider.Setup(x => x.GetCollection<UserCode>("UserCodes"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.SaveUserCodeByClientAsync(userCode);

            // Assert
            mockCollection.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<UserCode>>(),
                userCode,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetUserCodesByUserIdAsync

        [Fact]
        public async Task GetUserCodesByUserIdAsync_WithValidUserId_ReturnsUserCodes()
        {
            // Arrange
            var userId = "user-123";
            var createdDate = DateTime.UtcNow;
            var userCodes = new List<UserCode>
            {
                new UserCode 
                { 
                    ItemId = "code-1", 
                    UserId = userId, 
                    Code = "ABC123",
                    CreatedDate = createdDate,
                    CodeTtlInMinute = 30,
                    ClientId = "client-1",
                    Note = "Test note"
                }
            };
            var mockCollection = new Mock<IMongoCollection<UserCode>>();
            var mockCursor = new Mock<IAsyncCursor<UserCode>>();

            mockCursor.Setup(x => x.Current).Returns(userCodes);
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<UserCode>>(),
                It.IsAny<FindOptions<UserCode>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<UserCode>("UserCodes"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserCodesByUserIdAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result.First().UserId.Should().Be(userId);
            result.First().Code.Should().Be("ABC123");
            result.First().ExpiryDate.Should().Be(createdDate.AddMinutes(30));
        }

        [Fact]
        public async Task GetUserCodesByUserIdAsync_WithNullTtl_UsesCreatedDateAsExpiry()
        {
            // Arrange
            var userId = "user-123";
            var createdDate = DateTime.UtcNow;
            var userCodes = new List<UserCode>
            {
                new UserCode 
                { 
                    ItemId = "code-1", 
                    UserId = userId, 
                    Code = "ABC123",
                    CreatedDate = createdDate,
                    CodeTtlInMinute = null,
                    ClientId = "client-1",
                    Note = "Test note"
                }
            };
            var mockCollection = new Mock<IMongoCollection<UserCode>>();
            var mockCursor = new Mock<IAsyncCursor<UserCode>>();

            mockCursor.Setup(x => x.Current).Returns(userCodes);
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<UserCode>>(),
                It.IsAny<FindOptions<UserCode>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<UserCode>("UserCodes"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetUserCodesByUserIdAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.First().ExpiryDate.Should().Be(createdDate);
        }

        #endregion

        #region SaveClientCredentialAsync

        [Fact]
        public async Task SaveClientCredentialAsync_WithValidCredential_ReturnsSuccessResponse()
        {
            // Arrange
            var credential = new ClientCredential 
            { 
                ItemId = "client-1" 
            };
            var mockCollection = new Mock<IMongoCollection<ClientCredential>>();
            var replaceResult = new ReplaceOneResult.Acknowledged(1, 1, null);

            mockCollection.Setup(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<ClientCredential>>(),
                credential,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(replaceResult);

            _dbContextProvider.Setup(x => x.GetCollection<ClientCredential>("ClientCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.SaveClientCredentialAsync(credential);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            mockCollection.Verify(x => x.ReplaceOneAsync(
                It.IsAny<FilterDefinition<ClientCredential>>(),
                credential,
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region DeleteClientCredentialAsync

        [Fact]
        public async Task DeleteClientCredentialAsync_WithValidRequest_DeletesCredential()
        {
            // Arrange
            var request = new DeleteClientCredentialRequest { ItemId = "client-123" };
            var mockCollection = new Mock<IMongoCollection<ClientCredential>>();
            var deleteResult = new DeleteResult.Acknowledged(1);

            mockCollection.Setup(x => x.DeleteOneAsync(
                It.IsAny<FilterDefinition<ClientCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(deleteResult);

            _dbContextProvider.Setup(x => x.GetCollection<ClientCredential>("ClientCredentials"))
                .Returns(mockCollection.Object);

            // Act
            await _repository.DeleteClientCredentialAsync(request);

            // Assert
            mockCollection.Verify(x => x.DeleteOneAsync(
                It.IsAny<FilterDefinition<ClientCredential>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetClientCredentialsAsync

        [Fact]
        public async Task GetClientCredentialsAsync_ReturnsAllCredentials()
        {
            // Arrange
            var expectedCredentials = new List<ClientCredential>
            {
                new ClientCredential { ItemId = "client-1" },
                new ClientCredential { ItemId = "client-2" }
            };
            var mockCollection = new Mock<IMongoCollection<ClientCredential>>();
            var mockCursor = new Mock<IAsyncCursor<ClientCredential>>();

            mockCursor.Setup(x => x.Current).Returns(expectedCredentials);
            mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<ClientCredential>>(),
                It.IsAny<FindOptions<ClientCredential>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<ClientCredential>("ClientCredentials"))
                .Returns(mockCollection.Object);

            // Act
            var result = await _repository.GetClientCredentialsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
        }

        #endregion
    }
}
