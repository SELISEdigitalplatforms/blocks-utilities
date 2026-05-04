using FluentAssertions;
using Moq;
using Utility.DomainService.Sequence;
using Utility.DomainService.Sequence.service;

namespace XUnitTest.Sequence
{
    public class SequenceServiceTests
    {
        private readonly Mock<ISequenceRepository> _mockRepository;
        private readonly SequenceService _service;

        public SequenceServiceTests()
        {
            _mockRepository = new Mock<ISequenceRepository>();
            _service = new SequenceService(_mockRepository.Object);
        }

        #region GetNextSequenceNumberAsync Tests

        [Fact]
        public async Task GetNextSequenceNumberAsync_ShouldReturnSuccess_WithNextNumber()
        {
            // Arrange
            var query = new SequenceNumberQuery { Context = "test-context" };
            var expectedNumber = 42L;
            _mockRepository.Setup(r => r.GetNextSequenceNumberAsync(query.Context))
                .ReturnsAsync(expectedNumber);

            // Act
            var result = await _service.GetNextSequenceNumberAsync(query);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.Context.Should().Be(query.Context);
            result.CurrentNumber.Should().Be(expectedNumber);
        }

        [Fact]
        public async Task GetNextSequenceNumberAsync_ShouldCallRepository_WithCorrectContext()
        {
            // Arrange
            var query = new SequenceNumberQuery { Context = "invoice-sequence" };
            _mockRepository.Setup(r => r.GetNextSequenceNumberAsync(It.IsAny<string>()))
                .ReturnsAsync(1L);

            // Act
            await _service.GetNextSequenceNumberAsync(query);

            // Assert
            _mockRepository.Verify(r => r.GetNextSequenceNumberAsync("invoice-sequence"), Times.Once);
        }

        [Fact]
        public async Task GetNextSequenceNumberAsync_ShouldReturnIncrementingNumbers()
        {
            // Arrange
            var query = new SequenceNumberQuery { Context = "test-sequence" };
            var callCount = 0;
            _mockRepository.Setup(r => r.GetNextSequenceNumberAsync(query.Context))
                .ReturnsAsync(() => ++callCount);

            // Act
            var result1 = await _service.GetNextSequenceNumberAsync(query);
            var result2 = await _service.GetNextSequenceNumberAsync(query);
            var result3 = await _service.GetNextSequenceNumberAsync(query);

            // Assert
            result1.CurrentNumber.Should().Be(1);
            result2.CurrentNumber.Should().Be(2);
            result3.CurrentNumber.Should().Be(3);
        }

        [Theory]
        [InlineData("")]
        [InlineData("orders")]
        [InlineData("invoices-2024")]
        [InlineData("user_registration")]
        public async Task GetNextSequenceNumberAsync_ShouldHandleVariousContexts(string context)
        {
            // Arrange
            var query = new SequenceNumberQuery { Context = context };
            _mockRepository.Setup(r => r.GetNextSequenceNumberAsync(context))
                .ReturnsAsync(100L);

            // Act
            var result = await _service.GetNextSequenceNumberAsync(query);

            // Assert
            result.Should().NotBeNull();
            result.Context.Should().Be(context);
            result.IsSuccess.Should().BeTrue();
        }

        #endregion

        #region GetNextHexSequenceNumberAsync Tests

        [Fact]
        public async Task GetNextHexSequenceNumberAsync_ShouldReturnHexString()
        {
            // Arrange
            var query = new SequenceNumberHexQuery { Context = "hex-context" };
            var number = 255L; // 0xFF in hex
            _mockRepository.Setup(r => r.GetNextHexSequenceNumberAsync(query.Context))
                .ReturnsAsync(number);

            // Act
            var result = await _service.GetNextHexSequenceNumberAsync(query);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.Context.Should().Be(query.Context);
            result.CurrentNumber.Should().NotBeNullOrEmpty();
            // Should be uppercase hex formatted to 9 characters
            result.CurrentNumber.Should().MatchRegex("^[0-9A-F]+$");
        }

        [Fact]
        public async Task GetNextHexSequenceNumberAsync_ShouldFormatToNineCharacters()
        {
            // Arrange
            var query = new SequenceNumberHexQuery { Context = "format-test" };
            var number = 1L;
            _mockRepository.Setup(r => r.GetNextHexSequenceNumberAsync(query.Context))
                .ReturnsAsync(number);

            // Act
            var result = await _service.GetNextHexSequenceNumberAsync(query);

            // Assert
            result.CurrentNumber.Should().HaveLength(9);
            result.CurrentNumber.Should().Be("000000001");
        }

        [Fact]
        public async Task GetNextHexSequenceNumberAsync_ShouldReturnUppercaseHex()
        {
            // Arrange
            var query = new SequenceNumberHexQuery { Context = "case-test" };
            var number = 2748L; // ABC in hex
            _mockRepository.Setup(r => r.GetNextHexSequenceNumberAsync(query.Context))
                .ReturnsAsync(number);

            // Act
            var result = await _service.GetNextHexSequenceNumberAsync(query);

            // Assert
            result.CurrentNumber.Should().Be("000000ABC");
        }

        [Theory]
        [InlineData(0L, "000000000")]
        [InlineData(16L, "000000010")]
        [InlineData(4095L, "000000FFF")]
        public async Task GetNextHexSequenceNumberAsync_ShouldCorrectlyConvertNumbers(long input, string expected)
        {
            // Arrange
            var query = new SequenceNumberHexQuery { Context = "conversion-test" };
            _mockRepository.Setup(r => r.GetNextHexSequenceNumberAsync(query.Context))
                .ReturnsAsync(input);

            // Act
            var result = await _service.GetNextHexSequenceNumberAsync(query);

            // Assert
            result.CurrentNumber.Should().Be(expected);
        }

        #endregion

        #region ResetSequenceNumberAsync Tests

        [Fact]
        public async Task ResetSequenceNumberAsync_ShouldReturnSuccess()
        {
            // Arrange
            var request = new ResetSequenceNumberRequest { Context = "reset-context", Value = 100 };
            _mockRepository.Setup(r => r.ResetSequenceNumberAsync(request.Context, request.Value))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.ResetSequenceNumberAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task ResetSequenceNumberAsync_ShouldCallRepository_WithCorrectParameters()
        {
            // Arrange
            var request = new ResetSequenceNumberRequest { Context = "invoice-seq", Value = 5000 };
            _mockRepository.Setup(r => r.ResetSequenceNumberAsync(It.IsAny<string>(), It.IsAny<long>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.ResetSequenceNumberAsync(request);

            // Assert
            _mockRepository.Verify(r => r.ResetSequenceNumberAsync("invoice-seq", 5000), Times.Once);
        }

        [Theory]
        [InlineData("context1", 0)]
        [InlineData("context2", 1)]
        [InlineData("context3", 999999)]
        public async Task ResetSequenceNumberAsync_ShouldHandleVariousValues(string context, long value)
        {
            // Arrange
            var request = new ResetSequenceNumberRequest { Context = context, Value = value };
            _mockRepository.Setup(r => r.ResetSequenceNumberAsync(context, value))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.ResetSequenceNumberAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _mockRepository.Verify(r => r.ResetSequenceNumberAsync(context, value), Times.Once);
        }

        #endregion
    }
}
