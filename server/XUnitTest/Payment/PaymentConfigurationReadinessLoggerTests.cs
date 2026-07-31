using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentConfigurationReadinessLoggerTests
{
    /// <summary>
    /// This runs during host startup, so throwing here would take the whole service down —
    /// a worse outcome than the misconfiguration it exists to report.
    /// </summary>
    [Fact]
    public async Task Starts_cleanly_when_configuration_is_missing()
    {
        var act = () => Logger(new PaymentOptions
            {
                PublicBaseUrl = string.Empty,
                CurrencyMinorUnits = []
            })
            .StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Starts_cleanly_when_configuration_is_complete()
    {
        var act = () => Logger(new PaymentOptions
            {
                PublicBaseUrl = "https://payments.example",
                CurrencyMinorUnits = { ["CHF"] = 2 }
            })
            .StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Stops_cleanly() =>
        await Logger(new PaymentOptions()).StopAsync(CancellationToken.None);

    private static PaymentConfigurationReadinessLogger Logger(PaymentOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(item => item.CurrentValue).Returns(options);

        return new PaymentConfigurationReadinessLogger(
            monitor.Object,
            NullLogger<PaymentConfigurationReadinessLogger>.Instance);
    }
}
