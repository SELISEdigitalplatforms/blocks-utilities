using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Shared;
using Mfa.DomainService.Validators;

namespace XUnitTest.DomainService.Mfa
{
    public class VerifyOtpRequestValidatorTests
    {
        private readonly VerifyOtpRequestValidator _validator;

        public VerifyOtpRequestValidatorTests()
        {
            _validator = new VerifyOtpRequestValidator();
        }

        #region Valid Request Tests

        [Theory]
        [InlineData("1234", "mfa-id-123")]
        [InlineData("12345", "mfa-id-456")]
        [InlineData("123456", "mfa-id-789")]
        public void Validate_WithValidRequest_ReturnsSuccess(string verificationCode, string mfaId)
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = verificationCode,
                MfaId = mfaId,
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        #endregion

        #region VerificationCode Validation Tests

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_WithEmptyVerificationCode_ReturnsVerificationCodeRequiredError(string verificationCode)
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = verificationCode,
                MfaId = "valid-mfa-id",
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors[0].ErrorMessage.Should().Be(VerifyOtpRequestValidator.VerificationCodeRequired);
            result.Errors[0].PropertyName.Should().Be("VerificationCode");
        }

        [Theory]
        [InlineData("123")]
        [InlineData("1234567")]
        [InlineData("12")]
        [InlineData("1")]
        public void Validate_WithInvalidVerificationCodeLength_ReturnsLengthError(string verificationCode)
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = verificationCode,
                MfaId = "valid-mfa-id",
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors[0].ErrorMessage.Should().Be(VerifyOtpRequestValidator.VerificationCodeLength);
            result.Errors[0].PropertyName.Should().Be("VerificationCode");
        }

        [Theory]
        [InlineData("abcd")]
        [InlineData("12a4")]
        [InlineData("ABCD")]
        [InlineData("12-45")]
        [InlineData("12 45")]
        [InlineData("12.45")]
        public void Validate_WithNonNumericVerificationCode_ReturnsNumericError(string verificationCode)
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = verificationCode,
                MfaId = "valid-mfa-id",
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors[0].ErrorMessage.Should().Be(VerifyOtpRequestValidator.VerificationCodeNumeric);
            result.Errors[0].PropertyName.Should().Be("VerificationCode");
        }

        [Fact]
        public void Validate_WithInvalidVerificationCodeLength_StopsAtFirstError()
        {
            // Arrange - Code is too short AND non-numeric, but should only report length error due to CascadeMode.Stop
            var request = new VerifyOtpRequest
            {
                VerificationCode = "ab",
                MfaId = "valid-mfa-id",
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors[0].ErrorMessage.Should().Be(VerifyOtpRequestValidator.VerificationCodeLength);
        }

        #endregion

        #region MfaId Validation Tests

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_WithEmptyMfaId_ReturnsMfaRequiredError(string mfaId)
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = "1234",
                MfaId = mfaId,
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == VerifyOtpRequestValidator.MfaRequired);
            result.Errors.Should().Contain(e => e.PropertyName == "MfaId");
        }

        [Fact]
        public void Validate_WithMfaIdExceedingMaxLength_ReturnsMaxLimitError()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = "1234",
                MfaId = new string('a', 51), // 51 characters
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(1);
            result.Errors[0].ErrorMessage.Should().Be(VerifyOtpRequestValidator.MfaMaxLimit);
            result.Errors[0].PropertyName.Should().Be("MfaId");
        }

        [Fact]
        public void Validate_WithMfaIdAtMaxLength_ReturnsSuccess()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = "1234",
                MfaId = new string('a', 50), // Exactly 50 characters
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        #endregion

        #region Multiple Validation Errors Tests

        [Fact]
        public void Validate_WithMultipleErrors_ReturnsAllErrors()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = null,
                MfaId = null,
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(2);
            result.Errors.Should().Contain(e => e.ErrorMessage == VerifyOtpRequestValidator.VerificationCodeRequired);
            result.Errors.Should().Contain(e => e.ErrorMessage == VerifyOtpRequestValidator.MfaRequired);
        }

        [Fact]
        public void Validate_WithInvalidVerificationCodeAndInvalidMfaId_ReturnsMultipleErrors()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = "abc",
                MfaId = new string('a', 51),
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(2);
            result.Errors.Should().Contain(e => e.PropertyName == "VerificationCode");
            result.Errors.Should().Contain(e => e.PropertyName == "MfaId");
        }

        #endregion

        #region Boundary Tests

        [Theory]
        [InlineData("0000")]
        [InlineData("9999")]
        [InlineData("000000")]
        [InlineData("999999")]
        public void Validate_WithBoundaryValidVerificationCodes_ReturnsSuccess(string verificationCode)
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = verificationCode,
                MfaId = "valid-mfa-id",
                AuthType = UserMfaType.Email
            };

            // Act
            var result = _validator.Validate(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        #endregion

        #region Constants Tests

        [Fact]
        public void Constants_ShouldHaveCorrectValues()
        {
            // Assert
            VerifyOtpRequestValidator.MfaRequired.Should().Be("Mfa_Required");
            VerifyOtpRequestValidator.MfaMaxLimit.Should().Be("Mfa_MaxLimit_50_Exceed");
            VerifyOtpRequestValidator.VerificationCodeRequired.Should().Be("Verification_Code_Required");
            VerifyOtpRequestValidator.VerificationCodeLength.Should().Be("Verification_Code_Length_4_To_6");
            VerifyOtpRequestValidator.VerificationCodeNumeric.Should().Be("Verification_Code_Should_Be_Numeric");
        }

        #endregion
    }
}
