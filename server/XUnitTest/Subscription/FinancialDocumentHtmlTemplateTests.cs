using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// What the PDF actually says.
/// </summary>
/// <remarks>
/// Asserted on the HTML rather than on rendered pixels, because the template is where the decisions
/// are and a headless browser in a unit test would only be testing the browser. The things worth
/// pinning are the ones a subscriber would complain about: a discount attributed to the wrong source,
/// a period stated in the wrong timezone, a settlement shown as a single number, and a name that came
/// out of a text field being treated as markup.
/// </remarks>
public sealed class FinancialDocumentHtmlTemplateTests
{
    [Fact]
    public void Each_discount_source_gets_its_own_line_with_its_own_rate()
    {
        var html = Render(document =>
        {
            document.Amounts.AutomaticDiscountMinor = 8_000;
            document.Amounts.AutomaticDiscountBasisPoints = 800;
            document.Amounts.QuantityDiscountMinor = 5_000;
            document.Amounts.QuantityDiscountBasisPoints = 500;
            document.Amounts.PromotionalDiscountMinor = 2_000;
            document.Amounts.PromotionCode = "launch20";
        });

        // Three promises to the subscriber, three lines. One combined "discount" figure cannot be
        // read back into which of them they actually got.
        html.Should().Contain("Automatic price discount (8%)");
        html.Should().Contain("Volume discount (5%)");
        html.Should().Contain("Promotional discount (launch20)");
    }

    [Fact]
    public void A_discount_source_that_gave_nothing_is_not_mentioned()
    {
        var html = Render();

        // Rendering "Volume discount CHF 0.00" invites the subscriber to ask what it means.
        html.Should().NotContain("Volume discount");
        html.Should().NotContain("Promotional discount");
    }

    [Fact]
    public void The_tax_line_says_whether_the_rate_was_added_or_already_inside_the_price()
    {
        Render(document =>
        {
            document.Amounts.TaxRateBasisPoints = 770;
            document.Amounts.TaxMode = nameof(TaxMode.Exclusive);
        }).Should().Contain("Tax (7.7%, added)");

        Render(document =>
        {
            document.Amounts.TaxRateBasisPoints = 770;
            document.Amounts.TaxMode = nameof(TaxMode.Inclusive);
        }).Should().Contain("Tax (7.7%, included)");
    }

    [Fact]
    public void Credit_is_shown_below_tax_because_it_pays_a_bill_rather_than_reducing_one()
    {
        var html = Render(document =>
        {
            document.Amounts.TaxRateBasisPoints = 770;
            document.Amounts.TaxAmountMinor = 6_930;
            document.Amounts.CreditAppliedMinor = 1_000;
        });

        html.IndexOf("Account credit applied", StringComparison.Ordinal)
            .Should().BeGreaterThan(html.IndexOf("Tax (7.7%", StringComparison.Ordinal));
    }

    [Fact]
    public void The_service_period_is_stated_in_the_subscribers_zone_and_in_utc()
    {
        var html = Render(document =>
        {
            document.Period.StartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            document.Period.EndUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            document.Period.LocalStart = "2026-01-01";
            document.Period.LocalEnd = "2027-01-01";
            document.Period.TimeZoneId = "Pacific/Auckland";
        });

        // Both, because a period ending at midnight UTC ends on a different date in Auckland, and an
        // invoice that states only one of the two is wrong to somebody.
        html.Should().Contain("Pacific/Auckland");
        html.Should().Contain("2026-01-01 00:00:00Z");
    }

    [Fact]
    public void A_settlement_is_shown_as_two_sides_rather_than_one_number()
    {
        var html = Render(document => document.Settlement = new SubscriptionSettlementBreakdown
        {
            Outgoing = new SubscriptionSettlementSide
            {
                GrossAmountMinor = 10_000,
                PeriodTotalMinor = 10_770,
                ProratedValueMinor = 3_000
            },
            Target = new SubscriptionSettlementSide
            {
                GrossAmountMinor = 30_000,
                PeriodTotalMinor = 32_310,
                ProratedValueMinor = 9_000
            },
            CreditConsumedMinor = 500,
            NetSettlementMinor = 5_500
        });

        html.Should().Contain("Previous terms");
        html.Should().Contain("New terms");
        html.Should().Contain("Unused value on previous terms");
        html.Should().Contain("Remaining value on new terms");
        html.Should().Contain("Net settlement");
    }

    [Fact]
    public void A_credit_note_names_the_invoice_it_adjusts()
    {
        var html = Render(document =>
        {
            document.DocumentType = FinancialDocumentType.CreditNote;
            document.DocumentNumber = "CRN-2026-000004";
            document.OriginalDocumentNumber = "INV-2026-000009";
        });

        // A credit note on its own is meaningless, and this is the first thing anybody reconciling it
        // looks for.
        html.Should().Contain("Credit note");
        html.Should().Contain("Adjusts invoice");
        html.Should().Contain("INV-2026-000009");
        html.Should().Contain("Total credited");
    }

