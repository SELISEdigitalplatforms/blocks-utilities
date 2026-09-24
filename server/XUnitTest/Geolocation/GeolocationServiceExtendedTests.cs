using FluentAssertions;
using Moq;
using Utility.DomainService.Geolocation;
using Utility.DomainService.Geolocation.service;

namespace XUnitTest.Geolocation
{
    public class GeolocationServiceExtendedTests
    {
        private readonly Mock<IGeolocationRepository> _mockRepository;
        private readonly GeolocationService _service;

        public GeolocationServiceExtendedTests()
        {
            _mockRepository = new Mock<IGeolocationRepository>();
            _service = new GeolocationService(_mockRepository.Object);
        }

        #region LocateIpAsync Edge Cases

        [Fact]
        public async Task LocateIpAsync_ShouldReportFailure_WhenNothingCouldBeResolved()
        {
            // Arrange
            var request = new LocateIpRequest { IpAddresses = new List<string> { "8.8.8.8" } };
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<IpLookup>());

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse(
                because: "an empty success cannot be told apart from a provider outage by the "
                    + "caller, and the audit records that consume this need that distinction");
            result.ErrorMessage.Should().Contain("could not be located");
        }

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(9)]
        [InlineData(10)]
        public async Task LocateIpAsync_ShouldAcceptVaryingCounts_UpTo10(int count)
        {
            // Arrange
            var ipAddresses = Enumerable.Range(1, count).Select(i => $"8.8.8.{i}").ToList();
            var request = new LocateIpRequest { IpAddresses = ipAddresses };
            var mockLookups = ipAddresses.Select(ip => new IpLookup { StartIp = ip }).ToArray();
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(ipAddresses, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockLookups);

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.IpLookups.Should().HaveCount(count);
        }

        #endregion

        #region IpLookup Extended Tests

        [Fact]
        public void IpLookup_ShouldStoreGeoCoordinates()
        {
            // Arrange & Act
            var lookup = new IpLookup
            {
                Latitude = 37.7749,
                Longitude = -122.4194
            };

            // Assert
            lookup.Latitude.Should().BeApproximately(37.7749, 0.0001);
            lookup.Longitude.Should().BeApproximately(-122.4194, 0.0001);
        }

        [Fact]
        public void IpLookup_ShouldStoreIpNumberConversions()
        {
            // Arrange & Act
            var lookup = new IpLookup
            {
                StartIpNumber = 3232235521, // 192.168.0.1
                LastIpNumber = 3232235775   // 192.168.0.255
            };

            // Assert
            lookup.StartIpNumber.Should().Be(3232235521);
            lookup.LastIpNumber.Should().Be(3232235775);
        }

        [Fact]
        public void IpLookup_ShouldStoreLocationDetails()
        {
            // Arrange & Act
            var lookup = new IpLookup
            {
                City = "San Francisco",
                Region = "California",
                LocationCode = "US-CA",
                LocationCodeAsRegistered = "US-CA-SF"
            };

            // Assert
            lookup.City.Should().Be("San Francisco");
            lookup.Region.Should().Be("California");
            lookup.LocationCode.Should().Be("US-CA");
            lookup.LocationCodeAsRegistered.Should().Be("US-CA-SF");
        }

        [Fact]
        public void IpLookup_ShouldStoreIspAndFlagUrls()
        {
            // Arrange & Act
            var lookup = new IpLookup
            {
                IspName = "Google LLC",
                CountryFlagSvgUrl = "https://flags.example.com/us.svg",
                CountryFlagPngUrl = "https://flags.example.com/us.png"
            };

            // Assert
            lookup.IspName.Should().Be("Google LLC");
            lookup.CountryFlagSvgUrl.Should().Be("https://flags.example.com/us.svg");
            lookup.CountryFlagPngUrl.Should().Be("https://flags.example.com/us.png");
        }

        #endregion

        #region LocateIpRequest Tests

        [Fact]
        public void LocateIpRequest_ShouldStoreIpAddressList()
        {
            // Arrange & Act
            var request = new LocateIpRequest
            {
                IpAddresses = new List<string> { "8.8.8.8", "1.1.1.1" }
            };

            // Assert
            request.IpAddresses.Should().HaveCount(2);
        }

        #endregion

    }
}
