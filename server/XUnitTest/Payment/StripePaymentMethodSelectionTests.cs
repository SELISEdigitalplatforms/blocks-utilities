using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Which payment methods a Checkout Session offers.
/// </summary>
/// <remarks>
/// The behaviour this replaces was widely believed to be a hardcoded
/// <c>payment_method_types=["card"]</c>. It never was — nothing here has ever sent that field,
/// which is why <see cref="Nothing_is_sent_when_no_selection_is_configured"/> is the first case
/// and pins the default rather than a change to it. What actually kept TWINT and Klarna off a
/// subscription's first charge is the mandate a renewal needs, which
/// <see cref="A_saving_checkout_drops_methods_that_can_never_be_charged_again"/> covers.
/// </remarks>
public sealed class StripePaymentMethodSelectionTests
{
    private readonly StripeInitiationRequestFactory _factory = new();

    /// <summary>
    /// The default, and what every provider registered before this field existed does: name
    /// nothing and let the account's own Dashboard configuration decide. A Dashboard change
    /// reaches an ordinary payment precisely because of this.
    /// </summary>
    [Fact]
    public void Nothing_is_sent_when_no_selection_is_configured()
    {
        var form = StripeInitiationRequestFactory.ReadForm(Create());

        form.Keys.Should().NotContain(key =>
            key.StartsWith("payment_method_types", StringComparison.Ordinal));
        form.Should().NotContainKey("payment_method_configuration");
    }

    [Fact]
    public void A_configuration_id_is_sent_when_one_is_named()
    {
        var form = StripeInitiationRequestFactory.ReadForm(
            Create(provider: Provider(configurationId: "pmc_123")));

        form["payment_method_configuration"].Should().Be("pmc_123");
    }

    /// <summary>
    /// A one-off payment stores nothing, so there is no mandate to establish and every method
    /// the operator named survives — TWINT and Klarna included.
    /// </summary>
    [Fact]
    public void An_explicit_list_is_sent_whole_when_nothing_is_being_saved()
    {
        var form = StripeInitiationRequestFactory.ReadForm(
            Create(provider: Provider(methods: ["card", "twint", "paypal", "klarna"])));

        form["payment_method_types[0]"].Should().Be("card");
        form["payment_method_types[1]"].Should().Be("twint");
        form["payment_method_types[2]"].Should().Be("paypal");
        form["payment_method_types[3]"].Should().Be("klarna");
    }

    /// <summary>
    /// The heart of it. A subscription's first charge stores the method so renewals can be
    /// raised against it off-session, and TWINT and Klarna cannot be charged that way — a
    /// subscription bought with either could never renew itself. They are dropped rather than
    /// sent for Stripe to reject the whole session over, and the indexes close up behind them
    /// because Stripe requires a contiguous array.
    /// </summary>
    [Fact]
    public void A_saving_checkout_drops_methods_that_can_never_be_charged_again()
    {
        var request = Create(
            new MakePaymentRequest
            {
                Description = "A description",
                SavePaymentMethod = true
            },
            Provider(methods: ["card", "twint", "paypal", "klarna"]));

        var form = StripeInitiationRequestFactory.ReadForm(request);

        form["payment_method_types[0]"].Should().Be("card");
        form["payment_method_types[1]"].Should().Be("paypal");
        form.Should().NotContainKey("payment_method_types[2]");

        _factory.DroppedMethods.Should().BeEquivalentTo("twint", "klarna");
    }

    /// <summary>
    /// Nothing the operator named survives the narrowing. An empty array is not the same as no
    /// array — Stripe rejects the first — so the session falls back to the account default and
    /// still opens, rather than failing the checkout outright.
    /// </summary>
    [Fact]
    public void A_selection_that_narrows_to_nothing_falls_back_rather_than_failing()
    {
        var request = Create(
            new MakePaymentRequest
            {
                Description = "A description",
                SavePaymentMethod = true
            },
            Provider(methods: ["twint", "klarna"]));

        var form = StripeInitiationRequestFactory.ReadForm(request);

        form.Keys.Should().NotContain(key =>
            key.StartsWith("payment_method_types", StringComparison.Ordinal));
        _factory.DroppedMethods.Should().BeEquivalentTo("twint", "klarna");
    }

    /// <summary>Stripe rejects a session carrying both, so the explicit list wins.</summary>
    [Fact]
    public void An_explicit_list_and_a_configuration_id_are_never_sent_together()
    {
        var form = StripeInitiationRequestFactory.ReadForm(
            Create(provider: Provider(
                configurationId: "pmc_123",
                methods: ["card", "twint"])));

        form["payment_method_types[0]"].Should().Be("card");
        form.Should().NotContainKey("payment_method_configuration");
    }

    /// <summary>
    /// Stripe renders methods in the order they arrive, so the authored order is a presentation
    /// decision and is preserved. Casing and padding are not.
    /// </summary>
    [Fact]
    public void A_selection_is_normalized_without_being_reordered()
    {
        StripePaymentMethodSelection
            .Normalize(["  TWINT ", "card", "twint", "", "  ", "Card"])
            .Should().Equal("twint", "card");
    }

    [Theory]
    [InlineData("card", true)]
    [InlineData("paypal", true)]
    [InlineData("link", true)]
    [InlineData("twint", false)]
    [InlineData("klarna", false)]
    public void Only_methods_carrying_a_mandate_can_be_reused(string method, bool reusable) =>
        StripePaymentMethodSelection.CanBeReusedOffSession(method).Should().Be(reusable);

    private ProviderInitiationRequest Create(
        MakePaymentRequest? request = null,
        PaymentProvider? provider = null) =>
        _factory.Create(
            request ?? new MakePaymentRequest
            {
                Description = "A description",
                CustomerEmail = "shopper@example.com"
            },
            new PaymentExecutionContext("tenant-1", "actor-1", "organization-1"),
            new PaymentDetail
            {
                ItemId = "payment-1",
                TenantId = "tenant-1",
                CurrencyCode = "CHF"
            },
            provider ?? Provider(),
            "https://payments.example/return?state=signed",
            "payment-reference",
            "shopper-reference",
            providerPayerReference: null,
            includeStoredPaymentMethods: true,
            minorUnits: 29000);

    private static PaymentProvider Provider(
        string? configurationId = null,
        string[]? methods = null) => new()
        {
            ProviderName = PaymentConstants.StripeProvider,
            ApiBaseUrl = "https://api.stripe.com",
            MerchantId = "acct_123",
            PaymentMethodConfigurationId = configurationId,
            CheckoutPaymentMethodTypes = methods
        };
}
