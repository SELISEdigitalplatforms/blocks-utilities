using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class AsposeLicenseTests
    {
        [Fact]
        public void EnsureApplied_WithNoLicenceAvailable_DoesNotThrow()
        {
            // CI has no licence file and no Aspose:* configuration. A missing licence must degrade
            // to watermarked output rather than taking the process down, otherwise a licence that
            // expires overnight stops the worker instead of just marking its output.
            var act = () => AsposeLicense.EnsureApplied(new ConfigurationBuilder().Build(), NullLogger.Instance);

            act.Should().NotThrow();
        }

        [Fact]
        public void EnsureApplied_WithGarbageBase64_DoesNotThrow()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Aspose:LicenseBase64"] = "this-is-not-base64!!"
                })
                .Build();

            var act = () => AsposeLicense.EnsureApplied(configuration, NullLogger.Instance);

            act.Should().NotThrow();
        }

        [Fact]
        public void EnsureApplied_IsIdempotent()
        {
            var configuration = new ConfigurationBuilder().Build();

            AsposeLicense.EnsureApplied(configuration, NullLogger.Instance);
            var afterFirst = AsposeLicense.IsLicensed;

            AsposeLicense.EnsureApplied(configuration, NullLogger.Instance);

            // Every conversion calls this, so repeated calls must be free and must not flip the
            // result — Aspose's own licence state is process-global and applying twice is wasteful.
            AsposeLicense.IsLicensed.Should().Be(afterFirst);
        }
    }
}
