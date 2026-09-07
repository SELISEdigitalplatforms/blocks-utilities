using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.Shared.DTOs;
using Utility.DomainService.Shared.Services;

namespace XUnitTest.PdfGenerator
{
    public class PdfGeneratorNotificationServiceTests
    {
        private readonly Mock<IHttpHelperServices> _httpHelper = new();
        private readonly PdfGeneratorNotificationService _service;

        public PdfGeneratorNotificationServiceTests()
        {
            var logger = new Mock<ILogger<PdfGeneratorNotificationService>>();
            var crypto = new Mock<ICryptoService>();
            var tenants = new Mock<ITenants>();
            var config = new Mock<IConfiguration>();

            config.Setup(c => c["BlocksAppNotificationReceiver"]).Returns("pdf-generator");
            config.Setup(c => c["RootTenantId"]).Returns("root");
            config.Setup(c => c["NotificationServiceUrl"]).Returns("https://notify.test");

            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            crypto.Setup(c => c.Hash(It.IsAny<string>(), It.IsAny<string>())).Returns("secret-hash");

            _httpHelper.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));

            _service = new PdfGeneratorNotificationService(
                logger.Object,
                crypto.Object,
                tenants.Object,
                config.Object,
                _httpHelper.Object);
        }

        [Fact]
        public async Task NotifyMergePdfsEvent_ShouldSend_WhenCorrelationIdProvided()
        {
            await _service.NotifyMergePdfsEvent(true, "pdf-1", "corr-1", "p1");

            _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyMergePdfsEvent_ShouldSkip_WhenCorrelationIdIsEmpty()
        {
            await _service.NotifyMergePdfsEvent(true, "pdf-1", string.Empty, "p1");

            _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NotifyCreatePdfsFromHtmlEvent_ShouldSend()
        {
            await _service.NotifyCreatePdfsFromHtmlEvent(true, "corr-2", "p1", 2, 1);

            _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyExtractTextFromPdfsEvent_ShouldSend()
        {
            await _service.NotifyExtractTextFromPdfsEvent(false, "corr-3", "p1");

            _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyOtherEvents_ShouldSend()
        {
            await _service.NotifyCreatePdfsFromHtmlUsingTEEvent(true, "corr-4", "p1");
            await _service.NotifyCreatePdfsFromHtmlUsingTEBulkEvent(true, "corr-5", "p1", 1, 0);
            await _service.NotifyFixPdfsEvent(true, "corr-6", "p1");
            await _service.NotifyStampImageToPdfEvent(true, "pdf-2", "corr-7", "p1");
            await _service.NotifyStampTextToPdfEvent(true, "pdf-3", "corr-8", "p1");
            await _service.NotifyStampIntoPdfEvent(true, "pdf-4", "corr-9", "p1");

            _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Exactly(6));
        }

        [Fact]
        public async Task NotifyConvertDocumentToPdfEvent_Sends_EvenWithoutACorrelationId()
        {
            // Unlike every other Notify* method, this one targets a user, not a push connection,
            // so there is always a valid target once BlocksContext resolves one -- the previous
            // gate on the correlation id being present no longer applies.
            SetAuthenticatedUser("user-42");
            try
            {
                await _service.NotifyConvertDocumentToPdfEvent(true, "file-1", string.Empty, "p1");

                _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()), Times.Once);
            }
            finally
            {
                BlocksContext.ClearContext();
            }
        }

        [Fact]
        public async Task NotifyConvertDocumentToPdfEvent_Skips_WhenNoAuthenticatedUser()
        {
            BlocksContext.ClearContext();

            await _service.NotifyConvertDocumentToPdfEvent(true, "file-1", "corr-1", "p1");

            _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NotifyConvertDocumentToPdfEvent_RequestBody_MatchesTheNotifierContract()
        {
            SetAuthenticatedUser("user-77");
            object? capturedRequest = null;
            _httpHelper
                .Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Callback<object, string, Dictionary<string, string>, string, string>(
                    (payload, _, _, _, _) => capturedRequest = payload)
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));

            try
            {
                await _service.NotifyConvertDocumentToPdfEvent(true, "file-9", "corr-9", "p1");
            }
            finally
            {
                BlocksContext.ClearContext();
            }

            capturedRequest.Should().NotBeNull();
            var type = capturedRequest!.GetType();

            // No connectionId at all in the request -- this notification targets the user, not a
            // push connection, and the real /api/Notifier/Notify contract makes it optional.
            type.GetProperty("ConnectionId").Should().BeNull();

            var userIds = (List<string>)type.GetProperty("UserIds")!.GetValue(capturedRequest)!;
            userIds.Should().ContainSingle().Which.Should().Be("user-77");

            type.GetProperty("ConfigurationName")!.GetValue(capturedRequest)
                .Should().Be("SignatureNotification");

            var denormalizedPayload = (string)type.GetProperty("DenormalizedPayload")!.GetValue(capturedRequest)!;
            using var payloadJson = JsonDocument.Parse(denormalizedPayload);
            payloadJson.RootElement.GetProperty("FileId").GetString().Should().Be("file-9");
            payloadJson.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
            payloadJson.RootElement.GetProperty("MessageCoRelationId").GetString().Should().Be("corr-9");
        }

        private static void SetAuthenticatedUser(string userId) =>
            BlocksContext.SetContext(BlocksContext.Create(
                "tenant-1", null, userId, true, null, "org-1",
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, null));

        [Fact]
        public async Task SendNotification_ShouldHandleExceptions_WithoutThrowing()
        {
            _httpHelper.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ThrowsAsync(new Exception("http failed"));

            await _service.NotifyMergePdfsEvent(true, "pdf-1", "corr-ex", "p1");

            _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }
    }
}
