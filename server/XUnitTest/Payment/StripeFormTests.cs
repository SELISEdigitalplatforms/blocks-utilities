using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StripeFormTests
{
    [Fact]
    public void Scalars_are_written_as_flat_fields()
    {
        var form = new StripeForm()
            .Add("mode", "payment")
            .Add("amount", 1999L)
            .Add("quantity", 1)
            .Add("off_session", true);

        form.Fields.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["amount"] = "1999",
            ["quantity"] = "1",
            ["off_session"] = "true"
        });
    }

    [Fact]
    public void Nulls_are_omitted_because_stripe_reads_an_empty_field_as_a_clear()
    {
        var form = new StripeForm()
            .Add("customer", (string?)null)
            .Add("amount", (long?)null)
            .Add("confirm", (bool?)null);

        form.Fields.Should().BeEmpty();
    }

    [Fact]
    public void Nested_objects_use_bracketed_keys()
    {
        var form = new StripeForm()
            .AddObject("payment_intent_data", data => data
                .Add("capture_method", "manual")
                .Add("setup_future_usage", "off_session"));

        form.Fields.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["payment_intent_data[capture_method]"] = "manual",
            ["payment_intent_data[setup_future_usage]"] = "off_session"
        });
    }

    [Fact]
    public void Array_items_match_the_shape_stripe_documents()
    {
        var form = new StripeForm()
            .AddArrayItem("line_items", 0, item => item
                .Add("quantity", 1)
                .AddObject("price_data", price => price
                    .Add("currency", "usd")
                    .Add("unit_amount", 1000L)
                    .AddObject("product_data", product => product
                        .Add("name", "Gold Plan"))));

        form.Fields.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["line_items[0][quantity]"] = "1",
            ["line_items[0][price_data][currency]"] = "usd",
            ["line_items[0][price_data][unit_amount]"] = "1000",
            ["line_items[0][price_data][product_data][name]"] = "Gold Plan"
        });
    }

    [Fact]
    public void Metadata_entries_are_nested_and_skip_nulls()
    {
        var form = new StripeForm()
            .AddMetadata(new Dictionary<string, string?>
            {
                ["tenant"] = "tenant-1",
                ["payment"] = "payment-1",
                ["absent"] = null
            });

        form.Fields.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["metadata[tenant]"] = "tenant-1",
            ["metadata[payment]"] = "payment-1"
        });
    }

    [Fact]
    public void Amounts_are_written_invariantly_regardless_of_thread_culture()
    {
        var original = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            new StripeForm().Add("unit_amount", 1234567L)
                .Fields["unit_amount"].Should().Be("1234567");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void Headers_carry_bearer_auth_a_pinned_version_and_an_idempotency_key()
    {
        var headers = StripeRequestHeaders.Create(
            new PaymentProvider
            {
                ProviderName = PaymentConstants.StripeProvider,
                ApiKey = "sk_test_123"
            },
            "idem-1");

        headers["Authorization"].Should().Be("Bearer sk_test_123");
        headers[StripeConstants.VersionHeader].Should().Be(StripeConstants.ApiVersion);
        headers[StripeConstants.IdempotencyHeader].Should().Be("idem-1");
    }
}
