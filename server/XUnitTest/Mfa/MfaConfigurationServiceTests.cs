using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Moq;

namespace XUnitTest.Mfa
{
    public class MfaConfigurationServiceTests
    {
        private readonly Mock<IMfaManagementRepository> _repository;
        private readonly MfaConfigurationService _service;

        public MfaConfigurationServiceTests()
        {
            _repository = new Mock<IMfaManagementRepository>();
            _service = new MfaConfigurationService(_repository.Object);
        }

        #region GetAsync

        [Fact]
        public async Task GetAsync_WithExistingConfiguration_ReturnsMappedConfiguration()
        {
            // Arrange
            var repoConfig = new MfaConfiguration
            {
                Name = "Default",
                EnableMfa = true,
                UserMfaTypes = new List<UserMfaType> { UserMfaType.TOTP, UserMfaType.Email },
                MfaTemplate = new MfaTemplate
                {
                    TemplateName = "TestTemplate",
                    TemplateId = "template-123"
                }
            };

            _repository.Setup(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync(repoConfig);

            // Act
            var result = await _service.GetAsync();

            // Assert
            result.Should().NotBeNull();
            result!.EnableMfa.Should().BeTrue();
            result.UserMfaType.Should().HaveCount(2);
            result.UserMfaType.Should().Contain(UserMfaType.TOTP);
            result.UserMfaType.Should().Contain(UserMfaType.Email);
            result.MfaTemplate.Should().NotBeNull();
            result.MfaTemplate!.TemplateName.Should().Be("TestTemplate");
            result.MfaTemplate.TemplateId.Should().Be("template-123");

            _repository.Verify(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetAsync_WithNullConfiguration_ReturnsDefaultConfiguration()
        {
            // Arrange
            _repository.Setup(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync((MfaConfiguration?)null);

            // Act
            var result = await _service.GetAsync();

            // Assert
            result.Should().NotBeNull();
            result!.EnableMfa.Should().BeFalse();
            result.UserMfaType.Should().NotBeNull();
            result.UserMfaType.Should().BeEmpty();
            result.MfaTemplate.Should().NotBeNull();
            result.MfaTemplate!.TemplateName.Should().BeNull();
            result.MfaTemplate.TemplateId.Should().BeNull();

            _repository.Verify(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()), Times.Once);
        }

        [Theory]
        [InlineData(true, UserMfaType.TOTP)]
        [InlineData(true, UserMfaType.Email)]
        [InlineData(true, UserMfaType.Sms)]
        [InlineData(false, UserMfaType.None)]
        public async Task GetAsync_WithDifferentMfaSettings_MapsCorrectly(bool enableMfa, UserMfaType mfaType)
        {
            // Arrange
            var repoConfig = new MfaConfiguration
            {
                Name = "Default",
                EnableMfa = enableMfa,
                UserMfaTypes = new List<UserMfaType> { mfaType },
                MfaTemplate = new MfaTemplate { TemplateName = "Test", TemplateId = "123" }
            };

            _repository.Setup(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync(repoConfig);

            // Act
            var result = await _service.GetAsync();

            // Assert
            result.Should().NotBeNull();
            result!.EnableMfa.Should().Be(enableMfa);
            result.UserMfaType.Should().Contain(mfaType);
        }

        [Fact]
        public async Task GetAsync_WithMultipleMfaTypes_MapsAllTypes()
        {
            // Arrange
            var repoConfig = new MfaConfiguration
            {
                Name = "Default",
                EnableMfa = true,
                UserMfaTypes = new List<UserMfaType>
                {
                    UserMfaType.TOTP,
                    UserMfaType.Email,
                    UserMfaType.Sms,
                    UserMfaType.WhatsApp
                },
                MfaTemplate = new MfaTemplate { TemplateName = "Multi", TemplateId = "multi-123" }
            };

            _repository.Setup(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync(repoConfig);

            // Act
            var result = await _service.GetAsync();

            // Assert
            result.Should().NotBeNull();
            result!.UserMfaType.Should().HaveCount(4);
            result.UserMfaType.Should().Contain(UserMfaType.TOTP);
            result.UserMfaType.Should().Contain(UserMfaType.Email);
            result.UserMfaType.Should().Contain(UserMfaType.Sms);
            result.UserMfaType.Should().Contain(UserMfaType.WhatsApp);
        }

        [Fact]
        public async Task GetAsync_WithEmptyMfaTypes_ReturnsEmptyList()
        {
            // Arrange
            var repoConfig = new MfaConfiguration
            {
                Name = "Default",
                EnableMfa = false,
                UserMfaTypes = new List<UserMfaType>(),
                MfaTemplate = new MfaTemplate { TemplateName = "Empty", TemplateId = "empty-123" }
            };

            _repository.Setup(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync(repoConfig);

            // Act
            var result = await _service.GetAsync();

            // Assert
            result.Should().NotBeNull();
            result!.UserMfaType.Should().NotBeNull();
            result.UserMfaType.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAsync_CallsRepositoryOnce()
        {
            // Arrange
            _repository.Setup(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync((MfaConfiguration?)null);

            // Act
            await _service.GetAsync();

            // Assert
            _repository.Verify(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetAsync_WithNullMfaTemplate_CreatesNewTemplate()
        {
            // Arrange
            var repoConfig = new MfaConfiguration
            {
                Name = "Default",
                EnableMfa = true,
                UserMfaTypes = new List<UserMfaType> { UserMfaType.TOTP },
                MfaTemplate = null!
            };

            _repository.Setup(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync(repoConfig);

            // Act
            var result = await _service.GetAsync();

            // Assert
            result.Should().NotBeNull();
            result!.MfaTemplate.Should().BeNull();
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task GetAsync_WithExistingConfig_AndDefaultValues_ReturnsCorrectly()
        {
            // Arrange
            var repoConfig = new MfaConfiguration
            {
                Name = "Default",
                EnableMfa = false,
                UserMfaTypes = new List<UserMfaType> { UserMfaType.None },
                MfaTemplate = new MfaTemplate()
            };

            _repository.Setup(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync(repoConfig);

            // Act
            var result = await _service.GetAsync();

            // Assert
            result.Should().NotBeNull();
            result!.EnableMfa.Should().BeFalse();
            result.UserMfaType.Should().ContainSingle();
            result.UserMfaType.First().Should().Be(UserMfaType.None);
        }

        [Fact]
        public async Task GetAsync_MultipleCalls_CallsRepositoryMultipleTimes()
        {
            // Arrange
            _repository.Setup(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync((MfaConfiguration?)null);

            // Act
            await _service.GetAsync();
            await _service.GetAsync();
            await _service.GetAsync();

            // Assert
            _repository.Verify(x => x.GetItemAsync<MfaConfiguration>(
                It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(),
                It.IsAny<string>()), Times.Exactly(3));
        }

        #endregion
    }
}
