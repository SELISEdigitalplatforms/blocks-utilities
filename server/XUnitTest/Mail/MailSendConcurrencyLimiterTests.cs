using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Configuration;

namespace XUnitTest.Mail
{
    public class MailSendConcurrencyLimiterTests
    {
        [Fact]
        public async Task AcquireAsync_BlocksSecondLargeAttachmentSend_WhenLargeLimitIsOne()
        {
            var limiter = CreateLimiter(new Dictionary<string, string?>
            {
                ["MicrosoftGraphMail:LargeAttachmentMaxConcurrentSends"] = "1"
            });

            await using var firstLease = await limiter.AcquireAsync(MailCategory.LargeAttachment);

            var secondAcquireTask = limiter.AcquireAsync(MailCategory.LargeAttachment);
            await Task.Delay(50);

            Assert.False(secondAcquireTask.IsCompleted);

            await firstLease.DisposeAsync();
            await using var secondLease = await secondAcquireTask;

            Assert.True(secondAcquireTask.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task AcquireAsync_DoesNotBlockNoAttachmentMail_WhenLargeAttachmentLaneIsFull()
        {
            var limiter = CreateLimiter(new Dictionary<string, string?>
            {
                ["MicrosoftGraphMail:NoAttachmentMaxConcurrentSends"] = "1",
                ["MicrosoftGraphMail:LargeAttachmentMaxConcurrentSends"] = "1"
            });

            await using var largeAttachmentLease = await limiter.AcquireAsync(MailCategory.LargeAttachment);
            var noAttachmentAcquireTask = limiter.AcquireAsync(MailCategory.NoAttachment);

            await using var noAttachmentLease = await noAttachmentAcquireTask;

            Assert.True(noAttachmentAcquireTask.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task AcquireAsync_DoesNotBlockSmallAttachmentMail_WhenLargeAttachmentLaneIsFull()
        {
            var limiter = CreateLimiter(new Dictionary<string, string?>
            {
                ["MicrosoftGraphMail:SmallAttachmentMaxConcurrentSends"] = "1",
                ["MicrosoftGraphMail:LargeAttachmentMaxConcurrentSends"] = "1"
            });

            await using var largeAttachmentLease = await limiter.AcquireAsync(MailCategory.LargeAttachment);
            var smallAttachmentAcquireTask = limiter.AcquireAsync(MailCategory.SmallAttachment);

            await using var smallAttachmentLease = await smallAttachmentAcquireTask;

            Assert.True(smallAttachmentAcquireTask.IsCompletedSuccessfully);
        }

        private static MailSendConcurrencyLimiter CreateLimiter(Dictionary<string, string?> configurationValues)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configurationValues)
                .Build();

            return new MailSendConcurrencyLimiter(configuration);
        }
    }
}
