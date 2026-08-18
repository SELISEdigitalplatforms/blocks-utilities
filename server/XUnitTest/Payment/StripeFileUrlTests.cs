using FluentAssertions;
using Payment.DomainService.Providers.Stripe;

namespace XUnitTest.Payment;

/// <summary>Deciding whether a file link out of a Stripe response may be followed.</summary>
public sealed class StripeFileUrlTests
{
    [Theory]
    [InlineData("https://files.stripe.com/links/abc")]
    [InlineData("https://FILES.STRIPE.COM/links/abc")]
    [InlineData("https://api.stripe.com/v1/invoices/in_1/pdf")]
    public void Stripes_own_file_hosts_are_followed(string url) =>
        StripeFileUrl.IsStripeHosted(url).Should().BeTrue();

    [Theory]
    // The credential travels on this fetch, so a link pointing elsewhere is a request forgery
    // waiting to happen — including hosts that merely end in Stripe's domain.
    [InlineData("https://files.stripe.com.evil.test/links/abc")]
    [InlineData("https://evil.test/files.stripe.com")]
    [InlineData("http://files.stripe.com/links/abc")]
    [InlineData("https://localhost/links/abc")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/links/abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? url) =>
        StripeFileUrl.IsStripeHosted(url).Should().BeFalse();
}
