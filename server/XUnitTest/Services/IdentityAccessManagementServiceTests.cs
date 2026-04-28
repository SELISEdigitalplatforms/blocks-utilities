using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Moq;
using Tenant = Blocks.Genesis.Tenant;

namespace XUnitTest.Services
{
    public class IdentityAccessManagementServiceTests : IDisposable
    {
        private readonly Mock<ILogger<IdentityAccessManagementService>> _loggerMock;
        private readonly Mock<ITenants> _tenantsMock;
        private readonly Mock<ICryptoService> _cryptoServiceMock;
        private readonly Mock<IMessageClient> _messageClientMock;
        private readonly Mock<IUserRepository> _userRepositoryMock;
        private readonly IdentityAccessManagementService _service;

        public IdentityAccessManagementServiceTests()
        {
            _loggerMock = new Mock<ILogger<IdentityAccessManagementService>>();
            _tenantsMock = new Mock<ITenants>();
            _cryptoServiceMock = new Mock<ICryptoService>();
            _messageClientMock = new Mock<IMessageClient>();
            _userRepositoryMock = new Mock<IUserRepository>();

            _service = new IdentityAccessManagementService(
                _loggerMock.Object,
                _tenantsMock.Object,
                _cryptoServiceMock.Object,
                _messageClientMock.Object,
                _userRepositoryMock.Object
            );
        }

        public void Dispose()
        {
            BlocksContext.ClearContext();
        }

        private static void SetupBlocksContext(string userId = "user-123", string tenantId = "tenant-123")
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

        private static User CreateTestUser(string userId = "user-123")
        {
            return new User
            {
                ItemId = userId,
                Email = "test@example.com",
                UserName = "testuser",
                FirstName = "John",
                LastName = "Doe",
                Salutation = "Mr",
                Language = "en-US"
            };
        }

        private static Tenant CreateTestTenant(string tenantSalt = null, bool isRootTenant = false, string name = null)
        {
            return new Tenant
            {
                TenantSalt = tenantSalt,
                IsRootTenant = isRootTenant,
                Name = name,
                ApplicationDomain = "test-domain",
                DbConnectionString = "mongodb://localhost",
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = "test-password",
                    IssueDate = DateTime.UtcNow
                }
            };
        }

        #region HashPassword Tests

        [Fact]
        public void HashPassword_WithValidPassword_ReturnsHashedPassword()
        {
            // Arrange
            var password = "TestPassword123!";
            var tenantSalt = "tenant-salt-123";
            var expectedHash = "hashed-password";
            var tenantId = "tenant-123";

            SetupBlocksContext(tenantId: tenantId);
            _tenantsMock.Setup(x => x.GetTenantByID(tenantId)).Returns(CreateTestTenant(tenantSalt: tenantSalt));
            _cryptoServiceMock.Setup(x => x.Hash(password, tenantSalt)).Returns(expectedHash);

            // Act
            var result = _service.HashPassword(password);

            // Assert
            result.Should().Be(expectedHash);
            _cryptoServiceMock.Verify(x => x.Hash(password, tenantSalt), Times.Once);
        }

        [Fact]
        public void HashPassword_WithNullTenant_PassesNullSalt()
        {
            // Arrange
            var password = "TestPassword123!";
            var expectedHash = "hashed-password";
            var tenantId = "tenant-123";

            SetupBlocksContext(tenantId: tenantId);
            _tenantsMock.Setup(x => x.GetTenantByID(tenantId)).Returns((Tenant)null);
            _cryptoServiceMock.Setup(x => x.Hash(password, null)).Returns(expectedHash);

            // Act
            var result = _service.HashPassword(password);

            // Assert
            result.Should().Be(expectedHash);
            _cryptoServiceMock.Verify(x => x.Hash(password, null), Times.Once);
        }

        [Fact]
        public void HashPassword_WithNoContext_HandlesNullContext()
        {
            // Arrange
            var password = "TestPassword123!";
            var expectedHash = "hashed-password";

            _tenantsMock.Setup(x => x.GetTenantByID(null)).Returns((Tenant)null);
            _cryptoServiceMock.Setup(x => x.Hash(password, null)).Returns(expectedHash);

            // Act
            var result = _service.HashPassword(password);

            // Assert
            result.Should().Be(expectedHash);
            _tenantsMock.Verify(x => x.GetTenantByID(null), Times.Once);
        }

        #endregion

        #region SendToQueueAsync Tests

