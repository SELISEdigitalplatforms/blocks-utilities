using DomainService.Entities;
using DomainService.Services;
using DomainService.Worker;
using FluentAssertions;
using Mfa.DomainService.Configuration;
using Moq;

namespace XUnitTest.DomainService.Worker
{
    public class UpdateMfaConfigurationServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authenticationRepository = new();
        private readonly UpdateMfaConfigurationService _service;
        private const string MfaGrantType = "mfa_code";

        public UpdateMfaConfigurationServiceTests()
        {
            _service = new UpdateMfaConfigurationService(_authenticationRepository.Object);
        }

        [Fact]
        public async Task Consume_WithIsEnableTrue_AndMfaNotInList_AddsMfaGrantType()
        {
            // Arrange
            var config = new AuthenticationConfiguration
            {
                AllowedGrantTypes = new List<string> { "password", "refresh_token" }
            };
            var mfaEvent = new MfaActionEvent { IsEnable = true, ProjectKey = "test-project" };

            _authenticationRepository.Setup(x => x.GetAuthenticationConfigurationAsync()).ReturnsAsync(config);
            _authenticationRepository.Setup(x => x.UpdateAuthenticationConfigurationAsync(It.IsAny<AuthenticationConfiguration>())).Returns(Task.CompletedTask);

            // Act
            await _service.Consume(mfaEvent);

            // Assert
            config.AllowedGrantTypes.Should().Contain(MfaGrantType);
            config.AllowedGrantTypes.Should().HaveCount(3);
            _authenticationRepository.Verify(x => x.UpdateAuthenticationConfigurationAsync(config), Times.Once);
        }

        [Fact]
        public async Task Consume_WithIsEnableTrue_AndMfaAlreadyInList_DoesNotAddDuplicate()
        {
            // Arrange
            var config = new AuthenticationConfiguration
            {
                AllowedGrantTypes = new List<string> { "password", MfaGrantType, "refresh_token" }
            };
            var mfaEvent = new MfaActionEvent { IsEnable = true, ProjectKey = "test-project" };

            _authenticationRepository.Setup(x => x.GetAuthenticationConfigurationAsync()).ReturnsAsync(config);
            _authenticationRepository.Setup(x => x.UpdateAuthenticationConfigurationAsync(It.IsAny<AuthenticationConfiguration>())).Returns(Task.CompletedTask);

            // Act
            await _service.Consume(mfaEvent);

            // Assert
            config.AllowedGrantTypes.Should().Contain(MfaGrantType);
            config.AllowedGrantTypes.Should().HaveCount(3);
            config.AllowedGrantTypes.Count(x => x == MfaGrantType).Should().Be(1);
            _authenticationRepository.Verify(x => x.UpdateAuthenticationConfigurationAsync(config), Times.Once);
        }

        [Fact]
        public async Task Consume_WithIsEnableFalse_RemovesMfaGrantType()
        {
            // Arrange
            var config = new AuthenticationConfiguration
            {
                AllowedGrantTypes = new List<string> { "password", MfaGrantType, "refresh_token" }
            };
            var mfaEvent = new MfaActionEvent { IsEnable = false, ProjectKey = "test-project" };

            _authenticationRepository.Setup(x => x.GetAuthenticationConfigurationAsync()).ReturnsAsync(config);
            _authenticationRepository.Setup(x => x.UpdateAuthenticationConfigurationAsync(It.IsAny<AuthenticationConfiguration>())).Returns(Task.CompletedTask);

            // Act
            await _service.Consume(mfaEvent);

            // Assert
            config.AllowedGrantTypes.Should().NotContain(MfaGrantType);
            config.AllowedGrantTypes.Should().HaveCount(2);
            _authenticationRepository.Verify(x => x.UpdateAuthenticationConfigurationAsync(config), Times.Once);
        }

        [Fact]
        public async Task Consume_WithIsEnableFalse_AndMfaNotInList_DoesNotThrow()
        {
            // Arrange
            var config = new AuthenticationConfiguration
            {
                AllowedGrantTypes = new List<string> { "password", "refresh_token" }
            };
            var mfaEvent = new MfaActionEvent { IsEnable = false, ProjectKey = "test-project" };

            _authenticationRepository.Setup(x => x.GetAuthenticationConfigurationAsync()).ReturnsAsync(config);
            _authenticationRepository.Setup(x => x.UpdateAuthenticationConfigurationAsync(It.IsAny<AuthenticationConfiguration>())).Returns(Task.CompletedTask);

            // Act
            await _service.Consume(mfaEvent);

            // Assert
            config.AllowedGrantTypes.Should().NotContain(MfaGrantType);
            config.AllowedGrantTypes.Should().HaveCount(2);
            _authenticationRepository.Verify(x => x.UpdateAuthenticationConfigurationAsync(config), Times.Once);
        }
    }
}
