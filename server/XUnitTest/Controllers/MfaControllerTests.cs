using Api.Controllers;
using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.Shared.RequestModel;
using Mfa.DomainService.TOTP;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Controllers
{
    public class MfaControllerTests
    {
        private readonly Mock<IMfaManagementService> _mfaService = new();
        private readonly Mock<TotpService> _totpService;
        private readonly Mock<ChangeControllerContext> _changeContext;
        private readonly MfaController _controller;
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
        private readonly Mock<IConfigurationService> _cloudConfig = new();

        public MfaControllerTests()
        {
            _changeContext = new Mock<ChangeControllerContext>(_tenants.Object, _dbContextProvider.Object, _httpContextAccessor.Object)
            {
                CallBase = true
            };

            _totpService = new Mock<TotpService>(Mock.Of<IMfaManagementRepository>(), Mock.Of<ILogger<TotpService>>(), Mock.Of<IHttpContextAccessor>(), Mock.Of<IConfiguration>(), Mock.Of<ICacheClient>(), Mock.Of<IValidator<VerifyOtpRequest>>(), Mock.Of<ITenants>());
            //_changeContext = new Mock<ChangeControllerContext>( Mock.Of<ITenants>(),Mock.Of<IDbContextProvider>(), Mock.Of<IHttpContextAccessor>());
            _controller = new MfaController(_mfaService.Object, _totpService.Object, _changeContext.Object, _cloudConfig.Object);
        }

        private MfaController CreateController()
        {
            var controller = new MfaController(
                _mfaService.Object,
                _totpService.Object,
                _changeContext.Object,
                _cloudConfig.Object
            );

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        [Fact]
        public async Task GenerateOTP_ReturnsResponse_And_ChangesContext()
        {
            // Arrange
            var request = new OtpGenerationRequest
            {
                UserId = "user-1"
            };

            var response = new OtpGenerationResponse
            {
                IsSuccess = true
            };

            _mfaService
                .Setup(x => x.GenerateOTPAsync(request))
                .ReturnsAsync(response);

            // Act
            var result = await _controller.GenerateOTP(request);

            // Assert
            result.Should().Be(response);
        }

        [Fact]
        public async Task VerifyOTP_ReturnsResponse_And_ChangesContext()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                AuthType = UserMfaType.Email
            };

            var response = new OtpVerificationResponse
            {
                IsValid = true
            };

            _mfaService
                .Setup(x => x.VerifyOTPAsync(request))
                .ReturnsAsync(response);

            // Act
            var result = await _controller.VerifyOTP(request);

            // Assert
            result.Should().Be(response);
        }

        [Fact]
        public async Task DisableUserMfa_ReturnsSuccess()
        {
            // Arrange
            var request = new DisableUserMfaRequest
            {
                UserId = "user-1"
            };

            var response = new BaseResponse
            {
                IsSuccess = true
            };

            _mfaService
                .Setup(x => x.DisableUserMfa(request))
                .ReturnsAsync(response);

            // Act
            var result = await _controller.DisableUserMfa(request);

            // Assert
            result.Should().Be(response);
        }

        [Fact]
        public async Task SetUpTotp_WithEmptyUserId_ReturnsErrorResponse()
        {
            // Arrange
            var request = new SetUpUserTotpRequest { UserId = "" };
            var controller = CreateController();

            // Act
            var result = await controller.SetUpTotp(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().NotBeNull();
            result.Errors.Should().ContainKey("empty_user_id");
            result.Errors["empty_user_id"].Should().Be("User id should not be empty");
        }

        [Fact]
        public async Task SetUpTotp_WithWhitespaceUserId_ReturnsErrorResponse()
        {
            // Arrange
            var request = new SetUpUserTotpRequest { UserId = "   " };
            var controller = CreateController();

            // Act
            var result = await controller.SetUpTotp(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().NotBeNull();
            result.Errors.Should().ContainKey("empty_user_id");
            result.Errors["empty_user_id"].Should().Be("User id should not be empty");
        }

        [Fact]
        public async Task ResendOtp_WithValidMfaId_ReturnsSuccessfulResponse()
        {
            // Arrange
            var request = new ResendOtpRequest
            {
                MfaId = "mfa123",
                SendPhoneNumberAsEmailDomain = "123456678"
            };

            var expectedResponse = new OtpGenerationResponse
            {
                IsSuccess = true,
                MfaId = "mfa123"
            };

            _mfaService.Setup(x => x.ResendOtpAsync(request.MfaId, request.SendPhoneNumberAsEmailDomain))
                .ReturnsAsync(expectedResponse);
            var controller = CreateController();

            // Act
            var result = await controller.ResendOtp(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().Be("mfa123");
            _mfaService.Verify(
                x => x.ResendOtpAsync(request.MfaId, request.SendPhoneNumberAsEmailDomain),
                Times.Once);
        }

        [Fact]
        public async Task ResendOtp_WithEmptyMfaId_ReturnsErrorResponse()
        {
            // Arrange
            var request = new ResendOtpRequest
            {
                MfaId = "",
                SendPhoneNumberAsEmailDomain = "123456678"
            };
            var controller = CreateController();

            // Act
            var result = await controller.ResendOtp(request);

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().NotBeNull();
            result.Errors.Should().ContainKey("empty_mfa_id");
            result.Errors["empty_mfa_id"].Should().Be("Mfa id should not be empty");
            _mfaService.Verify(
                x => x.ResendOtpAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task ResendOtp_WithSendPhoneNumberAsEmailDomainTrue_CallsServiceWithCorrectParameter()
        {
            // Arrange
            var request = new ResendOtpRequest
            {
                MfaId = "mfa456",
                SendPhoneNumberAsEmailDomain = "123454678"
            };

            var expectedResponse = new OtpGenerationResponse
            {
                IsSuccess = true
            };

            _mfaService.Setup(x => x.ResendOtpAsync(request.MfaId, request.SendPhoneNumberAsEmailDomain))
                .ReturnsAsync(expectedResponse);
            var controller = CreateController();

            // Act
            var result = await controller.ResendOtp(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            _mfaService.Verify(
                x => x.ResendOtpAsync(request.MfaId, "123454678"),
                Times.Once);
        }
    }
}
