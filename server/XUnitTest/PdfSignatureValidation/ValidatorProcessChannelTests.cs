using FluentAssertions;
using Utility.DomainService.PdfSignatureValidation.Validator;

namespace XUnitTest.PdfSignatureValidation;

/// <remarks>
/// Guards how the validator's JVM is launched. The trusted-list cache directory on its command line
/// is what makes a restart after a timeout reload from disk instead of downloading everything again
/// (spec, Runtime), so a change that drops it would silently turn every restart into a two-minute
/// outage.
/// </remarks>
public sealed class ValidatorProcessChannelTests
{
    [Fact]
    public void The_jvm_is_started_with_the_cache_directory_the_lotl_signers_and_the_heap_cap()
    {
        var options = new PdfSignatureValidatorOptions
        {
            TrustedListCacheDirectory = "/var/cache/dss",
            LotlSignersPath = "/opt/dss/signers.pem",
            HeapMegabytes = 768,
            TrustedListRefreshHours = 12
        };

        var arguments = ValidatorProcessChannel.Arguments(options).ToList();

        arguments.Should().Contain("-Xmx768m", "the worker's other tools need their headroom");
        arguments.Should().ContainInConsecutiveOrder("--cache-dir", "/var/cache/dss");
        arguments.Should().ContainInConsecutiveOrder("--lotl-signers", "/opt/dss/signers.pem");
        arguments.Should().ContainInConsecutiveOrder("--refresh-hours", "12");
        arguments.Should().Contain("DssValidator", "the class the image's dss-build stage compiles");
    }

    [Fact]
    public void Extra_trust_anchors_are_passed_only_when_configured()
    {
        ValidatorProcessChannel.Arguments(new PdfSignatureValidatorOptions()).Should().NotContain("--anchors");

        var configured = ValidatorProcessChannel.Arguments(new PdfSignatureValidatorOptions { ExtraAnchorsPath = "/etc/anchors.pem" });

        configured.Should().ContainInConsecutiveOrder("--anchors", "/etc/anchors.pem");
    }

    [Fact]
    public void Dss_font_substitution_warnings_are_quietened_but_other_warnings_are_kept()
    {
        var arguments = ValidatorProcessChannel.Arguments(new PdfSignatureValidatorOptions()).ToList();

        arguments.Should().Contain("-Dorg.slf4j.simpleLogger.defaultLogLevel=warn");
        arguments.Should().Contain(
            "-Dorg.slf4j.simpleLogger.log.org.apache.pdfbox.pdmodel.font=error",
            "PDFBox logs dozens of lines per file about substituted fonts, which say nothing about the validation");
    }
}
