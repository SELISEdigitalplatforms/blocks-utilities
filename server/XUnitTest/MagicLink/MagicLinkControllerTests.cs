using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Api.Controllers;
using Microsoft.Extensions.Configuration;
using System.Net;
using Utility.DomainService.MagicLink.Service;
using Utility.DomainService.MagicLink;
using Utility.DomainService.MagicLink.Models;

namespace XUnitTest.MagicLink
{
    public class MagicLinkControllerTests
    {
        private readonly Mock<IMagicLinkService> _magicLinkService;
        private readonly Mock<IConfiguration> _configuration;
        private readonly MagicLinkController _controller;

        public MagicLinkControllerTests()
        {
            _magicLinkService = new Mock<IMagicLinkService>();
            _configuration = new Mock<IConfiguration>();

            _configuration
                .Setup(c => c["RootTenantId"])
                .Returns("root-tenant");

            _controller = new MagicLinkController(
                _magicLinkService.Object,
                _configuration.Object);

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
        }

        #region Simple pass-through endpoints

        [Fact]
        public async Task CreateLink_Returns_Service_Response()
        {
            var request = new CreateMagicLinkRequest();
            var response = new CreateMagicLinkResponse();

            _magicLinkService
                .Setup(s => s.CreateLinkAsync(request))
                .ReturnsAsync(response);

            var result = await _controller.CreateLink(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task CreateLinks_Returns_Service_Response()
        {
            var request = new CreateMagicLinksRequest();
            var response = new CreateMagicLinksResponse();

            _magicLinkService
                .Setup(s => s.CreateLinksAsync(request))
                .ReturnsAsync(response);

            var result = await _controller.CreateLinks(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task RemoveLinks_Returns_Service_Response()
        {
            var request = new RemoveMagicLinksRequest();
            var response = new RemoveMagicLinksResponse();

            _magicLinkService
                .Setup(s => s.RemoveLinksAsync(request))
                .ReturnsAsync(response);

            var result = await _controller.RemoveLinks(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task GetLink_Returns_Service_Response()
        {
            var request = new GetMagicLinkRequest();
            var response = new GetMagicLinkResponse();

            _magicLinkService
                .Setup(s => s.GetLinkAsync(request))
                .ReturnsAsync(response);

            var result = await _controller.GetLink(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task GetLinks_Returns_Service_Response()
        {
            var request = new GetMagicLinksRequest();
            var response = new GetMagicLinksResponse();

            _magicLinkService
                .Setup(s => s.GetLinksAsync(request))
                .ReturnsAsync(response);

            var result = await _controller.GetLinks(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task SaveConfig_Returns_Service_Response()
        {
            var request = new SaveLinkBasedActionConfigRequest();
            var response = new SaveLinkBasedActionConfigResponse();

            _magicLinkService
                .Setup(s => s.SaveLinkBasedActionConfigAsync(request))
                .ReturnsAsync(response);

            var result = await _controller.SaveConfig(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task GetConfig_Returns_Service_Response()
        {
            var request = new GetLinkBasedActionConfigRequest();
            var response = new GetLinkBasedActionConfigResponse();

            _magicLinkService
                .Setup(s => s.GetLinkBasedActionConfigAsync(request))
                .ReturnsAsync(response);

            var result = await _controller.GetConfig(request);

            Assert.Same(response, result);
        }

        #endregion

        #region Invoke endpoint

        [Fact]
        public async Task Invoke_RedirectType_Returns_PermanentRedirect()
        {
            var resultFromService = new InvokeMagicLinkResponse
            {
                IsSuccess = true,
                RedirectUrl = "https://example.com",
                Type = MagicLinkType.Redirect.ToString()
            };

            _magicLinkService
                .Setup(s => s.InvokeLinkAsync(It.IsAny<InvokeMagicLinkRequest>()))
                .ReturnsAsync(resultFromService);

            var result = await _controller.Invoke("link-id");

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://example.com", redirect.Url);
            Assert.True(redirect.Permanent);
        }

        [Fact]
        public async Task Invoke_ActionType_NoRedirect_Returns_HtmlContent()
        {
            var resultFromService = new InvokeMagicLinkResponse
            {
                IsSuccess = true,
                RedirectUrl = null,
                Type = MagicLinkType.Action.ToString()
            };

            _magicLinkService
                .Setup(s => s.InvokeLinkAsync(It.IsAny<InvokeMagicLinkRequest>()))
                .ReturnsAsync(resultFromService);

            var result = await _controller.Invoke("link-id");

            var content = Assert.IsType<ContentResult>(result);
            Assert.Equal("text/html", content.ContentType);
            Assert.Contains("Action queued successfully", content.Content);
        }

        [Fact]
        public async Task Invoke_LinkNotFound_Returns_404()
        {
            var response = new InvokeMagicLinkResponse
            {
                IsSuccess = false,
                ErrorCode = "LINK_NOT_FOUND",
                ErrorMessage = "Not found"
            };

            _magicLinkService
                .Setup(s => s.InvokeLinkAsync(It.IsAny<InvokeMagicLinkRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.Invoke("bad-id");

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal(404, notFound.StatusCode);
        }

        [Fact]
        public async Task Invoke_Expired_Returns_410()
        {
            var response = new InvokeMagicLinkResponse
            {
                IsSuccess = false,
                ErrorCode = "LINK_EXPIRED",
                ErrorMessage = "Expired"
            };

            _magicLinkService
                .Setup(s => s.InvokeLinkAsync(It.IsAny<InvokeMagicLinkRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.Invoke("expired-id");

            var gone = Assert.IsType<ObjectResult>(result);
            Assert.Equal(410, gone.StatusCode);
        }

        [Fact]
        public async Task Invoke_LimitExceeded_Returns_410()
        {
            var response = new InvokeMagicLinkResponse
            {
                IsSuccess = false,
                ErrorCode = "LINK_LIMIT_EXCEEDED",
                ErrorMessage = "Usage limit exceeded"
            };

            _magicLinkService
                .Setup(s => s.InvokeLinkAsync(It.IsAny<InvokeMagicLinkRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.Invoke("limit-id");

            var gone = Assert.IsType<ObjectResult>(result);
            Assert.Equal(410, gone.StatusCode);
        }

        [Fact]
        public async Task Invoke_Unknown_Error_Returns_400()
        {
            var response = new InvokeMagicLinkResponse
            {
                IsSuccess = false,
                ErrorCode = "UNKNOWN",
                ErrorMessage = "Bad request"
            };

            _magicLinkService
                .Setup(s => s.InvokeLinkAsync(It.IsAny<InvokeMagicLinkRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.Invoke("unknown-id");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Invoke_Success_NoRedirect_And_NotAction_Returns_Ok()
        {
            var response = new InvokeMagicLinkResponse
            {
                IsSuccess = true,
                RedirectUrl = null,
                Type = MagicLinkType.Redirect.ToString()
            };

            _magicLinkService
                .Setup(s => s.InvokeLinkAsync(It.IsAny<InvokeMagicLinkRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.Invoke("link-id");

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task Invoke_Builds_Request_From_Headers_And_Config()
        {
            InvokeMagicLinkRequest? capturedRequest = null;

            _controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.10.10.10");
            _controller.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 192.168.1.1";
            _controller.Request.Headers["User-Agent"] = "unit-test-agent";
            _controller.Request.Headers["Referer"] = "https://ref.example.com";
            _controller.Request.Headers["Accept-Language"] = "en-US";

            _magicLinkService
                .Setup(s => s.InvokeLinkAsync(It.IsAny<InvokeMagicLinkRequest>()))
                .Callback<InvokeMagicLinkRequest>(r => capturedRequest = r)
                .ReturnsAsync(new InvokeMagicLinkResponse
                {
                    IsSuccess = true,
                    RedirectUrl = null,
                    Type = MagicLinkType.Redirect.ToString()
                });

            await _controller.Invoke("link-id", null, "sub-1");

            Assert.NotNull(capturedRequest);
            Assert.Equal("root-tenant", capturedRequest!.ProjectKey);
            Assert.Equal("sub-1", capturedRequest.SubscriptionFilterId);
            Assert.True(capturedRequest.NotifyOnProcessEnding);
            Assert.Equal("203.0.113.7", capturedRequest.VisitorIpAddress);
            Assert.Equal("unit-test-agent", capturedRequest.VisitorUserAgent);
            Assert.Equal("https://ref.example.com", capturedRequest.VisitorOrigin);
            Assert.Equal("en-US", capturedRequest.VisitorLanguage);
        }

        [Fact]
        public async Task Invoke_Exception_Returns_500()
        {
            _magicLinkService
                .Setup(s => s.InvokeLinkAsync(It.IsAny<InvokeMagicLinkRequest>()))
                .ThrowsAsync(new Exception("boom"));

            var result = await _controller.Invoke("any");

            var error = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, error.StatusCode);
        }

        #endregion
    }
}
