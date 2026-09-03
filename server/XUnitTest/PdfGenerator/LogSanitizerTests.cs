using FluentAssertions;
using Utility.DomainService.Shared.Utilities;

namespace XUnitTest.PdfGenerator
{
    public class LogSanitizerTests
    {
        [Theory]
        [InlineData("req-0001")]
        [InlineData("9f8c1b2e-4d3a-4a1e-8b7c-0f2a5d6e7c8b")]
        [InlineData("Employment Contract.docx")]
        public void Scrub_OrdinaryValues_PassThroughUnchanged(string value)
        {
            LogSanitizer.Scrub(value).Should().Be(value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Scrub_NullOrEmpty_ReturnsEmpty(string? value)
        {
            LogSanitizer.Scrub(value).Should().BeEmpty();
        }

        [Fact]
        public void Scrub_ForgedLogEntry_CollapsesToASingleLine()
        {
            // The attack this exists for: a correlation ID that closes the real entry and opens a
            // fabricated error line of the attacker's choosing.
            const string Forged = "req-1\r\n[ERROR] Payment gateway compromised, contact admin";

            var scrubbed = LogSanitizer.Scrub(Forged);

            scrubbed.Should().NotContain("\r").And.NotContain("\n");
            scrubbed.Should().Be("req-1[ERROR] Payment gateway compromised, contact admin");
        }

        [Theory]
        [InlineData("a\nb")]
        [InlineData("a\rb")]
        [InlineData("a\r\nb")]
        public void Scrub_RemovesEveryNewlineForm(string value)
        {
            LogSanitizer.Scrub(value).Should().Be("ab");
        }

        [Fact]
        public void Scrub_RemovesTerminalEscapeSequencesAndNulls()
        {
            // A log read in a terminal will act on an embedded escape sequence, so control
            // characters go as well as newlines.
            var scrubbed = LogSanitizer.Scrub("req-1\u001b[31mred\u0000");

            scrubbed.Should().Be("req-1[31mred");
        }

        [Fact]
        public void Scrub_OverlongValue_IsTruncated()
        {
            var scrubbed = LogSanitizer.Scrub(new string('x', 5000));

            scrubbed.Should().HaveLength(200 + "...[truncated]".Length);
            scrubbed.Should().EndWith("...[truncated]");
        }

        [Fact]
        public void Scrub_ValueAtTheLimit_IsNotTruncated()
        {
            var atLimit = new string('x', 200);

            LogSanitizer.Scrub(atLimit).Should().Be(atLimit);
        }
    }
}
