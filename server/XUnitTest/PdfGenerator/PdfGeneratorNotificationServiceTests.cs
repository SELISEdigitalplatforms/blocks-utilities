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
        private readonly Mock<ILogger<PdfGeneratorNotificationService>> _logger = new();
        private readonly PdfGeneratorNotificationService _service;

        public PdfGeneratorNotificationServiceTests()
        {
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
                _logger.Object,
                crypto.Object,
                tenants.Object,
                config.Object,
                _httpHelper.Object);
        }

        private void VerifyLog(LogLevel level, string containing, Times times) =>
            _logger.Verify(
                x => x.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(containing)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                times);

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

        [Fact]
        public async Task NotifyConvertDocumentToPdfEvent_LogsWhoTheNotificationIsBeingSentTo()
        {
            SetAuthenticatedUser("user-55");
            try
            {
                await _service.NotifyConvertDocumentToPdfEvent(true, "file-1", "corr-1", "p1");
            }
            finally
            {
                BlocksContext.ClearContext();
            }

            VerifyLog(LogLevel.Information, "Sending notification to userId=user-55", Times.Once());
        }

        [Fact]
        public async Task NotifyConvertDocumentToPdfEvent_LogsSuccess_WithTheRecipient()
        {
            SetAuthenticatedUser("user-56");
            try
            {
                await _service.NotifyConvertDocumentToPdfEvent(true, "file-1", "corr-1", "p1");
            }
            finally
            {
                BlocksContext.ClearContext();
            }

            VerifyLog(LogLevel.Information, "Notification sent successfully to userId=user-56", Times.Once());
        }

        [Fact]
        public async Task NotifyConvertDocumentToPdfEvent_LogsError_WhenTheApiReportsFailure()
        {
            _httpHelper
                .Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ReturnsAsync((new NotificationResponse { isSuccess = false, errors = "recipient not found" }, string.Empty));

            SetAuthenticatedUser("user-57");
            try
            {
                await _service.NotifyConvertDocumentToPdfEvent(true, "file-1", "corr-1", "p1");
            }
            finally
            {
                BlocksContext.ClearContext();
            }

            VerifyLog(LogLevel.Error, "Failed to send notification to userId=user-57", Times.Once());
            VerifyLog(LogLevel.Error, "recipient not found", Times.Once());
        }

        [Fact]
        public async Task NotifyConvertDocumentToPdfEvent_LogsTheRawResponse_WhenTheHttpCallItselfFailed()
        {
            // MakeHttpPostRequest returns a null result with its own explanatory message as the
            // second tuple element when the HTTP call throws internally -- errors is null in that
            // case, so the log must fall back to the raw response rather than say nothing at all.
            _httpHelper
                .Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ReturnsAsync(((NotificationResponse?)null, "Operation Failed."));

            SetAuthenticatedUser("user-58");
            try
            {
                await _service.NotifyConvertDocumentToPdfEvent(true, "file-1", "corr-1", "p1");
            }
            finally
            {
                BlocksContext.ClearContext();
            }

            VerifyLog(LogLevel.Error, "Operation Failed.", Times.Once());
        }

        [Fact]
        public async Task NotifyConvertDocumentToPdfEvent_LogsError_WhenSendingThrows()
        {
            _httpHelper
                .Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("network unreachable"));

            SetAuthenticatedUser("user-59");
            try
            {
                await _service.NotifyConvertDocumentToPdfEvent(true, "file-1", "corr-1", "p1");
            }
            finally
            {
                BlocksContext.ClearContext();
            }

            VerifyLog(LogLevel.Error, "Error sending notification to userId=user-59", Times.Once());
        }

        [Fact]
        public async Task NotifyConvertDocumentToPdfEvent_LogsWarning_WhenThereIsNoUserToNotify()
        {
            BlocksContext.ClearContext();

            await _service.NotifyConvertDocumentToPdfEvent(true, "file-1", "corr-1", "p1");

            VerifyLog(LogLevel.Warning, "No authenticated user in context", Times.Once());
        }

        private static void SetAuthenticatedUser(string userId) =>
            BlocksContext.SetContext(BlocksContext.Create(
                "tenant-1", null, userId, true, null, "org-1",
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, null));

        [Fact]
        public async Task NotifyIngestPdfEvent_Sends_ToTheUserSuppliedExplicitly_EvenWithNoAmbientContext()
        {
            // The whole point of the explicit-userId overload: it must not depend on BlocksContext
            // being populated correctly by the time a queued message is consumed.
            BlocksContext.ClearContext();

            await _service.NotifyIngestPdfEvent(true, "file-1", "corr-1", "user-99", "p1");

            _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.Is<object>(payload => JsonSerializer.Serialize(payload).Contains("user-99")),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyIngestPdfEvent_Skips_WhenTheSuppliedUserIdIsNull()
        {
            BlocksContext.ClearContext();

            await _service.NotifyIngestPdfEvent(true, "file-1", "corr-1", null, "p1");

            _httpHelper.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

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
