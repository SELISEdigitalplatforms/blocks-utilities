using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// A <see cref="PaymentOptions"/> monitor for tests that need one but are not about
/// configuration.
/// </summary>
/// <remarks>
/// Defaults deliberately, so a test asserting the console's behaviour is asserting the
/// behaviour the product ships with rather than one a test set up for itself.
/// </remarks>
internal static class TestPaymentOptions
{
    /// <summary>The console's organization as configured out of the box.</summary>
    public const string ConsoleOrganizationId = "default";

    public static IOptionsMonitor<PaymentOptions> Monitor(
        PaymentOptions? options = null)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();

        monitor.Setup(x => x.CurrentValue).Returns(options ?? new PaymentOptions());

        return monitor.Object;
    }
}