        [Fact]
        public async Task SendToQueueAsync_WithValidPayload_SendsToConsumer()
        {
            // Arrange
            var queue = "test-queue";
            var payload = new TestPayload { Message = "Test message" };

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<TestPayload>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendToQueueAsync(queue, payload);

            // Assert
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<TestPayload>>(m => 
                    m.ConsumerName == queue && 
                    m.Payload == payload)), 
                Times.Once);
        }

        [Theory]
        [InlineData("queue1")]
        [InlineData("queue2")]
        [InlineData(Constants.MailQueue)]
        public async Task SendToQueueAsync_WithDifferentQueues_UsesCorrectQueueName(string queueName)
        {
            // Arrange
            var payload = new TestPayload { Message = "Test" };

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<TestPayload>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendToQueueAsync(queueName, payload);

            // Assert
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<TestPayload>>(m => m.ConsumerName == queueName)), 
                Times.Once);
        }

        #endregion

        #region SendToTopicAsync Tests

        [Fact]
        public async Task SendToTopicAsync_WithValidPayload_SendsToMassConsumer()
        {
            // Arrange
            var topic = "test-topic";
            var payload = new TestPayload { Message = "Test message" };

            _messageClientMock
                .Setup(x => x.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<TestPayload>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendToTopicAsync(topic, payload);

            // Assert
            _messageClientMock.Verify(x => x.SendToMassConsumerAsync(
                It.Is<ConsumerMessage<TestPayload>>(m => 
                    m.ConsumerName == topic && 
                    m.Payload == payload)), 
                Times.Once);
        }

        [Theory]
        [InlineData("topic1")]
        [InlineData("topic2")]
        [InlineData("notification-topic")]
        public async Task SendToTopicAsync_WithDifferentTopics_UsesCorrectTopicName(string topicName)
        {
            // Arrange
            var payload = new TestPayload { Message = "Test" };

            _messageClientMock
                .Setup(x => x.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<TestPayload>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendToTopicAsync(topicName, payload);

            // Assert
            _messageClientMock.Verify(x => x.SendToMassConsumerAsync(
                It.Is<ConsumerMessage<TestPayload>>(m => m.ConsumerName == topicName)), 
                Times.Once);
        }

        #endregion

        #region SendEmailAsync Tests

        [Fact]
        public async Task SendEmailAsync_WithValidCommand_SendsToMailQueue()
        {
            // Arrange
            var sendMailCommand = new SendMail
            {
                To = new[] { "test@example.com" },
                Purpose = "TestPurpose",
                Language = "en-US",
                ProjectKey = "test-project"
            };

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendEmailAsync(sendMailCommand);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m => 
                    m.ConsumerName == Constants.MailQueue && 
                    m.Payload == sendMailCommand)), 
                Times.Once);
        }

        #endregion

        #region SendActivationToEmailAsync Tests

        [Fact]
        public async Task SendActivationToEmailAsync_WithStandardPurpose_SendsEmailWithActivationUrl()
        {
            // Arrange
            var user = CreateTestUser();
            var activationUri = "https://example.com/activate";
            var emailPurpose = "AccountActivation";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendActivationToEmailAsync(user, activationUri, emailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m =>
                    m.ConsumerName == Constants.MailQueue &&
                    m.Payload.Purpose == emailPurpose &&
                    m.Payload.To.Contains(user.Email.ToLower()) &&
                    m.Payload.Language == user.Language &&
                    m.Payload.ProjectKey == projectKey &&
                    m.Payload.BodyDataContext.ContainsKey("AccountActivationUrl") &&
                    m.Payload.BodyDataContext["AccountActivationUrl"] == activationUri &&
                    m.Payload.BodyDataContext["UserName"] == user.UserName &&
                    m.Payload.BodyDataContext["DisplayName"] == $"{user.FirstName} {user.LastName}")),
                Times.Once);
        }

        [Fact]
        public async Task SendActivationToEmailAsync_WithProjectInvitation_IncludesProjectName()
        {
            // Arrange
            var user = CreateTestUser();
            var activationUri = "https://example.com/invite";
            var emailPurpose = "project_invitation";
            var projectKey = "test-project";
            var projectId = "project-123";
            var projectName = "Test Project";

            _userRepositoryMock.Setup(x => x.GetProjectIdFromProjectPeopleAsync(user.ItemId))
                .ReturnsAsync(projectId);
            _tenantsMock.Setup(x => x.GetTenantByID(projectId))
                .Returns(CreateTestTenant(name: projectName));
            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendActivationToEmailAsync(user, activationUri, emailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m =>
                    m.Payload.BodyDataContext.ContainsKey("ProjectInvitationLink") &&
                    m.Payload.BodyDataContext["ProjectInvitationLink"] == activationUri &&
                    m.Payload.BodyDataContext["ProjectName"] == projectName &&
                    m.Payload.BodyDataContext["DisplayName"] == $"{user.FirstName} {user.LastName}")),
                Times.Once);
            _userRepositoryMock.Verify(x => x.GetProjectIdFromProjectPeopleAsync(user.ItemId), Times.Once);
        }

        [Fact]
        public async Task SendActivationToEmailAsync_WithNullLanguage_UsesDefaultLanguage()
        {
            // Arrange
            var user = CreateTestUser();
            user.Language = null;
            var activationUri = "https://example.com/activate";
            var emailPurpose = "AccountActivation";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendActivationToEmailAsync(user, activationUri, emailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m => m.Payload.Language == "en-US")),
                Times.Once);
        }

        [Fact]
        public async Task SendActivationToEmailAsync_WithWhitespaceFirstName_UsesEmailAsDisplayNameForProjectInvitation()
        {
            // Arrange
            var user = CreateTestUser();
            user.FirstName = "   ";
            var activationUri = "https://example.com/activate";
            var emailPurpose = "project_invitation";
            var projectKey = "test-project";
            var projectId = "project-123";
            var projectName = "Test Project";

            _userRepositoryMock.Setup(x => x.GetProjectIdFromProjectPeopleAsync(user.ItemId))
                .ReturnsAsync(projectId);
            _tenantsMock.Setup(x => x.GetTenantByID(projectId))
                .Returns(CreateTestTenant(name: projectName));
            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendActivationToEmailAsync(user, activationUri, emailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m =>
                    m.Payload.BodyDataContext["DisplayName"] == user.Email)),
                Times.Once);
        }

        [Fact]
        public async Task SendActivationToEmailAsync_EmailToLowerCase_ConvertsToLower()
        {
            // Arrange
            var user = CreateTestUser();
            user.Email = "TEST@EXAMPLE.COM";
            var activationUri = "https://example.com/activate";
            var emailPurpose = "AccountActivation";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendActivationToEmailAsync(user, activationUri, emailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m =>
                    m.Payload.To.First() == "test@example.com")),
                Times.Once);
        }

        [Fact]
        public async Task SendActivationToEmailAsync_EmptyCcBcc_SetsEmptyArrays()
        {
            // Arrange
            var user = CreateTestUser();
            var activationUri = "https://example.com/activate";
            var emailPurpose = "AccountActivation";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendActivationToEmailAsync(user, activationUri, emailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m =>
                    m.Payload.Cc.Count() == 0 &&
                    m.Payload.Bcc.Count() == 0)),
                Times.Once);
        }

        [Fact]
        public async Task SendActivationToEmailAsync_LogsInformation()
        {
            // Arrange
            var user = CreateTestUser();
            var activationUri = "https://example.com/activate";
            var emailPurpose = "AccountActivation";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendActivationToEmailAsync(user, activationUri, emailPurpose, projectKey);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Sending Activation")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region SendAccountActivationEmailAsync Tests

        [Fact]
        public async Task SendAccountActivationEmailAsync_WithValidUser_SendsEmail()
        {
            // Arrange
            var user = CreateTestUser();
            var mailPurpose = "AccountActivated";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendAccountActivationEmailAsync(user, mailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m =>
                    m.ConsumerName == Constants.MailQueue &&
                    m.Payload.Purpose == mailPurpose &&
                    m.Payload.To.Contains(user.Email.ToLower()) &&
                    m.Payload.Language == user.Language &&
                    m.Payload.ProjectKey == projectKey)),
                Times.Once);
        }

        [Fact]
        public async Task SendAccountActivationEmailAsync_WithEmptyMailPurpose_UsesDefaultPurpose()
        {
            // Arrange
            var user = CreateTestUser();
            var mailPurpose = "";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendAccountActivationEmailAsync(user, mailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m => m.Payload.Purpose == "AccountActivated")),
                Times.Once);
        }

        [Fact]
        public async Task SendAccountActivationEmailAsync_WithWhitespaceMailPurpose_UsesDefaultPurpose()
        {
            // Arrange
            var user = CreateTestUser();
            var mailPurpose = "   ";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendAccountActivationEmailAsync(user, mailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m => m.Payload.Purpose == "AccountActivated")),
                Times.Once);
        }

        [Fact]
        public async Task SendAccountActivationEmailAsync_WithNullLanguage_UsesDefaultLanguage()
        {
            // Arrange
            var user = CreateTestUser();
            user.Language = null;
            var mailPurpose = "AccountActivated";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendAccountActivationEmailAsync(user, mailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m => m.Payload.Language == "en-US")),
                Times.Once);
        }

        [Fact]
        public async Task SendAccountActivationEmailAsync_IncludesAllUserFields()
        {
            // Arrange
            var user = CreateTestUser();
            var mailPurpose = "AccountActivated";
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendAccountActivationEmailAsync(user, mailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m =>
                    m.Payload.BodyDataContext["UserName"] == user.UserName &&
                    m.Payload.BodyDataContext["DisplayName"] == $"{user.FirstName} {user.LastName}" &&
                    m.Payload.BodyDataContext["CreatedUser.Salutation"] == user.Salutation &&
                    m.Payload.BodyDataContext["CreatedUser.FirstName"] == user.FirstName &&
                    m.Payload.BodyDataContext["CreatedUser.LastName"] == user.LastName &&
                    m.Payload.BodyDataContext["CreatedUser.Email"] == user.Email)),
                Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task SendAccountActivationEmailAsync_WithEmptyOrNullMailPurpose_UsesDefaultPurpose(string mailPurpose)
        {
            // Arrange
            var user = CreateTestUser();
            var projectKey = "test-project";

            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SendAccountActivationEmailAsync(user, mailPurpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m => m.Payload.Purpose == "AccountActivated")),
                Times.Once);
        }

        #endregion

        #region IsRoot Tests

        [Fact]
        public void IsRoot_WithRootTenant_ReturnsTrue()
        {
            // Arrange
            var tenantId = "root-tenant";
            SetupBlocksContext(tenantId: tenantId);
            _tenantsMock.Setup(x => x.GetTenantByID(tenantId))
                .Returns(CreateTestTenant(isRootTenant: true));

            // Act
            var result = _service.IsRoot();

            // Assert
            result.Should().BeTrue();
            _tenantsMock.Verify(x => x.GetTenantByID(tenantId), Times.Once);
        }

        [Fact]
        public void IsRoot_WithNonRootTenant_ReturnsFalse()
        {
            // Arrange
            var tenantId = "regular-tenant";
            SetupBlocksContext(tenantId: tenantId);
            _tenantsMock.Setup(x => x.GetTenantByID(tenantId))
                .Returns(CreateTestTenant(isRootTenant: false));

            // Act
            var result = _service.IsRoot();

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsRoot_WithNullTenant_ReturnsFalse()
        {
            // Arrange
            var tenantId = "non-existent-tenant";
            SetupBlocksContext(tenantId: tenantId);
            _tenantsMock.Setup(x => x.GetTenantByID(tenantId))
                .Returns((Tenant)null);

            // Act
            var result = _service.IsRoot();

            // Assert
            result.Should().BeFalse();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IsRoot_WithDifferentTenantStates_ReturnsCorrectValue(bool isRootTenant)
        {
            // Arrange
            var tenantId = "tenant-123";
            SetupBlocksContext(tenantId: tenantId);
            _tenantsMock.Setup(x => x.GetTenantByID(tenantId))
                .Returns(CreateTestTenant(isRootTenant: isRootTenant));

            // Act
            var result = _service.IsRoot();

            // Assert
            result.Should().Be(isRootTenant);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task AllEmailMethods_SendToCorrectQueue()
        {
            // Arrange
            var user = CreateTestUser();
            _messageClientMock
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendEmailAsync(new SendMail { To = new[] { user.Email }, Purpose = "Test" });
            await _service.SendActivationToEmailAsync(user, "link", "purpose", "key");
            await _service.SendAccountActivationEmailAsync(user, "purpose", "key");

            // Assert
            _messageClientMock.Verify(x => x.SendToConsumerAsync(
                It.Is<ConsumerMessage<SendMail>>(m => m.ConsumerName == Constants.MailQueue)),
                Times.Exactly(3));
        }

        #endregion

        #region Test Helper Classes

        public class TestPayload
        {
            public string Message { get; set; }
        }

        #endregion
    }
}
