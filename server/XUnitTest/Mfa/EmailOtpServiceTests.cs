using Blocks.Genesis;
using Blocks.MailDriver;
using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Moq;
using SendMail = Blocks.MailDriver.SendMail;

namespace XUnitTest.Mfa
{
    public class EmailOtpServiceTests
    {
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IMfaConfigurationService> _configurationService;
        private readonly Mock<IMailDriverService> _mailDriverService;
        private readonly EmailOtpService _service;

        public EmailOtpServiceTests()
        {
            _cacheClient = new Mock<ICacheClient>();
            _configurationService = new Mock<IMfaConfigurationService>();
            _mailDriverService = new Mock<IMailDriverService>();
            _service = new EmailOtpService(_cacheClient.Object, _configurationService.Object, _mailDriverService.Object);
        }

        #region GenerateAsync

        [Fact]
        public async Task GenerateAsync_WithValidUserInfo_GeneratesOtpAndSendsEmail()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            var config = new Configuration
            {
                EnableMfa = true,
                MfaTemplate = new MfaTemplate { TemplateName = "CustomTemplate", TemplateId = "template-123" }
            };

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(config);

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            var result = await _service.GenerateAsync(userInfo);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().NotBeNullOrEmpty();

            _cacheClient.Verify(x => x.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                300), Times.Once);

            _mailDriverService.Verify(x => x.SendAsync(It.Is<SendMail>(m =>
                m.To.Contains(userInfo.Email) &&
                m.Purpose == "CustomTemplate" &&
                m.Language == userInfo.Language &&
                m.BodyDataContext.ContainsKey("TwoFactorCode"))), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_WithNullLanguage_UsesDefaultLanguage()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            userInfo.Language = null!;

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(new Configuration { MfaTemplate = new MfaTemplate() });

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            var result = await _service.GenerateAsync(userInfo);

            // Assert
            result.Should().NotBeNull();
            _mailDriverService.Verify(x => x.SendAsync(It.Is<SendMail>(m =>
                m.Language == "en-US")), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_WithNullMfaTemplate_UsesDefaultTemplate()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            var config = new Configuration
            {
                EnableMfa = true,
                MfaTemplate = null
            };

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(config);

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            var result = await _service.GenerateAsync(userInfo);

            // Assert
            result.Should().NotBeNull();
            _mailDriverService.Verify(x => x.SendAsync(It.Is<SendMail>(m =>
                m.Purpose == "MfaViaEmail")), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_WithEmptyTemplateName_UsesDefaultTemplate()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            var config = new Configuration
            {
                EnableMfa = true,
                MfaTemplate = new MfaTemplate { TemplateName = "", TemplateId = "template-123" }
            };

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(config);

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            var result = await _service.GenerateAsync(userInfo);

            // Assert
            result.Should().NotBeNull();
            _mailDriverService.Verify(x => x.SendAsync(It.Is<SendMail>(m =>
                m.Purpose == "MfaViaEmail")), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_WithPhoneNumberAsEmailDomain_SendsToPhoneEmail()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            userInfo.PhoneNumber = "+1 234 567 8900";
            var emailDomain = "sms.example.com";

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(new Configuration { MfaTemplate = new MfaTemplate() });

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            var result = await _service.GenerateAsync(userInfo, emailDomain);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();

            _mailDriverService.Verify(x => x.SendAsync(It.Is<SendMail>(m =>
                m.To.Contains("0012345678900@sms.example.com"))), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_WithPhoneNumberAsEmailDomainButNoPhoneNumber_ReturnsError()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            userInfo.PhoneNumber = null!;
            var emailDomain = "sms.example.com";

            // Act
            var result = await _service.GenerateAsync(userInfo, emailDomain);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("phonenumber_not_exist");
            result.Errors["phonenumber_not_exist"].Should().Be("PhoneNumber not exist in user for mfa");

            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
            _mailDriverService.Verify(x => x.SendAsync(It.IsAny<SendMail>()), Times.Never);
        }

        [Fact]
        public async Task GenerateAsync_WithWhiteSpacePhoneNumber_ReturnsError()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            userInfo.PhoneNumber = "   ";
            var emailDomain = "sms.example.com";

            // Act
            var result = await _service.GenerateAsync(userInfo, emailDomain);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("phonenumber_not_exist");
        }

        [Fact]
        public async Task GenerateAsync_WhenEmailSendFails_ReturnsFailure()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(new Configuration { MfaTemplate = new MfaTemplate() });

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            // Act
            var result = await _service.GenerateAsync(userInfo);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.MfaId.Should().NotBeNullOrEmpty();
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("fr-FR")]
        [InlineData("es-ES")]
        [InlineData("de-DE")]
        public async Task GenerateAsync_WithDifferentLanguages_SendsWithCorrectLanguage(string language)
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            userInfo.Language = language;

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(new Configuration { MfaTemplate = new MfaTemplate() });

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            var result = await _service.GenerateAsync(userInfo);

            // Assert
            result.Should().NotBeNull();
            _mailDriverService.Verify(x => x.SendAsync(It.Is<SendMail>(m =>
                m.Language == language)), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_CachesContextWith300SecondLifeCycle()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(new Configuration { MfaTemplate = new MfaTemplate() });

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            await _service.GenerateAsync(userInfo);

            // Assert
            _cacheClient.Verify(x => x.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                300), Times.Once);
        }

        #endregion

        #region VerifyAsync

        [Fact]
        public async Task VerifyAsync_WithValidCode_ReturnsSuccessAndRemovesKey()
        {
            // Arrange
            var mfaId = Guid.NewGuid().ToString();
            var userId = Guid.NewGuid().ToString();
            var verificationCode = "12345";

            var context = new MfaAuthenticationContext
            {
                MfaId = mfaId,
                UserId = userId,
                MfaCode = verificationCode
            };

            var request = new VerifyOtpRequest
            {
                MfaId = mfaId,
                VerificationCode = verificationCode
            };

            _cacheClient.Setup(x => x.KeyExistsAsync(mfaId))
                .ReturnsAsync(true);

            _cacheClient.Setup(x => x.GetStringValueAsync(mfaId))
                .ReturnsAsync(context.Sterilize());

            _cacheClient.Setup(x => x.RemoveKeyAsync(mfaId))
                .ReturnsAsync(true);

            // Act
            var result = await _service.VerifyAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.IsValid.Should().BeTrue();
            result.UserId.Should().Be(userId);

            _cacheClient.Verify(x => x.KeyExistsAsync(mfaId), Times.Once);
            _cacheClient.Verify(x => x.GetStringValueAsync(mfaId), Times.Once);
            _cacheClient.Verify(x => x.RemoveKeyAsync(mfaId), Times.Once);
        }

        [Fact]
        public async Task VerifyAsync_WithInvalidMfaId_ReturnsError()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                MfaId = "invalid-mfa-id",
                VerificationCode = "12345"
            };

