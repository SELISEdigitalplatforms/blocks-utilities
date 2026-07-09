using Mail.DomainService.Mails;

namespace XUnitTest.Mail
{
    public class EmailSendContinuationTokenTests
    {
        [Fact]
        public void EncodeAndDecode_ReturnsOriginalCursorValues()
        {
            var createdAtUtc = new DateTime(2026, 7, 1, 10, 30, 0, DateTimeKind.Utc);
            var token = EmailSendContinuationToken.Encode(createdAtUtc, "mail-1");

            var decoded = EmailSendContinuationToken.TryDecode(token, out var decodedCreatedAtUtc, out var decodedItemId);

            Assert.True(decoded);
            Assert.Equal(createdAtUtc, decodedCreatedAtUtc);
            Assert.Equal("mail-1", decodedItemId);
        }

        [Fact]
        public void TryDecode_WhenTokenIsInvalid_ReturnsFalse()
        {
            var decoded = EmailSendContinuationToken.TryDecode("not-a-valid-token", out var createdAtUtc, out var itemId);

            Assert.False(decoded);
            Assert.Equal(default, createdAtUtc);
            Assert.Equal(string.Empty, itemId);
        }
    }
}
