using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator;

/// <summary>
/// Caller HTML rendered by a real Chromium: a script in it must not run. Needs a local Chromium,
/// named by PDF_TEST_CHROMIUM_PATH; skipped without it.
/// </summary>
public sealed class PdfRendererScriptTests
{
    [ChromiumFact]
    public async Task An_endless_script_loop_does_not_block_the_render()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PuppeteerSharp:ExecutablePath"] = ChromiumFactAttribute.ExecutablePath,
                ["PuppeteerSharp:RenderTimeoutSeconds"] = "20"
            })
            .Build();
        await using var engine = new PuppeteerSharpEngine(Mock.Of<ILogger<PuppeteerSharpEngine>>(), configuration);

        using var pdf = await engine.ConvertHtmlToPdfAsync(
            "<html><body><p>Invoice</p><script>while (true) {}</script></body></html>",
            new PdfGenerationOptions());

        pdf.Should().NotBeNull("with scripts on, the loop would hold the page until the time limit and the render would fail");
    }

    private sealed class ChromiumFactAttribute : FactAttribute
    {
        public static string? ExecutablePath => Environment.GetEnvironmentVariable("PDF_TEST_CHROMIUM_PATH");

        public ChromiumFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(ExecutablePath))
            {
                Skip = "Set PDF_TEST_CHROMIUM_PATH to a local Chromium to run this test.";
            }
        }
    }
}
