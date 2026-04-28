using Blocks.Genesis;
using FluentAssertions;
using Grpc.Core;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Users
{
    public class UserGrpcServiceTests
    {
        private readonly Mock<ILogger<UserGrpcService>> _loggerMock;
        private readonly Mock<IUserManagementMutationService> _userManagementServiceMock;
        private readonly UserGrpcService _grpcService;
        private readonly Mock<ServerCallContext> _serverCallContextMock;

        public UserGrpcServiceTests()
        {
            _loggerMock = new Mock<ILogger<UserGrpcService>>();
            _userManagementServiceMock = new Mock<IUserManagementMutationService>();
            _grpcService = new UserGrpcService(_loggerMock.Object, _userManagementServiceMock.Object);
            _serverCallContextMock = new Mock<ServerCallContext>();
        }

        #region SignupUser Tests

        [Fact]
        public async Task SignupUser_WithValidRequest_ReturnsSuccessReply()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "test@example.com",
                MailPurpose = "AccountActivation"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "user-123"
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("user-123");
            result.Errors.Should().BeEmpty();

            _userManagementServiceMock.Verify(x => x.CreateUserAsync(
                It.Is<CreateUserRequest>(r => 
                    r.Email == request.Email && 
                    r.MailPurpose == request.MailPurpose)), 
                Times.Once);
        }

        [Fact]
        public async Task SignupUser_WithValidationErrors_ReturnsErrorsInReply()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "invalid-email",
                MailPurpose = "Test"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = false,
                ItemId = null,
                Errors = new Dictionary<string, string>
                {
                    { "Email", "Invalid email format" },
                    { "MailPurpose", "Invalid mail purpose" }
                }
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.ItemId.Should().Be(string.Empty);
            result.Errors.Should().HaveCount(2);
            result.Errors.Should().ContainKey("Email");
            result.Errors["Email"].Should().Be("Invalid email format");
            result.Errors.Should().ContainKey("MailPurpose");
            result.Errors["MailPurpose"].Should().Be("Invalid mail purpose");
        }

        [Fact]
        public async Task SignupUser_WithNullItemId_ReturnsEmptyString()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "test@example.com"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = null
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be(string.Empty);
        }

        [Fact]
        public async Task SignupUser_WithNullErrors_ReturnsEmptyErrorsDictionary()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "test@example.com"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "user-123",
                Errors = null
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task SignupUser_LogsInformationMessage()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "test@example.com"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "user-123"
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Start SignupUser")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Theory]
        [InlineData("test@example.com", "Purpose1")]
        [InlineData("user@domain.com", "Purpose2")]
        [InlineData("admin@test.com", "AccountActivation")]
        public async Task SignupUser_WithDifferentInputs_MapsCorrectly(string email, string mailPurpose)
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = email,
                MailPurpose = mailPurpose
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "user-123"
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            _userManagementServiceMock.Verify(x => x.CreateUserAsync(
                It.Is<CreateUserRequest>(r => 
                    r.Email == email && 
                    r.MailPurpose == mailPurpose)), 
                Times.Once);
        }

        [Fact]
        public async Task SignupUser_WithEmptyErrors_ReturnsEmptyErrorsDictionary()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "test@example.com"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "user-123",
                Errors = new Dictionary<string, string>()
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task SignupUser_WithSingleError_ReturnsOneError()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "test@example.com"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "Email", "Email already exists" }
                }
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors["Email"].Should().Be("Email already exists");
        }

        [Fact]
        public async Task SignupUser_CallsServiceOnce()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "test@example.com"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "user-123"
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            _userManagementServiceMock.Verify(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()), Times.Once);
        }

        [Fact]
        public async Task SignupUser_WithMultipleErrors_ReturnsAllErrors()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "test@example.com"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "Email", "Invalid email" },
                    { "UserName", "UserName required" },
                    { "Password", "Password too weak" }
                }
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().HaveCount(3);
            result.Errors["Email"].Should().Be("Invalid email");
            result.Errors["UserName"].Should().Be("UserName required");
            result.Errors["Password"].Should().Be("Password too weak");
        }

        [Theory]
        [InlineData(true, "user-123")]
        [InlineData(true, "user-456")]
        [InlineData(false, null)]
        public async Task SignupUser_WithDifferentSuccessStates_ReturnsCorrectly(bool isSuccess, string itemId)
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "test@example.com"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = isSuccess,
                ItemId = itemId,
                Errors = isSuccess ? null : new Dictionary<string, string> { { "Error", "Failed" } }
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().Be(isSuccess);
            result.ItemId.Should().Be(itemId ?? string.Empty);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task SignupUser_FullWorkflow_Success()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "newuser@example.com",
                MailPurpose = "WelcomeEmail"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "new-user-id-123"
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert - Verify complete workflow
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("new-user-id-123");
            result.Errors.Should().BeEmpty();

            // Verify logging
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Start SignupUser")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            // Verify service call
            _userManagementServiceMock.Verify(x => x.CreateUserAsync(
                It.Is<CreateUserRequest>(r => 
                    r.Email == request.Email && 
                    r.MailPurpose == request.MailPurpose)),
                Times.Once);
        }

        [Fact]
        public async Task SignupUser_FullWorkflow_WithErrors()
        {
            // Arrange
            var request = new SignupUserRequest
            {
                Email = "invalid@email",
                MailPurpose = "Invalid"
            };

            var serviceResponse = new BaseMutationResponse
            {
                IsSuccess = false,
                ItemId = null,
                Errors = new Dictionary<string, string>
                {
                    { "Email", "Email format invalid" },
                    { "MailPurpose", "Unknown mail purpose" }
                }
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _grpcService.SignupUser(request, _serverCallContextMock.Object);

            // Assert - Verify complete error handling
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.ItemId.Should().Be(string.Empty);
            result.Errors.Should().HaveCount(2);
            result.Errors.Should().ContainKeys("Email", "MailPurpose");

            // Verify logging still occurs
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion
    }
}
