using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Api.Controllers;
using Payment.DomainService.Responses;
using Utility.DomainService.Geolocation.service;
using Utility.DomainService.Geolocation;

namespace XUnitTest.Geolocation
{
    /// <summary>
    /// Guards the status code and envelope the endpoint hands back.
    /// </summary>
    /// <remarks>
    /// Every outcome used to leave here as HTTP 200 with the failure hidden in the body, so a
    /// client that checked the status code - which is what HTTP clients, gateways, retry policies
    /// and dashboards all do - saw a rejected request as a successful one.
    /// </remarks>
    public sealed class GeolocationControllerTests
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
        public async Task LocateIp_Returns_The_Lookups_In_A_Success_Envelope()
        {
            var request = new LocateIpRequest();
            var lookups = new[] { new IpLookup { StartIp = "8.8.8.8", CountryCode = "US" } };

            _geolocationService
                .Setup(service => service.LocateIpAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LocateIpResponse { IsSuccess = true, IpLookups = lookups });

            var result = await _controller.LocateIp(request, CancellationToken.None) as ObjectResult;

            result!.StatusCode.Should().Be(StatusCodes.Status200OK);

            var body = result.Value.Should().BeOfType<ApiResponse<IpLookup[]>>().Subject;

            body.Success.Should().BeTrue();
            body.Data.Should().BeEquivalentTo(lookups,
                because: "the payload is the lookups themselves - nesting the service's response "
                    + "object would state the outcome twice, once in the envelope and once inside");
            body.Error.Should().BeNull();
        }

        [Fact]
        public async Task LocateIp_Reports_A_Rejected_Request_As_400()
        {
            var request = new LocateIpRequest();

            _geolocationService
                .Setup(service => service.LocateIpAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LocateIpResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "IP addresses are required",
                    FailureKind = GeolocationFailureKind.Validation
                });

            var result = await _controller.LocateIp(request, CancellationToken.None) as ObjectResult;

            result!.StatusCode.Should().Be(StatusCodes.Status400BadRequest,
                because: "a malformed request that answers 200 is one a client's error handling, "
                    + "its gateway and its dashboards all record as a success");

            var body = result.Value.Should().BeOfType<ApiResponse<IpLookup[]>>().Subject;

            body.Success.Should().BeFalse();
            body.Error!.Code.Should().Be("geolocation_invalid_request");
            body.Error.Message.Should().Be("IP addresses are required");
        }

        [Fact]
        public async Task LocateIp_Reports_An_Address_That_Could_Not_Be_Located_As_404()
        {
            var request = new LocateIpRequest();

            _geolocationService
                .Setup(service => service.LocateIpAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LocateIpResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "IP address could not be located",
                    FailureKind = GeolocationFailureKind.NotFound
                });

            var result = await _controller.LocateIp(request, CancellationToken.None) as ObjectResult;

            result!.StatusCode.Should().Be(StatusCodes.Status404NotFound,
                because: "nothing was located, which is a different outcome from a request this "
                    + "service refused to accept and has to be distinguishable without parsing");

            var body = result.Value.Should().BeOfType<ApiResponse<IpLookup[]>>().Subject;

            body.Error!.Code.Should().Be("geolocation_not_found");
        }

        [Fact]
        public async Task LocateIp_Carries_The_Requests_Correlation_Id()
        {
            var request = new LocateIpRequest();

            _controller.HttpContext.TraceIdentifier = "trace-42";
            _geolocationService
                .Setup(service => service.LocateIpAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LocateIpResponse { IsSuccess = true, IpLookups = [] });

            var result = await _controller.LocateIp(request, CancellationToken.None) as ObjectResult;

            var body = result!.Value.Should().BeOfType<ApiResponse<IpLookup[]>>().Subject;

            body.Meta.CorrelationId.Should().Be("trace-42",
                because: "a caller reporting a failed lookup has to be able to name the request, "
                    + "or the log line for it cannot be found");
        }
    }
}