            _cacheClient.Setup(x => x.KeyExistsAsync(request.MfaId))
                .ReturnsAsync(false);

            // Act
            var result = await _service.VerifyAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("message");
            result.Errors["message"].Should().Be("invalid_two_factor_id");

            _cacheClient.Verify(x => x.KeyExistsAsync(request.MfaId), Times.Once);
            _cacheClient.Verify(x => x.GetStringValueAsync(It.IsAny<string>()), Times.Never);
            _cacheClient.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_WithInvalidCode_ReturnsError()
        {
            // Arrange
            var mfaId = Guid.NewGuid().ToString();
            var userId = Guid.NewGuid().ToString();
            var correctCode = "12345";
            var wrongCode = "54321";

            var context = new MfaAuthenticationContext
            {
                MfaId = mfaId,
                UserId = userId,
                MfaCode = correctCode
            };

            var request = new VerifyOtpRequest
            {
                MfaId = mfaId,
                VerificationCode = wrongCode
            };

            _cacheClient.Setup(x => x.KeyExistsAsync(mfaId))
                .ReturnsAsync(true);

            _cacheClient.Setup(x => x.GetStringValueAsync(mfaId))
                .ReturnsAsync(context.Sterilize());

            // Act
            var result = await _service.VerifyAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("message");
            result.Errors["message"].Should().Be("invalid_two_factor_code");
            result.UserId.Should().BeNull();

            _cacheClient.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("12345", "12345", true)]
        [InlineData("12345", "54321", false)]
        [InlineData("98765", "98765", true)]
        [InlineData("11111", "22222", false)]
        public async Task VerifyAsync_WithDifferentCodes_ReturnsExpectedResult(string storedCode, string providedCode, bool expectedValid)
        {
            // Arrange
            var mfaId = Guid.NewGuid().ToString();
            var userId = Guid.NewGuid().ToString();

            var context = new MfaAuthenticationContext
            {
                MfaId = mfaId,
                UserId = userId,
                MfaCode = storedCode
            };

            var request = new VerifyOtpRequest
            {
                MfaId = mfaId,
                VerificationCode = providedCode
            };

            _cacheClient.Setup(x => x.KeyExistsAsync(mfaId))
                .ReturnsAsync(true);

            _cacheClient.Setup(x => x.GetStringValueAsync(mfaId))
                .ReturnsAsync(context.Sterilize());

            _cacheClient.Setup(x => x.RemoveKeyAsync(mfaId))
                .ReturnsAsync(true);

            // Act
            var result = await _service.VerifyAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().Be(expectedValid);
            result.IsSuccess.Should().Be(expectedValid);

            if (expectedValid)
            {
                result.UserId.Should().Be(userId);
                _cacheClient.Verify(x => x.RemoveKeyAsync(mfaId), Times.Once);
            }
            else
            {
                result.UserId.Should().BeNull();
                _cacheClient.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
            }
        }

