using FluentAssertions;
using Utility.DomainService.PdfGenerator.Tooling.VeraPdf;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Parses a real report captured by actually running veraPDF's own CLI Docker image
/// (ghcr.io/verapdf/cli:latest) against a genuine PdfSharp-produced PDF - not a hand-written sample
/// guessing at the schema. That real report is what first showed <c>&lt;check&gt;</c> nested inside
/// <c>&lt;rule&gt;</c>, both carrying <c>status="failed"</c>, which the parser originally
/// double-counted and then collapsed into one generic string per the earlier priority order - the
/// exact bug these tests now guard against regressing.
/// </remarks>
public sealed class VeraPdfXmlParserTests
{
    private readonly VeraPdfXmlParser _parser = new();

    // Captured verbatim (stdout only - stderr, where veraPDF's own Java logging goes, was
    // redirected separately, matching how ProcessRunner reads them) from:
    //   docker run --rm --network none -v <dir>:/work ghcr.io/verapdf/cli:latest --format xml /work/plain.pdf
    // against a one-page PDF written by PdfSharp with no PDF/A metadata at all.
    private const string RealNonCompliantReport = """
        <?xml version="1.0" encoding="utf-8"?>
        <report>
          <buildInformation>
            <releaseDetails id="core" version="1.31.37" buildDate="2026-08-26T09:26:00Z"></releaseDetails>
            <releaseDetails id="validation-model" version="1.31.169" buildDate="2026-08-31T10:21:00Z"></releaseDetails>
            <releaseDetails id="apps" version="1.31.167" buildDate="2026-08-31T10:27:00Z"></releaseDetails>
          </buildInformation>
          <jobs>
            <job>
              <item size="2146">
                <name>/work/plain.pdf</name>
              </item>
              <validationReport jobEndStatus="normal" profileName="PDF/A-1b validation profile" statement="PDF file is not compliant with Validation Profile requirements." isCompliant="false">
                <details passedRules="126" failedRules="3" passedChecks="143" failedChecks="3">
                  <rule specification="ISO 19005-1:2005" clause="6.2.3.3" testNumber="1" status="failed" failedChecks="1">
                    <description>DeviceRGB may be used only if the file has a PDF/A-1 OutputIntent that uses an RGB colour space</description>
                    <object>PDDeviceRGB</object>
                    <test>gOutputCS != null &amp;&amp; gOutputCS == "RGB "</test>
                    <check status="failed">
                      <context>root/document[0]/pages[0](4 0 obj PDPage)/Group[0]/colorSpace[0]</context>
                      <errorMessage>DeviceRGB colour space is used without RGB output intent profile</errorMessage>
                    </check>
                  </rule>
                  <rule specification="ISO 19005-1:2005" clause="6.7.11" testNumber="1" status="failed" failedChecks="1">
                    <description>The PDF/A version and conformance level of a file shall be specified using the PDF/A Identification extension schema</description>
                    <object>MainXMPPackage</object>
                    <test>containsPDFAIdentification == true</test>
                    <check status="failed">
                      <context>root/document[0]/metadata[0](5 0 obj PDMetadata)/XMPPackage[0]</context>
                      <errorMessage>The document metadata stream doesn't contain PDF/A Identification Schema</errorMessage>
                    </check>
                  </rule>
                  <rule specification="ISO 19005-1:2005" clause="6.4" testNumber="3" status="failed" failedChecks="1">
                    <description>A Group object with an S key with a value of Transparency shall not be included in a form XObject. A Group object with an S key with a value of Transparency shall not be included in a page dictionary</description>
                    <object>PDGroup</object>
                    <test>S != "Transparency"</test>
                    <check status="failed">
                      <context>root/document[0]/pages[0](4 0 obj PDPage)/Group[0]</context>
                      <errorMessage>A transparency group is present in a form XObject or page dictionary</errorMessage>
                    </check>
                  </rule>
                </details>
              </validationReport>
              <duration start="1788868145754" finish="1788868156951">00:00:11.197</duration>
            </job>
          </jobs>
          <batchSummary totalJobs="1" failedToParse="0" encrypted="0" outOfMemory="0" veraExceptions="0">
            <validationReports compliant="0" nonCompliant="1" failedJobs="0">1</validationReports>
            <featureReports failedJobs="0">0</featureReports>
            <repairReports failedJobs="0">0</repairReports>
            <duration start="1788868145640" finish="1788868157093">00:00:11.453</duration>
          </batchSummary>
        </report>
        """;

    [Fact]
    public void The_real_report_parses_as_not_compliant_with_the_full_profile_name()
    {
        var result = _parser.Parse(RealNonCompliantReport, processSuccess: true, processError: null);

        result.Success.Should().BeTrue();
        result.IsCompliant.Should().BeFalse();
        result.ProfileName.Should().Be("PDF/A-1b validation profile");
    }

    [Fact]
    public void Each_of_the_three_distinct_rule_failures_is_reported_once_not_collapsed_or_duplicated()
    {
        var result = _parser.Parse(RealNonCompliantReport, processSuccess: true, processError: null);

        // Three failedRules in the report. The nested <check status="failed"> under each <rule>
        // must not add three more, and "ISO 19005-1:2005" (the specification every rule shares)
        // must not be the string that survives deduplication for all three.
        result.Errors.Should().HaveCount(3);
        result.Errors.Should().OnlyHaveUniqueItems();
        result.Errors.Should().Contain(e => e.Contains("6.2.3.3") && e.Contains("DeviceRGB"));
        result.Errors.Should().Contain(e => e.Contains("6.7.11") && e.Contains("PDF/A Identification"));
        result.Errors.Should().Contain(e => e.Contains("6.4") && e.Contains("Transparency"));
    }
}