    [Fact]
    public void A_trial_invoice_states_its_terms_and_that_nothing_is_due()
    {
        var html = Render(document =>
        {
            document.DocumentType = FinancialDocumentType.TrialInvoice;
            document.Amounts = new FinancialDocumentAmounts();
            document.Trial = new FinancialDocumentTrial
            {
                StartsAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                EndsAtUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
                RequiresPaymentMethod = false,
                FirstBillingAtUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc)
            };
        });

        html.Should().Contain("Trial invoice");
        html.Should().Contain("Not required");
        html.Should().Contain("First billing expected");
        html.Should().Contain("Nothing is charged for a trial period.");
        html.Should().Contain("No payment due");
    }

    [Fact]
    public void A_refunded_invoice_says_so_where_its_payment_status_goes()
    {
        Render(document => document.Status = FinancialDocumentStatus.PartiallyRefunded)
            .Should().Contain("Paid, partially refunded");
        Render(document => document.Status = FinancialDocumentStatus.Refunded)
            .Should().Contain("Paid, since refunded in full");
    }

    [Fact]
    public void Text_a_subscriber_typed_is_escaped_rather_than_rendered()
    {
        // A billing profile is a text field, and an organization legal name is the obvious place to
        // put a script tag. Every interpolated value goes through the same escape for exactly this
        // reason — a template that decides per field which to trust will eventually trust the wrong one.
        var html = Render(document =>
        {
            document.Subscriber.LegalName = "<script>alert('x')</script> & Co";
            document.Merchant.PaymentInstructions = "Pay <b>now</b>";
        });

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("&amp; Co");
        html.Should().NotContain("Pay <b>now</b>");
    }

    [Fact]
    public void Amounts_are_formatted_with_the_currency_code_beside_them()
    {
        // Never a locale symbol. "1.234,00 €" and "€1,234.00" are the same number to two different
        // readers and different numbers to a careless one.
        Render().Should().Contain("CHF 1,000.00");
    }

    [Fact]
    public void A_currency_with_no_decimal_places_is_not_given_two()
    {
        var currency = new Mock<ICurrencyMinorUnitResolver>();
        currency
            .Setup(resolver => resolver.TryConvertBack(
                It.IsAny<long>(), It.IsAny<string>(), out It.Ref<decimal>.IsAny))
            .Returns((long minor, string _, out decimal amount) =>
            {
                amount = minor;

                return true;
            });
        currency
            .Setup(resolver => resolver.TryConvert(
                It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<long>.IsAny))
            .Returns((decimal amount, string _, out long minor) =>
            {
                minor = (long)amount;

                return true;
            });

        var document = Document();
        document.CurrencyCode = "JPY";

        var html = FinancialDocumentHtmlTemplate.Render(
            document,
            new FinancialDocumentMoneyFormatter(currency.Object, "JPY"));

        // Yen has no minor unit. Printing "JPY 100000.00" would be wrong by a factor of a hundred to
        // anybody reading it as a decimal currency.
        html.Should().Contain("JPY 100,000");
        html.Should().NotContain("JPY 100,000.00");
    }

    [Fact]
    public void The_document_is_self_contained()
    {
        var html = Render();

        // A renderer that has to fetch anything is one that fails when the network does, and an
        // invoice whose look depends on what a CDN returned is not a financial record.
        html.Should().NotContain("http://");
        html.Should().NotContain("https://");
        html.Should().NotContain("<script");
        html.Should().Contain("<style>");
    }

    [Fact]
    public void A_resolved_logo_is_embedded_as_a_data_uri_instead_of_the_merchant_name()
    {
        var html = Render(
            document => document.Merchant.LegalName = "Blocks AG",
            logo: FinancialDocumentLogoResolution.Embedded("data:image/png;base64,QUJD"));

        html.Should().Contain("<img class=\"logo\"");
        html.Should().Contain("data:image/png;base64,QUJD");

        // The name still names the merchant on the document -- as the image's alt text -- but is not
        // rendered a second time as visible text beside the logo.
        html.Should().Contain("alt=\"Blocks AG\"");
    }

    [Fact]
    public void With_no_logo_the_merchant_name_is_shown_as_text()
    {
        var html = Render(logo: FinancialDocumentLogoResolution.None);

        html.Should().NotContain("<img");
        html.Should().Contain("Blocks AG");
    }

    [Fact]
    public void A_logo_warning_still_renders_the_document_from_the_merchant_name()
    {
        // The resolver's contract: a warning means "fell back", never "stop". A document must still
        // come out the other end even when its branding asset could not be read.
        var html = Render(logo: FinancialDocumentLogoResolution.Warning("document_logo_unavailable"));

        html.Should().NotContain("<img");
        html.Should().Contain("Blocks AG");
    }

    [Fact]
    public void Snapshotted_brand_colors_reach_the_stylesheet()
    {
        var html = Render(document =>
        {
            document.Merchant.PrimaryColor = "#112233";
            document.Merchant.AccentColor = "#AABBCC";
        });

        html.Should().Contain("#112233");
        html.Should().Contain("#AABBCC");
    }

    [Fact]
    public void An_unset_brand_color_falls_back_to_the_shared_default_palette()
    {
        var html = Render();

        html.Should().Contain(FinancialDocumentBrandingDefaults.PrimaryColor);
        html.Should().Contain(FinancialDocumentBrandingDefaults.AccentColor);
    }

    [Fact]
    public void A_malformed_stored_color_falls_back_to_the_shared_default_rather_than_reaching_the_css()
    {
        // Defence in depth: the validator refuses this on the way in, but the template does not
        // trust that every document it is ever handed passed through it -- an older document, or a
        // test fixture, might not have.
        var html = Render(document => document.Merchant.PrimaryColor = "not-a-color; }</style><script>");

        html.Should().NotContain("not-a-color");
        html.Should().NotContain("<script>");
        html.Should().Contain(FinancialDocumentBrandingDefaults.PrimaryColor);
    }

    [Fact]
    public void The_headline_states_the_total_and_what_became_of_it()
    {
        Render().Should().Contain("CHF 1,000.00 — Paid");

        Render(document =>
            {
                document.DocumentType = FinancialDocumentType.CreditNote;
                document.Amounts.TotalMinor = 1_000_00;
            })
            .Should().Contain("CHF 1,000.00 — Credited");
    }

    [Fact]
    public void The_footer_names_the_document_its_total_and_its_status()
    {
        Render().Should().Contain("INV-2026-000001 · CHF 1,000.00 · Paid");
    }

    private static string Render(
        Action<SubscriptionFinancialDocument>? customize = null,
        FinancialDocumentLogoResolution? logo = null)
    {
        var currency = new Mock<ICurrencyMinorUnitResolver>();
        currency
            .Setup(resolver => resolver.TryConvertBack(
                It.IsAny<long>(), It.IsAny<string>(), out It.Ref<decimal>.IsAny))
            .Returns((long minor, string _, out decimal amount) =>
            {
                amount = minor / 100m;

                return true;
            });
        currency
            .Setup(resolver => resolver.TryConvert(
                It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<long>.IsAny))
            .Returns((decimal amount, string _, out long minor) =>
            {
                minor = (long)(amount * 100);

                return true;
            });

        var document = Document();
        customize?.Invoke(document);

        return FinancialDocumentHtmlTemplate.Render(
            document,
            new FinancialDocumentMoneyFormatter(currency.Object, document.CurrencyCode),
            logo);
    }

    private static SubscriptionFinancialDocument Document() =>
        new()
        {
            DocumentNumber = "INV-2026-000001",
            DocumentType = FinancialDocumentType.Invoice,
            IssuedAtUtc = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc),
            TenantId = "tenant-1",
            OrganizationId = "org-1",
            SubscriptionId = "sub-1",
            CurrencyCode = "CHF",
            Merchant = new FinancialDocumentMerchant { LegalName = "Blocks AG" },
            Subscriber = new FinancialDocumentParty
            {
                OrganizationId = "org-1",
                LegalName = "Northwind Trading AG"
            },
            BillingContact = new FinancialDocumentPerson
            {
                Name = "Ada Byron",
                Email = "ada@northwind.example"
            },
            InitiatedBy = new FinancialDocumentPerson { Name = "System renewal" },
            Subject = new FinancialDocumentSubject
            {
                PlanCode = "pro",
                PlanName = "Pro",
                PriceId = "price-1",
                Interval = BillingInterval.Year,
                IntervalCount = 1,
                UnitAmountMinor = 100_000
            },
            Period = new FinancialDocumentPeriod
            {
                StartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LocalStart = "2026-01-01",
                LocalEnd = "2027-01-01",
                TimeZoneId = "Europe/Zurich"
            },
            Amounts = new FinancialDocumentAmounts
            {
                GrossSubtotalMinor = 100_000,
                NetSubtotalMinor = 100_000,
                TotalMinor = 100_000
            },
            Lines =
            [
                new FinancialDocumentLine
                {
                    Description = "Pro",
                    Quantity = 1,
                    UnitAmountMinor = 100_000,
                    AmountMinor = 100_000
                }
            ]
        };
}
