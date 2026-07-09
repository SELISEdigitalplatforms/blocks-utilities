using System.Net;
using System.Security.Claims;
using BlocksTemplate.Api;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Mail
{
    public class ApiRateLimitingExtensionsTests
    {
        [Fact]
        public void GetPolicy_ReturnsMailSendPolicy_ForMailSendEndpoint()
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/Mail/Send";

            var policy = ApiRateLimitingExtensions.GetPolicy(context.Request);

            Assert.Equal("mail-send-api", policy);
        }

        [Fact]
        public void GetPolicy_ReturnsGeneralPolicy_ForNonMailEndpoint()
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/Mail/GetEmailSends";

            var policy = ApiRateLimitingExtensions.GetPolicy(context.Request);

            Assert.Equal("general-api", policy);
        }

        [Fact]
        public void GetPartitionKey_UsesAuthenticatedPrincipalBeforeHeaders()
        {
            var context = new DefaultHttpContext();
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "User-A")
            ], "unit-test"));
            context.Request.Headers["x-blocks-key"] = "tenant-header";

            var partition = ApiRateLimitingExtensions.GetPartitionKey(context);

            Assert.Equal("principal", partition.KeyType);
            Assert.Equal("user-a", partition.Value);
        }

        [Fact]
        public void GetPartitionKey_UsesBlocksKeyHeaderBeforeRemoteIp()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["x-blocks-key"] = "Tenant-A";
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");

            var partition = ApiRateLimitingExtensions.GetPartitionKey(context);

            Assert.Equal("blocks-key", partition.KeyType);
            Assert.Equal("tenant-a", partition.Value);
        }

        [Fact]
        public void GetPartitionKey_FallsBackToRemoteIp()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");

            var partition = ApiRateLimitingExtensions.GetPartitionKey(context);

            Assert.Equal("ip", partition.KeyType);
            Assert.Equal("203.0.113.10", partition.Value);
        }
    }
}
