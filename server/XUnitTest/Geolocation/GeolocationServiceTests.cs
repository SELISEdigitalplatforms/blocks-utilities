using FluentAssertions;
using Moq;
using Utility.DomainService.Geolocation;
using Utility.DomainService.Geolocation.service;

namespace XUnitTest.Geolocation
{
    public class GeolocationServiceTests
    {
        private readonly Mock<IGeolocationRepository> _mockRepository;
        private readonly GeolocationService _service;

        public GeolocationServiceTests()
        {
            _mockRepository = new Mock<IGeolocationRepository>();
            _service = new GeolocationService(_mockRepository.Object);
        }

        #region LocateIpAsync Tests

        [Fact]
        public async Task LocateIpAsync_ShouldReturnError_WhenIpAddressesIsNull()
        {
            // Arrange
            var request = new LocateIpRequest { IpAddresses = null };

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("required");
        }

        [Fact]
        public async Task LocateIpAsync_ShouldReturnError_WhenIpAddressesIsEmpty()
        {
            // Arrange
            var request = new LocateIpRequest { IpAddresses = new List<string>() };

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("required");
        }

        [Fact]
        public async Task LocateIpAsync_ShouldReturnError_WhenMoreThan10IpAddresses()
        {
            // Arrange
            var ipAddresses = Enumerable.Range(1, 11).Select(i => $"192.168.1.{i}").ToList();
            var request = new LocateIpRequest { IpAddresses = ipAddresses };

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Maximum 10");
        }

        [Fact]
        public async Task LocateIpAsync_ShouldReturnSuccess_WithValidIpAddresses()
        {
            // Arrange
            var ipAddresses = new List<string> { "8.8.8.8", "1.1.1.1" };
            var request = new LocateIpRequest { IpAddresses = ipAddresses };
            var expectedLookups = new IpLookup[]
            {
                new IpLookup { StartIp = "8.8.8.8", CountryCode = "US", CountryName = "United States" },
                new IpLookup { StartIp = "1.1.1.1", CountryCode = "AU", CountryName = "Australia" }
            };
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(ipAddresses, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedLookups);

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.IpLookups.Should().HaveCount(2);
        }

        [Fact]
        public async Task LocateIpAsync_ShouldNotDisguiseAnUnexpectedFailure_AsAnUnlocatableAddress()
        {
            // Arrange
            var ipAddresses = new List<string> { "8.8.8.8" };
            var request = new LocateIpRequest { IpAddresses = ipAddresses };
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Repository error"));

            // Act
            var act = () => _service.LocateIpAsync(request);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>(
                because: "the repository already absorbs every way the provider can fail, so "
                    + "anything still escaping is a defect in this service and has to be visible "
                    + "as one rather than reported to the caller as a bad IP address");
        }

        #endregion

        #region LocateAsync Tests

        [Fact]
        public async Task LocateAsync_ShouldReturnError_WhenIpAddressesIsNull()
        {
            // Arrange
            var request = new LocateRequest();
            IEnumerable<string>? ipAddresses = null;

            // Act
            var result = await _service.LocateAsync(request, ipAddresses!);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("No IP addresses");
        }

        [Fact]
        public async Task LocateAsync_ShouldReturnError_WhenIpAddressesIsEmpty()
        {
            // Arrange
            var request = new LocateRequest();
            var ipAddresses = Enumerable.Empty<string>();

            // Act
            var result = await _service.LocateAsync(request, ipAddresses);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("No IP addresses");
        }

        [Fact]
        public async Task LocateAsync_ShouldReturnSuccess_WithValidIpAddresses()
        {
            // Arrange
            var request = new LocateRequest();
            var ipAddresses = new[] { "203.0.113.1" };
            var expectedLookups = new IpLookup[]
            {
                new IpLookup { StartIp = "203.0.113.1", CountryCode = "JP", CountryName = "Japan" }
            };
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(ipAddresses, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedLookups);

            // Act
            var result = await _service.LocateAsync(request, ipAddresses);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.IpLookups.Should().HaveCount(1);
            result.IpLookups.First().CountryCode.Should().Be("JP");
        }

        #endregion

        #region IpLookup Tests

        [Fact]
        public void IpLookup_ShouldStoreAllProperties()
        {
            // Arrange & Act
            var lookup = new IpLookup
            {
                StartIp = "192.168.1.1",
                LastIp = "192.168.1.255",
                CountryCode = "US",
                CountryName = "United States",
                ContinentCode = "NA",
                ContinentName = "North America"
            };

            // Assert
            lookup.StartIp.Should().Be("192.168.1.1");
            lookup.LastIp.Should().Be("192.168.1.255");
            lookup.CountryCode.Should().Be("US");
            lookup.CountryName.Should().Be("United States");
            lookup.ContinentCode.Should().Be("NA");
            lookup.ContinentName.Should().Be("North America");
        }

        #endregion

        #region LocateIpRequest Tests

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public async Task LocateIpAsync_ShouldAcceptUpTo10IpAddresses(int count)
        {
            // Arrange
            var ipAddresses = Enumerable.Range(1, count).Select(i => $"192.168.1.{i}").ToList();
            var request = new LocateIpRequest { IpAddresses = ipAddresses };
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ipAddresses.Select(ip => new IpLookup { StartIp = ip }).ToArray());

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        #endregion
    }
}
