using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Users
{
    public class UserManagementMutationServiceTests : IDisposable
    {
        private readonly Mock<ILogger<UserManagementMutationService>> _loggerMock;
        private readonly Mock<IValidator<CreateUserRequest>> _createValidatorMock;
        private readonly Mock<IValidator<UpdateUserRequest>> _updateValidatorMock;
        private readonly Mock<IIdentityAccessManagementService> _iamServiceMock;
        private readonly Mock<IUserRepository> _userRepositoryMock;
        private readonly Mock<IMessageClient> _messageClientMock;
        private readonly Mock<ICacheClient> _cacheClientMock;
        private readonly UserManagementMutationService _service;

        public UserManagementMutationServiceTests()
        {
            _loggerMock = new Mock<ILogger<UserManagementMutationService>>();
            _createValidatorMock = new Mock<IValidator<CreateUserRequest>>();
            _updateValidatorMock = new Mock<IValidator<UpdateUserRequest>>();
            _iamServiceMock = new Mock<IIdentityAccessManagementService>();
            _userRepositoryMock = new Mock<IUserRepository>();
            _messageClientMock = new Mock<IMessageClient>();
            _cacheClientMock = new Mock<ICacheClient>();

            _service = new UserManagementMutationService(
                _loggerMock.Object,
                _createValidatorMock.Object,
                _updateValidatorMock.Object,
                _iamServiceMock.Object,
                _userRepositoryMock.Object,
                _messageClientMock.Object,
                _cacheClientMock.Object
            );
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

        private static ValidationResult CreateValidValidation()
        {
            return new ValidationResult();
        }

        private static ValidationResult CreateInvalidValidation(params (string PropertyName, string ErrorMessage)[] errors)
        {
            var failures = errors.Select(e => new ValidationFailure(e.PropertyName, e.ErrorMessage)).ToList();
            return new ValidationResult(failures);
        }

        private static CreateUserRequest CreateValidUserRequest(string email = "test@example.com", string orgId = "org-123")
        {
            return new CreateUserRequest
            {
                Email = email,
                UserName = "testuser",
                FirstName = "John",
                LastName = "Doe",
                Password = "SecurePass123!",
                Language = "en-US",
                OrganizationId = orgId,
                UserCreationType = UserCreationType.Portal,
                UserPassType = UserPassType.Password,
                Memberships = new List<OrganizationMembership>()
            };
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
                Password = "hashed-password",
                Active = true,
                OrganizationIds = new List<string> { "org-123" },
                Memberships = new List<OrganizationMembership>()
            };
        }

        #region CreateUserAsync Tests

        [Fact]
        public async Task CreateUserAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = CreateValidUserRequest();
            SetupBlocksContext();
            _createValidatorMock.Setup(x => x.ValidateAsync(request, default))
                .ReturnsAsync(CreateValidValidation());
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync(request.Email))
                .ReturnsAsync((User)null);
            _userRepositoryMock.Setup(x => x.CreateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()))
                .Returns(Task.CompletedTask);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UpdateResourceUsageCommand>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.CreateUserAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrEmpty();
            _userRepositoryMock.Verify(x => x.CreateUserAsync(It.IsAny<User>()), Times.Once);
            _messageClientMock.Verify(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()), Times.Once);
        }

        [Fact]
        public async Task CreateUserAsync_WithValidationError_ReturnsErrors()
        {
            // Arrange
            var request = CreateValidUserRequest();
            var validation = CreateInvalidValidation(
                ("Email", "Invalid email format"),
                ("Password", "Password too weak")
            );
            _createValidatorMock.Setup(x => x.ValidateAsync(request, default))
                .ReturnsAsync(validation);

            // Act
            var result = await _service.CreateUserAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().HaveCount(2);
            result.Errors.Should().ContainKey("Email");
            result.Errors.Should().ContainKey("Password");
            _userRepositoryMock.Verify(x => x.CreateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task CreateUserAsync_WithExistingUser_UpdatesUser()
        {
            // Arrange
            var request = CreateValidUserRequest();
            var existingUser = CreateTestUser();
            existingUser.OrganizationIds = new List<string> { "org-456" };
            
            SetupBlocksContext();
            _createValidatorMock.Setup(x => x.ValidateAsync(request, default))
                .ReturnsAsync(CreateValidValidation());
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync(request.Email))
                .ReturnsAsync(existingUser);
            _userRepositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()))
                .Returns(Task.CompletedTask);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UpdateResourceUsageCommand>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.CreateUserAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be(existingUser.ItemId);
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.Is<User>(u => 
                u.OrganizationIds.Contains(request.OrganizationId))), Times.Once);
        }

        [Fact]
        public async Task CreateUserAsync_SendsEventToQueue()
        {
            // Arrange
            var request = CreateValidUserRequest();
            SetupBlocksContext();
            _createValidatorMock.Setup(x => x.ValidateAsync(request, default))
                .ReturnsAsync(CreateValidValidation());
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync(request.Email))
                .ReturnsAsync((User)null);
            _userRepositoryMock.Setup(x => x.CreateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()))
                .Returns(Task.CompletedTask);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UpdateResourceUsageCommand>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.CreateUserAsync(request);

            // Assert
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<UserMutationEvent>>(m => 
                    m.ConsumerName == Constants.IamQueue &&
                    m.Payload.Action == MutationEventType.Create)), 
                Times.Once);
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<UpdateResourceUsageCommand>>(m => 
                    m.ConsumerName == Constants.IdentifierQueue)), 
                Times.Once);
        }

        #endregion

        #region MapUser Tests

        [Fact]
        public void MapUser_WithValidRequest_MapsCorrectly()
        {
            // Arrange
            var request = CreateValidUserRequest("user@test.com");
            SetupBlocksContext();
            _iamServiceMock.Setup(x => x.HashPassword(request.Password))
                .Returns("hashed-password");

            // Act
            var result = _service.MapUser(request);

            // Assert
            result.Should().NotBeNull();
            result.Email.Should().Be(request.Email.ToLower());
            result.UserName.Should().Be(request.UserName.ToLower());
            result.FirstName.Should().Be(request.FirstName);
            result.LastName.Should().Be(request.LastName);
            result.Password.Should().Be("hashed-password");
            result.Language.Should().Be(request.Language);
            result.UserCreationType.Should().Be(request.UserCreationType);
        }

        [Theory]
        [InlineData(null, "default")]
        [InlineData("org-123", "org-123")]
        public void MapUser_WithDifferentOrganizationIds_SetsCorrectly(string orgId, string expected)
        {
            // Arrange
            var request = CreateValidUserRequest();
            request.OrganizationId = orgId;
            SetupBlocksContext();
            _iamServiceMock.Setup(x => x.HashPassword(It.IsAny<string>()))
                .Returns("hashed");

            // Act
            var result = _service.MapUser(request);

            // Assert
            result.OrganizationIds.Should().Contain(expected);
        }

        [Fact]
        public void MapUser_WithEmptyUserName_UsesEmail()
        {
            // Arrange
            var request = CreateValidUserRequest();
            request.UserName = "";
            SetupBlocksContext();
            _iamServiceMock.Setup(x => x.HashPassword(It.IsAny<string>()))
                .Returns("hashed");

            // Act
            var result = _service.MapUser(request);

            // Assert
            result.UserName.Should().Be(request.Email.ToLower());
        }

        [Fact]
        public void MapUser_WithEmptyPassword_SetsEmptyPasswordAndMinDate()
        {
            // Arrange
            var request = CreateValidUserRequest();
            request.Password = "";
            SetupBlocksContext();

            // Act
            var result = _service.MapUser(request);

            // Assert
            result.Password.Should().Be(string.Empty);
            result.PasswordSetTime.Should().Be(DateTime.MinValue);
        }

        [Theory]
        [InlineData("", "AccountActivation")]
        [InlineData(null, "AccountActivation")]
        [InlineData("CustomPurpose", "CustomPurpose")]
        public void MapUser_WithDifferentMailPurposes_SetsCorrectly(string mailPurpose, string expected)
        {
            // Arrange
            var request = CreateValidUserRequest();
            request.MailPurpose = mailPurpose;
            SetupBlocksContext();
            _iamServiceMock.Setup(x => x.HashPassword(It.IsAny<string>()))
                .Returns("hashed");

            // Act
            var result = _service.MapUser(request);

            // Assert
            result.MailPurpose.Should().Be(expected);
        }

        [Fact]
        public void MapUser_WithEmptyMemberships_CreatesDefaultMembership()
        {
            // Arrange
            var request = CreateValidUserRequest();
            request.Memberships = new List<OrganizationMembership>();
            SetupBlocksContext();
            _iamServiceMock.Setup(x => x.HashPassword(It.IsAny<string>()))
                .Returns("hashed");

            // Act
            var result = _service.MapUser(request);

            // Assert
            result.Memberships.Should().HaveCount(1);
            result.Memberships[0].Roles.Should().Contain("user");
        }

        #endregion

        #region UpdateUserAsync Tests

        [Fact]
        public async Task UpdateUserAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = new UpdateUserRequest
            {
                ItemId = "user-123",
                FirstName = "Jane",
                LastName = "Smith",
                PhoneNumber = "1234567890",
                Memberships = new List<OrganizationMembership>()
            };
            var existingUser = CreateTestUser();
            SetupBlocksContext();
            _updateValidatorMock.Setup(x => x.Validate(request))
                .Returns(CreateValidValidation());
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(request.ItemId))
                .ReturnsAsync(existingUser);
            _userRepositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.UpdateUserAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be(request.ItemId);
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.Is<User>(u => 
                u.FirstName == request.FirstName &&
                u.LastName == request.LastName)), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_WithValidationError_ReturnsErrors()
        {
            // Arrange
            var request = new UpdateUserRequest { ItemId = "user-123" };
            var validation = CreateInvalidValidation(("FirstName", "First name is required"));
            _updateValidatorMock.Setup(x => x.Validate(request))
                .Returns(validation);

            // Act
            var result = await _service.UpdateUserAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("FirstName");
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserAsync_WithNonExistentUser_ReturnsError()
        {
            // Arrange
            var request = new UpdateUserRequest { ItemId = "non-existent" };
            _updateValidatorMock.Setup(x => x.Validate(request))
                .Returns(CreateValidValidation());
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(request.ItemId))
                .ReturnsAsync((User)null);

            // Act
            var result = await _service.UpdateUserAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ItemId");
            result.Errors["ItemId"].Should().Be("Not found");
        }

        [Fact]
        public async Task UpdateUserAsync_WithMfaEnabled_UpdatesMfaType()
        {
            // Arrange
            var request = new UpdateUserRequest
            {
                ItemId = "user-123",
                MfaEnabled = true,
                UserMfaType = UserMfaType.TOTP,
                Memberships = new List<OrganizationMembership>()
            };
            var existingUser = CreateTestUser();
            SetupBlocksContext();
            _updateValidatorMock.Setup(x => x.Validate(request))
                .Returns(CreateValidValidation());
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(request.ItemId))
                .ReturnsAsync(existingUser);
            _userRepositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.UpdateUserAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.Is<User>(u => 
                u.MfaEnabled == true &&
                u.UserMfaType == UserMfaType.TOTP)), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_UpdatesOrganizationIds()
        {
            // Arrange
            var request = new UpdateUserRequest
            {
                ItemId = "user-123",
                Memberships = new List<OrganizationMembership>
                {
                    new OrganizationMembership { OrganizationId = "org-1" },
                    new OrganizationMembership { OrganizationId = "org-2" }
                }
            };
            var existingUser = CreateTestUser();
            SetupBlocksContext();
            _updateValidatorMock.Setup(x => x.Validate(request))
                .Returns(CreateValidValidation());
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(request.ItemId))
                .ReturnsAsync(existingUser);
            _userRepositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.UpdateUserAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.Is<User>(u => 
                u.OrganizationIds.Contains("org-1") &&
                u.OrganizationIds.Contains("org-2"))), Times.Once);
        }

        #endregion

        #region DeactivateUserAsync Tests

        [Fact]
        public async Task DeactivateUserAsync_WithValidUser_DeactivatesAndSendsEvent()
        {
            // Arrange
            var request = new DeactivateUserRequest { UserId = "user-123" };
            var user = CreateTestUser();
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(request.UserId))
                .ReturnsAsync(user);
            _userRepositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserStatusChangedEvent>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.DeactivateUserAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.Is<User>(u => u.Active == false)), Times.Once);
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<UserStatusChangedEvent>>(m => 
                    m.Payload.UserId == request.UserId &&
                    m.Payload.IsActive == false)), Times.Once);
        }

        [Fact]
        public async Task DeactivateUserAsync_WithNonExistentUser_ReturnsError()
        {
            // Arrange
            var request = new DeactivateUserRequest { UserId = "non-existent" };
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(request.UserId))
                .ReturnsAsync((User)null);

            // Act
            var result = await _service.DeactivateUserAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("user_not_found");
        }

        #endregion

        #region UpdateUserByLoginInfoAsync Tests

        [Fact]
        public async Task UpdateUserByLoginInfoAsync_WithValidUser_UpdatesLoginInfo()
        {
            // Arrange
            var refreshEvent = new RefreshTokenEvent
            {
                UserId = "user-123",
                DeviceInformation = new DeviceInformation { Browser = "Chrome", OS = "Windows" }
            };
            var user = CreateTestUser();
            user.LogInCount = 0;
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(refreshEvent.UserId))
                .ReturnsAsync(user);
            _userRepositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);

            // Act
            await _service.UpdateUserByLoginInfoAsync(refreshEvent);

            // Assert
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.Is<User>(u => 
                u.LogInCount == 1 &&
                u.FirstLoggedInTime != DateTime.MinValue &&
                u.LastLoggedInTime != DateTime.MinValue)), Times.Once);
        }

        [Fact]
        public async Task UpdateUserByLoginInfoAsync_WithExistingLogins_IncrementsCount()
        {
            // Arrange
            var refreshEvent = new RefreshTokenEvent
            {
                UserId = "user-123",
                DeviceInformation = new DeviceInformation()
            };
            var user = CreateTestUser();
            user.LogInCount = 5;
            user.FirstLoggedInTime = DateTime.Now.AddDays(-30);
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(refreshEvent.UserId))
                .ReturnsAsync(user);
            _userRepositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);

            // Act
            await _service.UpdateUserByLoginInfoAsync(refreshEvent);

            // Assert
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.Is<User>(u => 
                u.LogInCount == 6)), Times.Once);
        }

        [Fact]
        public async Task UpdateUserByLoginInfoAsync_WithNullUser_DoesNotUpdate()
        {
            // Arrange
            var refreshEvent = new RefreshTokenEvent { UserId = "non-existent" };
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(refreshEvent.UserId))
                .ReturnsAsync((User)null);

            // Act
            await _service.UpdateUserByLoginInfoAsync(refreshEvent);

            // Assert
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        #endregion

        #region ExecuteUserMutationCommandAsync Tests

        [Fact]
        public async Task ExecuteUserMutationCommandAsync_SendsActivationAndSavesTimeline()
        {
            // Arrange
            var command = new UserMutationEvent { ItemId = "user-123", Action = MutationEventType.Create };
            var user = CreateTestUser();
            user.Language = "en-US";
            user.MailPurpose = "AccountActivation";
            var config = new IamConfiguration
            {
                AccountActivationUrl = "https://example.com/activate",
                ActivationUrlLifetimeInMinutes = 60
            };
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(command.ItemId))
                .ReturnsAsync(user);
            _userRepositoryMock.Setup(x => x.GetIamConfigurationAsync())
                .ReturnsAsync(config);
            _cacheClientMock.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            _userRepositoryMock.Setup(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>()))
                .ReturnsAsync(true);
            _userRepositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()))
                .ReturnsAsync(true);

            // Act
            await _service.ExecuteUserMutationCommandAsync(command);

            // Assert
            _cacheClientMock.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), user.ItemId, 3600), Times.Once);
            _iamServiceMock.Verify(x => x.SendActivationToEmailAsync(user, It.IsAny<string>(), "AccountActivation", string.Empty), Times.Once);
            _userRepositoryMock.Verify(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>()), Times.Once);
            _userRepositoryMock.Verify(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()), Times.Once);
        }

        #endregion

        #region SaveRolesAndPermissionsAsync Tests

        [Fact]
        public async Task SaveRolesAndPermissionsAsync_WithValidRequest_SavesAndSendsEvent()
        {
            // Arrange
            var request = new SaveRolesAndPermissionsRequest
            {
                UserId = "user-123",
                Memberships = new List<OrganizationMembership>
                {
                    new OrganizationMembership { OrganizationId = "org-1", Roles = new List<string> { "admin" } }
                }
            };
            var user = CreateTestUser();
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(request.UserId))
                .ReturnsAsync(user);
            _userRepositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SaveRolesAndPermissionsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be(user.ItemId);
            _userRepositoryMock.Verify(x => x.UpdateUserAsync(It.Is<User>(u => 
                u.Memberships == request.Memberships)), Times.Once);
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<UserMutationEvent>>(m => 
                    m.Payload.Action == MutationEventType.Update)), Times.Once);
        }

        [Fact]
        public async Task SaveRolesAndPermissionsAsync_WithNonExistentUser_ReturnsError()
        {
            // Arrange
            var request = new SaveRolesAndPermissionsRequest { UserId = "non-existent" };
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(request.UserId))
                .ReturnsAsync((User)null);

            // Act
            var result = await _service.SaveRolesAndPermissionsAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ItemId");
        }

        #endregion

        #region CreateUserByEmailAsync Tests

        [Fact]
        public async Task CreateUserByEmailAsync_WithValidEvent_CreatesUser()
        {
            // Arrange
            var @event = new CreateUserByEmailEvent
            {
                Email = "newuser@example.com",
                EventType = "project_invitation",
                EventQueue = "test-queue",
                ProjectKey = "test-project"
            };
            var config = new IamConfiguration
            {
                ActivationUrlLifetimeInMinutes = 60
            };
            SetupBlocksContext();
            _createValidatorMock.Setup(x => x.ValidateAsync(It.IsAny<CreateUserRequest>(), default))
                .ReturnsAsync(CreateValidValidation());
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync(@event.Email))
                .ReturnsAsync((User)null);
            _userRepositoryMock.Setup(x => x.CreateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(CreateTestUser());
            _userRepositoryMock.Setup(x => x.GetIamConfigurationAsync())
                .ReturnsAsync(config);
            _cacheClientMock.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);
            _userRepositoryMock.Setup(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>()))
                .ReturnsAsync(true);
            _userRepositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()))
                .ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<CreateUserByEmailPostEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.CreateUserByEmailAsync(@event);

            // Assert
            result.Should().BeTrue();
            _userRepositoryMock.Verify(x => x.CreateUserAsync(It.IsAny<User>()), Times.Once);
            _iamServiceMock.Verify(x => x.SendToQueueAsync(@event.EventQueue, It.IsAny<CreateUserByEmailPostEvent>()), Times.Once);
        }

        [Fact]
        public async Task CreateUserByEmailAsync_WithValidationError_ReturnsFalse()
        {
            // Arrange
            var @event = new CreateUserByEmailEvent { Email = "invalid@email" };
            var validation = CreateInvalidValidation(("Email", "Invalid email"));
            SetupBlocksContext();
            _createValidatorMock.Setup(x => x.ValidateAsync(It.IsAny<CreateUserRequest>(), default))
                .ReturnsAsync(validation);

            // Act
            var result = await _service.CreateUserByEmailAsync(@event);

            // Assert
            result.Should().BeFalse();
            _userRepositoryMock.Verify(x => x.CreateUserAsync(It.IsAny<User>()), Times.Never);
        }

        #endregion

        #region CreateUserViaSsoAsync Tests

        [Fact]
        public async Task CreateUserViaSsoAsync_WithValidRequest_CreatesUserAndSendsEvent()
        {
            // Arrange
            var request = new CreateUserViaSsoRequest
            {
                Email = "sso@example.com",
                FirstName = "SSO",
                LastName = "User",
                MailPurpose = "WelcomeEmail",
                SendWelcomeMail = true,
                ProjectKey = "sso-project",
                Platform = "Web",
                Memberships = new List<OrganizationMembership>(),
                Active = true,
                IsVarified = true,
                ExternalUserId = "ext-123"
            };
            SetupBlocksContext();
            _iamServiceMock.Setup(x => x.HashPassword(It.IsAny<string>()))
                .Returns("hashed-password");
            _userRepositoryMock.Setup(x => x.CreateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreateUserViaSsoEvent>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.CreateUserViaSsoAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrEmpty();
            _userRepositoryMock.Verify(x => x.CreateUserAsync(It.Is<User>(u => 
                u.Email == request.Email.ToLower() &&
                u.Active == request.Active &&
                u.IsVarified == request.IsVarified &&
                u.ExternalUserId == request.ExternalUserId)), Times.Once);
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<CreateUserViaSsoEvent>>(m => 
                    m.ConsumerName == Constants.IamQueue &&
                    m.Payload.MailPurpose == request.MailPurpose &&
                    m.Payload.SendWelcomeMail == request.SendWelcomeMail)), Times.Once);
        }

        #endregion

        #region ExecuteUserMutationViaSsoCommandAsync Tests

        [Fact]
        public async Task ExecuteUserMutationViaSsoCommandAsync_WithSendWelcomeMail_SendsEmail()
        {
            // Arrange
            var command = new CreateUserViaSsoEvent
            {
                ItemId = "user-123",
                Action = MutationEventType.Create,
                SendWelcomeMail = true,
                MailPurpose = "WelcomeEmail",
                ProjectKey = "test-project"
            };
            var user = CreateTestUser();
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(command.ItemId))
                .ReturnsAsync(user);
            _iamServiceMock.Setup(x => x.SendAccountActivationEmailAsync(user, command.MailPurpose, command.ProjectKey))
                .ReturnsAsync(true);
            _userRepositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()))
                .ReturnsAsync(true);

            // Act
            await _service.ExecuteUserMutationViaSsoCommandAsync(command);

            // Assert
            _iamServiceMock.Verify(x => x.SendAccountActivationEmailAsync(user, command.MailPurpose, command.ProjectKey), Times.Once);
            _userRepositoryMock.Verify(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteUserMutationViaSsoCommandAsync_WithoutSendWelcomeMail_SkipsEmail()
        {
            // Arrange
            var command = new CreateUserViaSsoEvent
            {
                ItemId = "user-123",
                Action = MutationEventType.Create,
                SendWelcomeMail = false,
                MailPurpose = "None",
                ProjectKey = "test-project"
            };
            var user = CreateTestUser();
            SetupBlocksContext();
            _userRepositoryMock.Setup(x => x.GetUserByIdAsync(command.ItemId))
                .ReturnsAsync(user);
            _userRepositoryMock.Setup(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()))
                .ReturnsAsync(true);

            // Act
            await _service.ExecuteUserMutationViaSsoCommandAsync(command);

            // Assert
            _iamServiceMock.Verify(x => x.SendAccountActivationEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _userRepositoryMock.Verify(x => x.InsertUserTimelineAsync(It.IsAny<UserTimeline>()), Times.Once);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task CreateUser_FullWorkflow_Success()
        {
            // Arrange
            var request = CreateValidUserRequest();
            SetupBlocksContext();
            _createValidatorMock.Setup(x => x.ValidateAsync(request, default))
                .ReturnsAsync(CreateValidValidation());
            _userRepositoryMock.Setup(x => x.GetUserByEmailAsync(request.Email))
                .ReturnsAsync((User)null);
            _iamServiceMock.Setup(x => x.HashPassword(request.Password))
                .Returns("hashed-password");
            _userRepositoryMock.Setup(x => x.CreateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(true);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()))
                .Returns(Task.CompletedTask);
            _messageClientMock.Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UpdateResourceUsageCommand>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.CreateUserAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrEmpty();
            
            // Verify logging
            _loggerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("User creation start")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            
            // Verify all operations
            _userRepositoryMock.Verify(x => x.GetUserByEmailAsync(request.Email), Times.Once);
            _userRepositoryMock.Verify(x => x.CreateUserAsync(It.IsAny<User>()), Times.Once);
            _messageClientMock.Verify(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()), Times.Once);
            _messageClientMock.Verify(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<UpdateResourceUsageCommand>>()), Times.Once);
        }

        #endregion
    }
}
