using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Microsoft.Graph.Models;

namespace XUnitTest.Mail
{
    public class MicrosoftGraphServiceClientTests
    {
        [Fact]
        public void BuildMessage_MapsHtmlBodySubjectAndRecipients()
        {
            var mail = new MailToBeSent
            {
                ItemId = "mail-1",
                To = ["to@example.com"],
                Cc = ["cc@example.com"],
                Bcc = ["bcc@example.com"],
                ReplyTo = ["reply@example.com"]
            };
            var body = new MailBody
            {
                Subject = "Subject",
                Body = "<strong>Hello</strong>"
            };

            var message = MicrosoftGraphServiceClient.BuildMessage(mail, body);

            Assert.Equal("Subject", message.Subject);
            Assert.Equal(BodyType.Html, message.Body?.ContentType);
            Assert.Equal("<strong>Hello</strong>", message.Body?.Content);
            Assert.Single(message.ToRecipients!);
            Assert.Single(message.CcRecipients!);
            Assert.Single(message.BccRecipients!);
            Assert.Single(message.ReplyTo!);
            Assert.Contains(message.InternetMessageHeaders!, header =>
                header.Name == "x-blocks-mail-item-id" &&
                header.Value == "mail-1");
        }

        [Fact]
        public void GetRecipientAddresses_IgnoresNullEmptyAndWhitespaceValues()
        {
            var recipients = MicrosoftGraphServiceClient.GetRecipientAddresses([
                "first@example.com",
                "",
                "   ",
                "second@example.com"
            ]);

            Assert.Equal(2, recipients.Count);
            Assert.Equal("first@example.com", recipients[0].EmailAddress?.Address);
            Assert.Equal("second@example.com", recipients[1].EmailAddress?.Address);
        }

        [Fact]
        public void GetRecipientAddresses_ReturnsEmptyList_WhenInputIsNull()
        {
            var recipients = MicrosoftGraphServiceClient.GetRecipientAddresses(null);

            Assert.Empty(recipients);
        }
    }
}
