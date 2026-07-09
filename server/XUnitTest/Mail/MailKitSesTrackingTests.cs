using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Services.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;

namespace XUnitTest.Mail;

public class MailKitSesTrackingTests
{
    [Fact]
    public async Task SendAsync_WhenSesTrackingEnabled_AddsCorrelationHeadersAndCapturesMessageId()
    {
        var adapter = new Mock<IMailKitSmtpClient>();
        MimeMessage? capturedMessage = null;
        adapter.Setup(x => x.SendAsync(It.IsAny<MimeMessage>()))
            .Callback<MimeMessage>(message => capturedMessage = message)
            .ReturnsAsync("Ok <ses-message-id>");
        var client = new TestMailKitSmtpClient(adapter.Object, CreateConfiguration(true));

        var result = await client.SendAsync(CreateMail(), new MailBody { Subject = "Subject", Body = "<p>secret body</p>" });

        Assert.True(result.IsAccepted);
        Assert.Equal("ses-message-id", result.ProviderRequestId);
        Assert.Equal("blocks-mail-delivery", capturedMessage!.Headers["X-SES-CONFIGURATION-SET"]);
        Assert.Equal("mailItemId=mail-1,tenantId=tenant-a", capturedMessage.Headers["X-SES-MESSAGE-TAGS"]);
        Assert.Null(capturedMessage.Headers["X-Mail-Body"]);
        Assert.Null(capturedMessage.Headers["X-Tenant-Id"]);
    }

    [Fact]
    public async Task SendAsync_WhenCorrelationTagIsInvalid_DoesNotSubmit()
    {
        var adapter = new Mock<IMailKitSmtpClient>();
        var client = new TestMailKitSmtpClient(adapter.Object, CreateConfiguration(true));
        var mail = CreateMail();
        mail.TenantId = "tenant with spaces";

        var result = await client.SendAsync(mail, new MailBody { Subject = "Subject", Body = "Body" });

        Assert.False(result.IsAccepted);
        Assert.Equal("InvalidAmazonSesTrackingConfiguration", result.FailureReason);
        adapter.Verify(x => x.SendAsync(It.IsAny<MimeMessage>()), Times.Never);
    }

    private static IConfiguration CreateConfiguration(bool enabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AmazonSes:DeliveryTrackingEnabled"] = enabled.ToString(),
            ["AmazonSes:ConfigurationSetName"] = "blocks-mail-delivery"
        }).Build();

    private static MailToBeSent CreateMail() => new()
    {
        ItemId = "mail-1",
        TenantId = "tenant-a",
        To = ["to@example.com"],
        Cc = [],
        Bcc = [],
        ReplyTo = [],
        MailServerConfiguration = new MailServerConfiguration
        {
            Host = "smtp.example.com",
            Port = 587,
            EnableSSL = true,
            SenderAddress = "sender@example.com",
            SenderName = "Sender",
            SenderUserName = "user",
            AccountPassword = "password"
        }
    };

    private sealed class TestMailKitSmtpClient : MailKitSmtpClient
    {
        private readonly IMailKitSmtpClient _adapter;

        public TestMailKitSmtpClient(IMailKitSmtpClient adapter, IConfiguration configuration)
            : base(NullLogger<MailKitSmtpClient>.Instance, configuration)
        {
            _adapter = adapter;
        }

        protected override IMailKitSmtpClient CreateSmtpClient() => _adapter;
    }
}
