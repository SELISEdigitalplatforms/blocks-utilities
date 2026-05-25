using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Api.Controllers;
using Utility.DomainService.Geolocation.service;
using Utility.DomainService.Geolocation;

namespace XUnitTest.Geolocation
{
    public class GeolocationControllerTests
    {
        private readonly Mock<IGeolocationService> _geolocationService = new();
        private readonly GeolocationController _controller;

        public GeolocationControllerTests()
        {
            _controller = new GeolocationController(
                _geolocationService.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
        }

        [Fact]
        public async Task LocateIp_Returns_Service_Response()
        {
            var request = new LocateIpRequest();
            var response = new LocateIpResponse();

            _geolocationService.Setup(s => s.LocateIpAsync(request)).ReturnsAsync(response);

            var result = await _controller.LocateIp(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task Locate_Uses_Visitor_Ip_Addresses_And_Returns_Service_Response()
        {
            var request = new LocateRequest();
            var response = new LocateIpResponse();
            var ipAddresses = new[] { "192.168.1.10", "10.0.0.1" };

            _geolocationService.Setup(s => s.GetVisitorsIpAddresses(_controller.HttpContext)).Returns(ipAddresses);
            _geolocationService.Setup(s => s.LocateAsync(request, ipAddresses)).ReturnsAsync(response);

            var result = await _controller.Locate(request);

            Assert.Same(response, result);
        }
    }
}
