using FluentAssertions;
using Moq;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.ProcessExecution;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Found live: with Ghostscript not installed locally, a real ingestion run against a
/// non-compliant PDF/A file threw System.ComponentModel.Win32Exception ("cannot find the file
/// specified") out of Process.Start(), past every tool wrapper's own graceful-failure handling,
/// all the way to PdfIngestionPipeline's outer catch - which discards everything the pipeline had
/// already legitimately determined (the file was perfectly readable) and reports the verdict as
/// completely unreadable with zero pages instead. This pins the fix: a process that cannot even
/// start must come back as an ordinary failed ProcessResult, the same as a process that starts and
/// exits non-zero, not as an exception - the same fix a Docker daemon hiccup or a bad working
/// directory needs, not just a missing binary.
/// </remarks>
public sealed class ProcessRunnerTests
{
    private static IExternalProcessConcurrencyLimiter UnlimitedConcurrency()
    {
        var limiter = new Mock<IExternalProcessConcurrencyLimiter>();
        limiter.Setup(x => x.AcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IDisposable>());
        return limiter.Object;
    }

    [Fact]
    public async Task A_missing_executable_is_reported_as_a_failed_result_not_thrown()
    {
        var runner = new ProcessRunner(UnlimitedConcurrency());
        var command = new ProcessCommand
        {
            FileName = @"C:\this\path\definitely\does\not\exist\nonexistent-tool.exe",
            Arguments = ["--version"]
        };

        var result = await runner.RunAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.TimedOut.Should().BeFalse(because: "this is a start failure, not a timeout");
        result.StandardError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_invalid_working_directory_is_reported_as_a_failed_result_not_thrown()
    {
        var runner = new ProcessRunner(UnlimitedConcurrency());
        var command = new ProcessCommand
        {
            FileName = "cmd",
            Arguments = ["/c", "echo", "hi"],
            WorkingDirectory = @"C:\this\directory\definitely\does\not\exist\at\all"
        };

        var act = async () => await runner.RunAsync(command, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
