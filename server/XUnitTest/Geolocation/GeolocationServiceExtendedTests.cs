using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Net;
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

        #region GetVisitorsIpAddresses Tests

        [Fact]
        public void GetVisitorsIpAddresses_ShouldReturnRemoteIpAddress_WhenNoForwardedHeader()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.1");

            // Act
            var result = _service.GetVisitorsIpAddresses(httpContext);

            // Assert
            result.Should().HaveCount(1);
            result.First().Should().Be("192.168.1.1");
        }

        [Fact]
        public void GetVisitorsIpAddresses_ShouldReturnForwardedIp_WhenHeaderExists()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Forwarded-For"] = "10.0.0.1";
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.1");

            // Act
            var result = _service.GetVisitorsIpAddresses(httpContext);

            // Assert
            result.Should().HaveCount(1);
            result.First().Should().Be("10.0.0.1");
        }

        [Fact]
        public void GetVisitorsIpAddresses_ShouldReturnMultipleIps_WhenCommaDelimited()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 10.0.0.2, 10.0.0.3";

            // Act
            var result = _service.GetVisitorsIpAddresses(httpContext);

            // Assert
            result.Should().HaveCount(3);
            result.Should().Contain("10.0.0.1");
            result.Should().Contain("10.0.0.2");
            result.Should().Contain("10.0.0.3");
        }

        [Fact]
        public void GetVisitorsIpAddresses_ShouldTrimWhitespace()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Forwarded-For"] = "  10.0.0.1  ,  10.0.0.2  ";

            // Act
            var result = _service.GetVisitorsIpAddresses(httpContext);

            // Assert
            result.Should().HaveCount(2);
            result.First().Should().Be("10.0.0.1");
            result.Last().Should().Be("10.0.0.2");
        }

        [Fact]
        public void GetVisitorsIpAddresses_ShouldRemoveEmptyEntries()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Forwarded-For"] = "10.0.0.1,,10.0.0.2";

            // Act
            var result = _service.GetVisitorsIpAddresses(httpContext);

            // Assert
            result.Should().HaveCount(2);
        }

        [Fact]
        public void GetVisitorsIpAddresses_ShouldReturnEmpty_WhenNoIpAvailable()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            // No X-Forwarded-For and no RemoteIpAddress

            // Act
            var result = _service.GetVisitorsIpAddresses(httpContext);

            // Assert
            // When there's no IP, the split with RemoveEmptyEntries returns empty enumerable
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetVisitorsIpAddresses_ShouldPreferForwardedHeader_OverRemoteIp()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.195";
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.1");

            // Act
            var result = _service.GetVisitorsIpAddresses(httpContext);

            // Assert
            result.Should().HaveCount(1);
            result.First().Should().Be("203.0.113.195");
            result.First().Should().NotBe("192.168.1.1");
        }

        [Fact]
        public void GetVisitorsIpAddresses_ShouldHandleIpv6Address()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.IPv6Loopback;

            // Act
            var result = _service.GetVisitorsIpAddresses(httpContext);

            // Assert
            result.Should().HaveCount(1);
            result.First().Should().Be("::1");
        }

        [Fact]
        public void GetVisitorsIpAddresses_ShouldHandleMultipleIpv6Addresses()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Forwarded-For"] = "2001:db8::1, 2001:db8::2";

            // Act
            var result = _service.GetVisitorsIpAddresses(httpContext);

            // Assert
            result.Should().HaveCount(2);
        }

        #endregion

        #region LocateIpAsync Edge Cases

        [Fact]
        public async Task LocateIpAsync_ShouldReturnEmpty_WhenRepositoryReturnsNull()
        {
            // Arrange
            var request = new LocateIpRequest { IpAddresses = new List<string> { "8.8.8.8" } };
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(It.IsAny<IEnumerable<string>>(), false))
                .ReturnsAsync((IpLookup[]?)null!);

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.IpLookups.Should().BeNull();
        }

        [Fact]
        public async Task LocateIpAsync_ShouldReturnEmpty_WhenRepositoryReturnsEmptyArray()
        {
            // Arrange
            var request = new LocateIpRequest { IpAddresses = new List<string> { "8.8.8.8" } };
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(It.IsAny<IEnumerable<string>>(), false))
                .ReturnsAsync(Array.Empty<IpLookup>());

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.IpLookups.Should().BeEmpty();
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
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(ipAddresses, false))
                .ReturnsAsync(mockLookups);

            // Act
            var result = await _service.LocateIpAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.IpLookups.Should().HaveCount(count);
        }

        #endregion

        #region LocateAsync Edge Cases

        [Fact]
        public async Task LocateAsync_ShouldHandleException_FromRepository()
        {
            // Arrange
            var request = new LocateRequest { UseCustomProvider = false };
            var ipAddresses = new[] { "8.8.8.8" };
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(ipAddresses, false))
                .ThrowsAsync(new Exception("Database connection failed"));

            // Act
            var result = await _service.LocateAsync(request, ipAddresses);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Failed to locate");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task LocateAsync_ShouldPassCustomProviderFlag(bool useCustomProvider)
        {
            // Arrange
            var request = new LocateRequest { UseCustomProvider = useCustomProvider };
            var ipAddresses = new[] { "8.8.8.8" };
            _mockRepository.Setup(r => r.ResolveMultipleIpsToCountryAsync(ipAddresses, useCustomProvider))
                .ReturnsAsync(new IpLookup[] { new IpLookup() });

            // Act
            await _service.LocateAsync(request, ipAddresses);

            // Assert
            _mockRepository.Verify(r => r.ResolveMultipleIpsToCountryAsync(ipAddresses, useCustomProvider), Times.Once);
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
                IpAddresses = new List<string> { "8.8.8.8", "1.1.1.1" },
                UseCustomProvider = true
            };

            // Assert
            request.IpAddresses.Should().HaveCount(2);
            request.UseCustomProvider.Should().BeTrue();
        }

        #endregion

        #region LocateRequest Tests

        [Fact]
        public void LocateRequest_ShouldHaveDefaultUseCustomProviderFalse()
        {
            // Arrange & Act
            var request = new LocateRequest();

            // Assert
            request.UseCustomProvider.Should().BeFalse();
        }

        #endregion
    }
}
