using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Accounts.Workers
{
    public class AccountActivityWorkerServiceTests : IDisposable
    {
        private readonly Mock<ILogger<AccountActivityWorkerService>> _loggerMock;
        private readonly Mock<IIdentityAccessManagementRepository> _repositoryMock;
        private readonly Mock<IIdentityAccessManagementService> _iamServiceMock;
        private readonly Mock<ICacheClient> _cacheClientMock;
        private readonly AccountActivityWorkerService _workerService;

        public AccountActivityWorkerServiceTests()
        {
            _loggerMock = new Mock<ILogger<AccountActivityWorkerService>>();
            _repositoryMock = new Mock<IIdentityAccessManagementRepository>();
            _iamServiceMock = new Mock<IIdentityAccessManagementService>();
            _cacheClientMock = new Mock<ICacheClient>();

            _workerService = new AccountActivityWorkerService(
                _loggerMock.Object,
                _repositoryMock.Object,
                _iamServiceMock.Object,
                _cacheClientMock.Object
            );
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        private static void SetupBlocksContext(string userId, string tenantId)
        {
            var createMethods = typeof(BlocksContext).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .ToList();

            var create15Method = createMethods.FirstOrDefault(m => m.GetParameters().Length == 15);
            
            if (create15Method != null)
            {
                var context = (BlocksContext)create15Method.Invoke(null, new object[]
                {
                    tenantId, Array.Empty<string>(), userId, true, string.Empty, string.Empty,
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

        #region Consume Tests

        [Fact]
        public async Task Consume_WithCode_RemovesCacheKeysAndUpdatesKeyMap()
        {
            // Arrange
            var accountEvent = new AccountActivityEvent
            {
                Code = "activation-code",
                UserId = "user-123",
                Event = "Activate_Account",
                PreventPostEvent = true
            };
            var user = new User { ItemId = accountEvent.UserId, Email = "test@example.com", FirstName = "John", LastName = "Doe" };
            var keyMaps = new List<UserKeyMap>
            {
                new UserKeyMap { Key = "key1", UserId = accountEvent.UserId },
                new UserKeyMap { Key = "key2", UserId = accountEvent.UserId }
            };

            _repositoryMock.Setup(x => x.GetActiveUserKeyMapAsync(accountEvent.UserId)).ReturnsAsync(keyMaps);
            _cacheClientMock.Setup(x => x.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId)).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(accountEvent.UserId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            // Act
            await _workerService.Consume(accountEvent);

            // Assert
            _cacheClientMock.Verify(x => x.RemoveKeyAsync("key1"), Times.Once);
            _cacheClientMock.Verify(x => x.RemoveKeyAsync("key2"), Times.Once);
            _repositoryMock.Verify(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId), Times.Once);
        }

        [Fact]
        public async Task Consume_WithoutCode_SkipsCacheRemovalAndKeyMapUpdate()
        {
            // Arrange
            var accountEvent = new AccountActivityEvent
            {
                Code = "",
                UserId = "user-123",
                Event = "Change_Password",
                PreventPostEvent = true
            };
            var user = new User { ItemId = accountEvent.UserId, Email = "test@example.com" };

            _repositoryMock.Setup(x => x.GetUserByIdAsync(accountEvent.UserId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            // Act
            await _workerService.Consume(accountEvent);

            // Assert
            _repositoryMock.Verify(x => x.GetActiveUserKeyMapAsync(It.IsAny<string>()), Times.Never);
            _cacheClientMock.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
            _repositoryMock.Verify(x => x.UpdateUserKeyMapActivationAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Consume_WithNullKeyMaps_HandlesGracefully()
        {
            // Arrange
            var accountEvent = new AccountActivityEvent
            {
                Code = "activation-code",
                UserId = "user-123",
                Event = "Activate_Account",
                PreventPostEvent = true
            };
            var user = new User { ItemId = accountEvent.UserId, Email = "test@example.com" };

            _repositoryMock.Setup(x => x.GetActiveUserKeyMapAsync(accountEvent.UserId)).ReturnsAsync((List<UserKeyMap>)null);
            _repositoryMock.Setup(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId)).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(accountEvent.UserId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            // Act
            await _workerService.Consume(accountEvent);

            // Assert
            _cacheClientMock.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
            _repositoryMock.Verify(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId), Times.Once);
        }

        [Fact]
        public async Task Consume_WithResetPasswordEvent_CallsHandlePostEventForResetPassword()
        {
            // Arrange
            var accountEvent = new AccountActivityEvent
            {
                Code = "reset-code",
                UserId = "user-123",
                Event = "Reset_Password",
                PreventPostEvent = false
            };
            var user = new User { ItemId = accountEvent.UserId, Email = "test@example.com" };

            _repositoryMock.Setup(x => x.GetActiveUserKeyMapAsync(accountEvent.UserId)).ReturnsAsync(new List<UserKeyMap>());
            _repositoryMock.Setup(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId)).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(accountEvent.UserId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.AuthenticationQueue, It.IsAny<LogoutAllEvent>())).Returns(Task.CompletedTask);

            // Act
            await _workerService.Consume(accountEvent);

            // Assert
            _iamServiceMock.Verify(x => x.SendToQueueAsync(Constants.AuthenticationQueue, 
                It.Is<LogoutAllEvent>(e => e.UserId == accountEvent.UserId)), Times.Once);
        }

        [Fact]
        public async Task Consume_WithUnknownEvent_DoesNotCallPostEventHandlers()
        {
            // Arrange
            var accountEvent = new AccountActivityEvent
            {
                Code = "some-code",
                UserId = "user-123",
                Event = "Unknown_Event",
                PreventPostEvent = false
            };
            var user = new User { ItemId = accountEvent.UserId, Email = "test@example.com" };

            _repositoryMock.Setup(x => x.GetActiveUserKeyMapAsync(accountEvent.UserId)).ReturnsAsync(new List<UserKeyMap>());
            _repositoryMock.Setup(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId)).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(accountEvent.UserId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            // Act
            await _workerService.Consume(accountEvent);

            // Assert
            _iamServiceMock.Verify(x => x.SendEmailAsync(It.IsAny<SendMail>()), Times.Never);
            _iamServiceMock.Verify(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task Consume_WithPreventPostEventTrue_SkipsPostEventHandling()
        {
            // Arrange
            var accountEvent = new AccountActivityEvent
            {
                Code = "code",
                UserId = "user-123",
                Event = "Activate_Account",
                PreventPostEvent = true
            };
            var user = new User { ItemId = accountEvent.UserId, Email = "test@example.com" };

            _repositoryMock.Setup(x => x.GetActiveUserKeyMapAsync(accountEvent.UserId)).ReturnsAsync(new List<UserKeyMap>());
            _repositoryMock.Setup(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId)).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(accountEvent.UserId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            // Act
            await _workerService.Consume(accountEvent);

            // Assert
            _iamServiceMock.Verify(x => x.SendEmailAsync(It.IsAny<SendMail>()), Times.Never);
            _iamServiceMock.Verify(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        }

        #endregion

        #region SaveUserTimeline Tests

        [Fact]
        public async Task SaveUserTimeline_WithBlocksContext_UsesContextUserId()
        {
            // Arrange
            var user = new User 
            { 
                ItemId = "user-123", 
                Email = "test@example.com",
                CreatedBy = "original-creator"
            };
            var accountEvent = new AccountActivityEvent { Event = "Test_Event" };
            
            SetupBlocksContext("admin-456", "test-tenant");
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            // Act
            var result = await _workerService.SaveUserTimeline(user, accountEvent);

            // Assert
            result.Should().BeTrue();
            _repositoryMock.Verify(x => x.InsertUserTimelineAsync(It.Is<UserTimeline>(t =>
                t.CreatedBy == "admin-456" &&
                t.Event == "Test_Event" &&
                t.CurrentData == user
            )), Times.Once);
        }

        [Fact]
        public async Task SaveUserTimeline_WithoutBlocksContext_UsesSelfAsCreator()
        {
            // Arrange
            var user = new User 
            { 
                ItemId = "user-123", 
                Email = "test@example.com",
                CreatedBy = "self-user-123"
            };
            var accountEvent = new AccountActivityEvent { Event = "Self_Event" };

            BlocksContext.ClearContext();
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            // Act
            var result = await _workerService.SaveUserTimeline(user, accountEvent);

            // Assert
            result.Should().BeTrue();
            _repositoryMock.Verify(x => x.InsertUserTimelineAsync(It.Is<UserTimeline>(t =>
                t.CreatedBy == "self-user-123" &&
                t.Event == "Self_Event"
            )), Times.Once);
        }

        [Fact]
        public async Task SaveUserTimeline_CreatesNewGuidForItemId()
        {
            // Arrange
            var user = new User 
            { 
                ItemId = "user-123", 
                Email = "test@example.com",
                CreatedBy = "creator-123"
            };
            var accountEvent = new AccountActivityEvent { Event = "Test_Event" };

            BlocksContext.ClearContext();
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            // Act
            var result = await _workerService.SaveUserTimeline(user, accountEvent);

            // Assert
            result.Should().BeTrue();
            _repositoryMock.Verify(x => x.InsertUserTimelineAsync(It.Is<UserTimeline>(t =>
                !string.IsNullOrWhiteSpace(t.ItemId) &&
                t.CurrentData == user
            )), Times.Once);
        }

        #endregion


        #region HandlePostEventForResetPassword Tests

        [Fact]
        public async Task HandlePostEventForResetPassword_SendsLogoutAllEventToQueue()
        {
            // Arrange
            var userId = "user-123";
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.AuthenticationQueue, It.IsAny<LogoutAllEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _workerService.HandlePostEventForResetPassword(userId);

            // Assert
            result.Should().BeTrue();
            _iamServiceMock.Verify(x => x.SendToQueueAsync(Constants.AuthenticationQueue, 
                It.Is<LogoutAllEvent>(e => e.UserId == userId)), Times.Once);
        }

        #endregion

        #region Integration Tests (Full Flow)

        [Fact]
        public async Task Consume_ActivateAccountWithMailPurpose_CompletesFullFlow()
        {
            // Arrange
            var accountEvent = new AccountActivityEvent
            {
                Code = "activation-code",
                UserId = "user-123",
                Event = "Activate_Account",
                PreventPostEvent = false,
                MailPurpose = "WelcomeEmail"
            };
            var user = new User
            {
                ItemId = accountEvent.UserId,
                Email = "test@example.com",
                UserName = "johndoe",
                FirstName = "John",
                LastName = "Doe",
                Salutation = "Mr",
                Language = "en-US",
                CreatedBy = "system"
            };
            var keyMaps = new List<UserKeyMap> { new UserKeyMap { Key = "key1" } };

            SetupBlocksContext("admin-789", "test-tenant");
            _repositoryMock.Setup(x => x.GetActiveUserKeyMapAsync(accountEvent.UserId)).ReturnsAsync(keyMaps);
            _cacheClientMock.Setup(x => x.RemoveKeyAsync("key1")).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId)).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(accountEvent.UserId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<SendMail>())).ReturnsAsync(true);

            // Act
            await _workerService.Consume(accountEvent);

            // Assert - Verify all operations occurred
            _cacheClientMock.Verify(x => x.RemoveKeyAsync("key1"), Times.Once);
            _repositoryMock.Verify(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId), Times.Once);
            
        }

        [Fact]
        public async Task Consume_ResetPasswordWithoutPreventPostEvent_CompletesFullFlow()
        {
            // Arrange
            var accountEvent = new AccountActivityEvent
            {
                Code = "reset-code",
                UserId = "user-456",
                Event = "Reset_Password",
                PreventPostEvent = false
            };
            var user = new User
            {
                ItemId = accountEvent.UserId,
                Email = "user@example.com",
                CreatedBy = "system"
            };
            var keyMaps = new List<UserKeyMap> 
            { 
                new UserKeyMap { Key = "key1" },
                new UserKeyMap { Key = "key2" }
            };

            _repositoryMock.Setup(x => x.GetActiveUserKeyMapAsync(accountEvent.UserId)).ReturnsAsync(keyMaps);
            _cacheClientMock.Setup(x => x.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId)).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(accountEvent.UserId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.AuthenticationQueue, It.IsAny<LogoutAllEvent>())).Returns(Task.CompletedTask);

            // Act
            await _workerService.Consume(accountEvent);

            // Assert - Verify all operations occurred
            _cacheClientMock.Verify(x => x.RemoveKeyAsync("key1"), Times.Once);
            _cacheClientMock.Verify(x => x.RemoveKeyAsync("key2"), Times.Once);
            _repositoryMock.Verify(x => x.UpdateUserKeyMapActivationAsync(accountEvent.UserId), Times.Once);
            _repositoryMock.Verify(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()), Times.Once);
            _iamServiceMock.Verify(x => x.SendToQueueAsync(Constants.AuthenticationQueue, 
                It.Is<LogoutAllEvent>(e => e.UserId == accountEvent.UserId)), Times.Once);
        }

        #endregion
    }
}
