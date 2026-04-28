using FluentAssertions;
using Mfa.DomainService.Services;

namespace XUnitTest.DomainService.Mfa
{
    public class MfaAuthenticationContextTests
    {
        [Fact]
        public void Create_ShouldReturnContextWithValidProperties()
        {
            // Arrange
            var mfaId = "test-mfa-id";
            var userId = "test-user-id";

            // Act
            var result = MfaAuthenticationContext.Create(mfaId, userId);

            // Assert
            result.Should().NotBeNull();
            result.MfaId.Should().Be(mfaId);
            result.UserId.Should().Be(userId);
            result.MfaCode.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Create_ShouldGenerateFiveDigitMfaCode()
        {
            // Arrange
            var mfaId = "test-mfa-id";
            var userId = "test-user-id";

            // Act
            var result = MfaAuthenticationContext.Create(mfaId, userId);

            // Assert
            result.MfaCode.Should().MatchRegex(@"^\d{5}$");
            int.Parse(result.MfaCode).Should().BeInRange(11111, 99999);
        }

        [Fact]
        public void GenerateSecureRandomNumber_ShouldReturnFiveDigitNumber()
        {
            // Act
            var result = MfaAuthenticationContext.GenerateSecureRandomNumber();

            // Assert
            result.Should().MatchRegex(@"^\d{5}$");
            int.Parse(result).Should().BeInRange(11111, 99999);
        }

        [Theory]
        [InlineData(100)]
        public void GenerateSecureRandomNumber_ShouldGenerateDifferentNumbers(int iterations)
        {
            // Arrange
            var generatedNumbers = new HashSet<string>();

            // Act
            for (int i = 0; i < iterations; i++)
            {
                generatedNumbers.Add(MfaAuthenticationContext.GenerateSecureRandomNumber());
            }

            // Assert
            generatedNumbers.Count.Should().BeGreaterThan(1, "generated numbers should be random");
        }

        [Fact]
        public void Sterilize_ShouldSerializeContextToJson()
        {
            // Arrange
            var context = new MfaAuthenticationContext
            {
                UserId = "user-123",
                MfaId = "mfa-456",
                MfaCode = "12345"
            };

            // Act
            var result = context.Sterilize();

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().Contain("user-123");
            result.Should().Contain("mfa-456");
            result.Should().Contain("12345");
        }

        [Fact]
        public void Deserialize_ShouldDeserializeJsonToContext()
        {
            // Arrange
            var json = "{\"UserId\":\"user-123\",\"MfaId\":\"mfa-456\",\"MfaCode\":\"12345\"}";

            // Act
            var result = MfaAuthenticationContext.Deserialize(json);

            // Assert
            result.Should().NotBeNull();
            result.UserId.Should().Be("user-123");
            result.MfaId.Should().Be("mfa-456");
            result.MfaCode.Should().Be("12345");
        }

        [Fact]
        public void Sterilize_AndDeserialize_ShouldRoundTripSuccessfully()
        {
            // Arrange
            var original = MfaAuthenticationContext.Create("mfa-789", "user-789");

            // Act
            var serialized = original.Sterilize();
            var deserialized = MfaAuthenticationContext.Deserialize(serialized);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.UserId.Should().Be(original.UserId);
            deserialized.MfaId.Should().Be(original.MfaId);
            deserialized.MfaCode.Should().Be(original.MfaCode);
        }
    }
}
