using FluentAssertions;
using Payment.DomainService.Providers.Stripe;

namespace XUnitTest.Payment;

/// <summary>Deciding whether a file link out of a Stripe response may be followed.</summary>
public sealed class StripeFileUrlTests
{
    [Theory]
    // The shape Stripe actually serves an invoice PDF from. This is the case the first version
    // refused: it allowed files.stripe.com, which is where File objects live, and every real
    // invoice download failed the check while a test using a fabricated files.stripe.com URL
    // reported the guard working.
    [InlineData("https://pay.stripe.com/invoice/acct_1Ty96e/test_YWNjd/pdf?s=ap")]
    [InlineData("https://invoice.stripe.com/i/acct_1Ty96e/test_YWNjd/pdf")]
    [InlineData("https://files.stripe.com/links/abc")]
    [InlineData("https://FILES.STRIPE.COM/links/abc")]
    [InlineData("https://api.stripe.com/v1/invoices/in_1/pdf")]
    [InlineData("https://stripe.com/anything")]
    public void Stripes_own_hosts_are_followed(string url) =>
        StripeFileUrl.IsStripeHosted(url).Should().BeTrue();

    [Theory]
    // The credential travels on this fetch, so a link pointing elsewhere is a request forgery
    // waiting to happen — including hosts that merely end in Stripe's domain.
    [InlineData("https://files.stripe.com.evil.test/links/abc")]
    [InlineData("https://notstripe.com/links/abc")]
    [InlineData("https://evil.test/files.stripe.com")]
    [InlineData("http://files.stripe.com/links/abc")]
    [InlineData("http://pay.stripe.com/invoice/abc/pdf")]
    [InlineData("https://localhost/links/abc")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/links/abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? url) =>
        StripeFileUrl.IsStripeHosted(url).Should().BeFalse();
}