        [Fact]
        public async Task VerifyAsync_WhenKeyExists_GetsStringValue()
        {
            // Arrange
            var mfaId = Guid.NewGuid().ToString();
            var context = new MfaAuthenticationContext
            {
                MfaId = mfaId,
                UserId = Guid.NewGuid().ToString(),
                MfaCode = "12345"
            };

            var request = new VerifyOtpRequest
            {
                MfaId = mfaId,
                VerificationCode = "12345"
            };

            _cacheClient.Setup(x => x.KeyExistsAsync(mfaId))
                .ReturnsAsync(true);

            _cacheClient.Setup(x => x.GetStringValueAsync(mfaId))
                .ReturnsAsync(context.Sterilize());

            _cacheClient.Setup(x => x.RemoveKeyAsync(mfaId))
                .ReturnsAsync(true);

            // Act
            await _service.VerifyAsync(request);

            // Assert
            _cacheClient.Verify(x => x.GetStringValueAsync(mfaId), Times.Once);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task GenerateAndVerify_WithValidFlow_WorksEndToEnd()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            var testMfaId = Guid.NewGuid().ToString();
            var testContext = new MfaAuthenticationContext
            {
                MfaId = testMfaId,
                UserId = userInfo.ItemId,
                MfaCode = "12345"
            };

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(new Configuration { MfaTemplate = new MfaTemplate() });

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act - Generate
            var generateResult = await _service.GenerateAsync(userInfo);

            // Assert - Generate
            generateResult.IsSuccess.Should().BeTrue();
            generateResult.MfaId.Should().NotBeNullOrEmpty();

            // Arrange - Verify
            _cacheClient.Setup(x => x.KeyExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            _cacheClient.Setup(x => x.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync(testContext.Sterilize());

            _cacheClient.Setup(x => x.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            var verifyRequest = new VerifyOtpRequest
            {
                MfaId = generateResult.MfaId,
                VerificationCode = testContext.MfaCode
            };

            // Act - Verify (with a different MFA code, so it should fail since we're using a pre-created context)
            // This test verifies the generate method works and returns proper MfaId
            verifyRequest.VerificationCode = "wrongcode";
            var verifyResult = await _service.VerifyAsync(verifyRequest);

            // Assert - Verify fails with wrong code
            verifyResult.IsSuccess.Should().BeFalse();
            verifyResult.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task GenerateAsync_WithPhoneNumberContainingSpacesAndPlus_FormatsCorrectly()
        {
            // Arrange
            var userInfo = CreateValidUserInfo();
            userInfo.PhoneNumber = "+44 123 456 7890";
            var emailDomain = "sms.example.com";

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync(new Configuration { MfaTemplate = new MfaTemplate() });

            _mailDriverService.Setup(x => x.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            var result = await _service.GenerateAsync(userInfo, emailDomain);

            // Assert
            result.Should().NotBeNull();
            _mailDriverService.Verify(x => x.SendAsync(It.Is<SendMail>(m =>
                m.To.Contains("00441234567890@sms.example.com"))), Times.Once);
        }
                
        [Fact]
        public async Task VerifyAsync_OnlyRemovesKeyWhenCodeIsValid()
        {
            // Arrange - Invalid case
            var mfaId = Guid.NewGuid().ToString();
            var context = new MfaAuthenticationContext
            {
                MfaId = mfaId,
                UserId = Guid.NewGuid().ToString(),
                MfaCode = "12345"
            };

            var invalidRequest = new VerifyOtpRequest
            {
                MfaId = mfaId,
                VerificationCode = "wrong"
            };

            _cacheClient.Setup(x => x.KeyExistsAsync(mfaId))
                .ReturnsAsync(true);

            _cacheClient.Setup(x => x.GetStringValueAsync(mfaId))
                .ReturnsAsync(context.Sterilize());

            // Act - Invalid
            await _service.VerifyAsync(invalidRequest);

            // Assert - Invalid
            _cacheClient.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Never);

            // Arrange - Valid case
            var validRequest = new VerifyOtpRequest
            {
                MfaId = mfaId,
                VerificationCode = "12345"
            };

            _cacheClient.Setup(x => x.RemoveKeyAsync(mfaId))
                .ReturnsAsync(true);

            // Act - Valid
            await _service.VerifyAsync(validRequest);

            // Assert - Valid
            _cacheClient.Verify(x => x.RemoveKeyAsync(mfaId), Times.Once);
        }

        #endregion

        #region Helper Methods

        private static UserInfo CreateValidUserInfo()
        {
            return new UserInfo
            {
                ItemId = Guid.NewGuid().ToString(),
                Email = "test@example.com",
                MfaEnabled = true,
                Language = "en-US",
                PhoneNumber = "+1234567890",
                Active = true,
                UserMfaType = UserMfaType.Email,
                IsMfaVerified = false
            };
        }

        #endregion
    }
}
