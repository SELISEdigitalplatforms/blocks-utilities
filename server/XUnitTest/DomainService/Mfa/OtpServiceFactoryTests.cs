using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.TOTP;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace XUnitTest.DomainService.Mfa
{
    public class OtpServiceFactoryTests
    {
        private readonly Mock<IServiceProvider> _serviceProvider;
        private readonly OtpServiceFactory _factory;

        public OtpServiceFactoryTests()
        {
            _serviceProvider = new Mock<IServiceProvider>();
            _factory = new OtpServiceFactory(_serviceProvider.Object);
        }

        [Fact]
        public void GetOTPService_WithTOTP_ReturnsTotpService()
        {
            // Arrange
            var mockTotpService = new Mock<TotpService>(
                Mock.Of<IMfaManagementRepository>(),
                null, // ILogger
                null, // IHttpContextAccessor
                null, // IConfiguration
                Mock.Of<ICacheClient>(),
                null, // IValidator
                null  // ITenants
            );

            _serviceProvider.Setup(x => x.GetService(typeof(TotpService)))
                .Returns(mockTotpService.Object);

            // Act
            var result = _factory.GetOTPService(UserMfaType.TOTP);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeAssignableTo<IOtpService>();
            _serviceProvider.Verify(x => x.GetService(typeof(TotpService)), Times.Once);
        }

        [Fact]
        public void GetOTPService_WithEmail_ReturnsEmailOtpService()
        {
            // Arrange
            var mockEmailService = new Mock<EmailOtpService>(
                Mock.Of<ICacheClient>(),
                null, // IMfaConfigurationService
                null  // IMailDriverService
            );

            _serviceProvider.Setup(x => x.GetService(typeof(EmailOtpService)))
                .Returns(mockEmailService.Object);

            // Act
            var result = _factory.GetOTPService(UserMfaType.Email);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeAssignableTo<IOtpService>();
            _serviceProvider.Verify(x => x.GetService(typeof(EmailOtpService)), Times.Once);
        }

        [Theory]
        [InlineData(UserMfaType.None)]
        [InlineData((UserMfaType)999)]
        public void GetOTPService_WithInvalidMfaType_ThrowsArgumentException(UserMfaType invalidType)
        {
            // Act
            Action act = () => _factory.GetOTPService(invalidType);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Invalid MfaAuthType*");
        }

        [Fact]
        public void GetOTPService_WhenTotpServiceNotRegistered_ThrowsInvalidOperationException()
        {
            // Arrange
            _serviceProvider.Setup(x => x.GetService(typeof(TotpService)))
                .Returns(null);

            // Act
            Action act = () => _factory.GetOTPService(UserMfaType.TOTP);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void GetOTPService_WhenEmailServiceNotRegistered_ThrowsInvalidOperationException()
        {
            // Arrange
            _serviceProvider.Setup(x => x.GetService(typeof(EmailOtpService)))
                .Returns(null);

            // Act
            Action act = () => _factory.GetOTPService(UserMfaType.Email);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }
    }
}
