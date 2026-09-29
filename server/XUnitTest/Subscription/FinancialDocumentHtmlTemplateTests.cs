using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
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
/// a period stated in the wrong timezone or ending a day late, a settlement shown as a single number,
/// a layout that no longer matches the reference invoice, and a name that came out of a text field
/// being treated as markup.
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
        // read back into which of them they actually got. The rate sits in the Discount column, as
        // the reference prints "20%" beside its introductory discount.
        html.Should().Contain("<td>Automatic price discount</td>");
        html.Should().Contain("<td class=\"num\">8%</td>");
        html.Should().Contain("<td>Volume discount</td>");
        html.Should().Contain("<td class=\"num\">5%</td>");
        html.Should().Contain("Promotional discount (launch20)");
        html.Should().Contain(
            "<td class=\"num\">-80.00</td>",
            "each deduction is its row's Value, so the column adds up to the net amount");
    }

    [Fact]
    public void A_discount_source_that_gave_nothing_is_not_mentioned()
    {
        var html = Render();

        // Rendering "Volume discount 0.00" invites the subscriber to ask what it means.
        html.Should().NotContain("Volume discount");
        html.Should().NotContain("Promotional discount");
    }

    [Fact]
    public void A_discount_is_a_row_in_the_line_table_rather_than_only_a_total()
    {
        var html = Render(document =>
        {
            document.Amounts.AutomaticDiscountMinor = 8_000;
            document.Amounts.AutomaticDiscountBasisPoints = 800;
        });

        // Priced next to the charge it reduced, not filed away in a second section — and stated
        // once, so a subscriber cannot add the same figure to itself while reconciling the total.
        html.IndexOf("Automatic price discount", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("<table class=\"totals\">", StringComparison.Ordinal));
        html.Should().NotContain("Automatic price discount</th>");
    }

    [Fact]
    public void The_customer_number_is_the_subscribers_organization_id()
    {
        Render().Should().Contain("<tr><th>Customer number</th><td>org-1</td></tr>");
    }

    [Fact]
    public void The_vat_line_states_the_rate_and_says_when_it_was_already_inside_the_price()
    {
        Render(document =>
        {
            document.Amounts.TaxRateBasisPoints = 770;
            document.Amounts.TaxMode = nameof(TaxMode.Exclusive);
        }).Should().Contain(
            "<th>VAT (7.7%)</th>",
            "the reference labels VAT added to the net as \"VAT (8.1%)\"");

        Render(document =>
        {
            document.Amounts.TaxRateBasisPoints = 770;
            document.Amounts.TaxMode = nameof(TaxMode.Inclusive);
        }).Should().Contain(
            "<th>VAT (7.7%, included)</th>",
            "an inclusive rate is already inside the net amount, and saying nothing invites adding it twice");
    }

    [Fact]
    public void Credit_is_shown_below_vat_because_it_pays_a_bill_rather_than_reducing_one()
    {
        var html = Render(document =>
        {
            document.Amounts.TaxRateBasisPoints = 770;
            document.Amounts.TaxAmountMinor = 6_930;
            document.Amounts.CreditAppliedMinor = 1_000;
        });

        html.IndexOf("Account credit applied", StringComparison.Ordinal)
            .Should().BeGreaterThan(html.IndexOf("VAT (7.7%", StringComparison.Ordinal));
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
    public void The_period_column_ends_on_the_last_day_covered_rather_than_the_next_periods_first()
    {
        // The stored local end is the boundary — the first day of the next period. Printed as-is,
        // INV-2026-000012 read "2026-09-29 to 2026-10-01" beside "2 of 30 days".
        var html = Render(document =>
        {
            document.Period.StartUtc = new DateTime(2026, 9, 29, 13, 48, 53, DateTimeKind.Utc);
            document.Period.EndUtc = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
            document.Period.LocalStart = "2026-09-29";
            document.Period.LocalEnd = "2026-10-01";
            document.Period.IsProrated = true;
            document.Period.ProratedDays = 2;
            document.Period.ProratedTotalDays = 30;
        });

        html.Should().Contain(
            "29.09.2026–<br>30.09.2026",
            "a two-day proration must not be printed as a three-day period");
        html.Should().Contain(
            "Europe/Zurich · 2 of 30 days",
            "the proration is what reconciles Price with Value on the same row");
        html.Should().NotContain("01.10.2026");
    }

    [Fact]
    public void A_period_that_starts_and_ends_on_one_local_day_does_not_end_the_day_before()
    {
        var html = Render(document =>
        {
            document.Period.StartUtc = new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc);
            document.Period.EndUtc = new DateTime(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
            document.Period.LocalStart = "2026-09-29";
            document.Period.LocalEnd = "2026-09-29";
        });

        html.Should().Contain("29.09.2026–<br>29.09.2026", "a period cannot end before it starts");
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
        html.Should().Contain("<td>VAT</td>", "the settlement names the tax the way the totals do");
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
            .Should().Contain("<tr><th>Status</th><td>Paid, partially refunded</td></tr>");
        Render(document => document.Status = FinancialDocumentStatus.Refunded)
            .Should().Contain("<tr><th>Status</th><td>Paid, since refunded in full</td></tr>");
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
    public void The_total_among_the_facts_carries_the_currency_code_beside_it()
    {
        // Never a locale symbol. "1.234,00 €" and "€1,234.00" are the same number to two different
        // readers and different numbers to a careless one.
        Render().Should().Contain("CHF 1&#39;000.00");
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
        html.Should().NotContain("100,000.00");
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
        html.Should().Contain("<div class=\"merchant\">Blocks AG</div>");
    }

    [Fact]
    public void A_logo_warning_still_renders_the_document_from_the_merchant_name()
    {
        // The resolver's contract: a warning means "fell back", never "stop". A document must still
        // come out the other end even when its branding asset could not be read.
        var html = Render(logo: FinancialDocumentLogoResolution.Warning("document_logo_unavailable"));

        html.Should().NotContain("<img");
        html.Should().Contain("<div class=\"merchant\">Blocks AG</div>");
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
    public void The_total_and_its_status_are_facts_in_the_right_column_rather_than_a_headline()
    {
        var html = Render();

        html.Should().Contain("<tr><th>Invoice total</th><td class=\"strong\">CHF 1&#39;000.00</td></tr>");
        html.Should().Contain("<tr><th>Status</th><td>Paid</td></tr>");
        html.Should().NotContain("class=\"headline\"", "the reference has no headline amount");

        var creditNote = Render(document =>
        {
            document.DocumentType = FinancialDocumentType.CreditNote;
            document.Amounts.TotalMinor = 1_000_00;
        });

        creditNote.Should().Contain("<th>Credit note total</th>");
        creditNote.Should().Contain("<th>Credit note number</th>");
        creditNote.Should().Contain("<tr><th>Status</th><td>Credited</td></tr>");
    }

    [Fact]
    public void The_subscription_and_its_utc_period_are_kept_among_the_facts()
    {
        var html = Render();

        html.Should().Contain("<tr><th>Subscription</th><td>sub-1</td></tr>");
        html.Should().Contain(
            "<tr><th>Service period (UTC)</th><td>2026-01-01 00:00:00Z to 2027-01-01 00:00:00Z</td></tr>");
    }

    [Fact]
    public void Dates_are_written_as_the_reference_writes_them()
    {
        Render().Should().Contain("<tr><th>Invoice date</th><td>25.08.2026</td></tr>");
    }

    [Fact]
    public void The_merchants_vat_number_is_a_fact_rather_than_an_address_line()
    {
        var html = Render(document => document.Merchant.TaxRegistrationId = "CHE-104.375.184 MWST");

        html.Should().Contain("<tr><th>VAT No.</th><td class=\"strong\">CHE-104.375.184 MWST</td></tr>");
        html.Should().NotContain("Tax ID", "the reference labels it VAT No.");
    }

    [Fact]
    public void A_merchant_with_no_vat_number_prints_no_empty_row()
    {
        Render().Should().NotContain("VAT No.");
    }

    [Fact]
    public void The_footer_is_the_merchant_profiles_letterhead_as_the_reference_prints_it()
    {
        // Every value on these lines comes from the merchant profile snapshotted at issue: the
        // reference's own footer, reproduced from those fields.
        var html = Render(document => document.Merchant = new FinancialDocumentMerchant
        {
            LegalName = "SELISE Group AG",
            DisplayName = "AMLORA",
            Address = new BillingAddress
            {
                Line1 = "The Circle 37",
                PostalCode = "8058",
                City = "Zürich-Flughafen",
                CountryCode = "CH"
            },
            SupportEmail = "info@amlora.ch",
            PaymentInstructions =
                "Bankers – Raiffeisenbank Seerücken – BIC RAIFCH22XXX – IBAN CH98 8080 8001 5221 3223 4"
        });

        var footer = html[html.IndexOf("<div class=\"foot\">", StringComparison.Ordinal)..];

        footer.Should().Contain(
            "AMLORA – SELISE Group AG - The Circle 37 - CH-8058 Zürich-Flughafen - info@amlora.ch");
        footer.Should().Contain(
            "IBAN CH98 8080 8001 5221 3223 4",
            "the reference's bank line is the merchant's payment instructions");
        html.Should().NotContain("How to pay");
        html.Should().NotContain("Questions?");
    }

    [Fact]
    public void The_seller_is_not_repeated_as_an_address_block_in_the_body()
    {
        var html = Render(document => document.Merchant.SupportEmail = "billing@blocks.example");

        // Once, in the footer: the reference has no seller block beside "Bill to".
        html.Split("billing@blocks.example").Length.Should().Be(2);
        html.IndexOf("billing@blocks.example", StringComparison.Ordinal)
            .Should().BeGreaterThan(html.IndexOf("<div class=\"foot\">", StringComparison.Ordinal));
    }

    [Fact]
    public void The_footer_repeats_on_every_page_without_covering_the_last_lines_of_one()
    {
        var html = Render();

        html.Should().Contain("position:fixed", "Chromium repeats a fixed element on every printed page");
        html.Should().Contain(
            "<tfoot><tr><td><div class=\"foot-space\">",
            "the repeated spacer keeps the band the footer is drawn into clear of line items");
    }

    [Fact]
    public void A_zero_amount_prints_as_zero_rather_than_as_a_bare_currency_code()
    {
        // Against the real resolver, not the permissive fake below: TryConvertBack refuses anything
        // at or below zero, so every nil figure on a document used to render as "CHF" with no
        // number at all. On a Total line that reads as missing data rather than as nothing owed,
        // which is the one reading a financial record must never invite.
        var money = new FinancialDocumentMoneyFormatter(RealResolver(), "CHF");

        money.Format(0).Should().Be("CHF 0.00");
        money.Format(1_000_00).Should().Be("CHF 1'000.00");
        money.Format(-2_50).Should().Be("-CHF 2.50");
    }

    [Fact]
    public void A_bare_figure_keeps_the_zero_and_the_unknown_currency_rules()
    {
        var money = new FinancialDocumentMoneyFormatter(RealResolver(), "CHF");

        money.FormatFigure(0).Should().Be("0.00", "a nil figure is a number, not a blank");
        money.FormatFigure(1_000_00).Should().Be("1'000.00");
        money.FormatFigure(-2_50).Should().Be("-2.50");
        new FinancialDocumentMoneyFormatter(RealResolver(), "XXX").FormatFigure(1_000_00)
            .Should().Be(
                "—",
                "minor units printed for an unknown exponent would be wrong by a factor of a hundred");
    }

    [Fact]
    public void Francs_are_grouped_with_the_swiss_apostrophe_and_other_currencies_keep_the_comma()
    {
        new FinancialDocumentMoneyFormatter(RealResolver(), "CHF").FormatFigure(1_234_567_89)
            .Should().Be("1'234'567.89", "the reference and Swiss convention group francs with an apostrophe");
        new FinancialDocumentMoneyFormatter(RealResolver(), "EUR").FormatFigure(1_234_567_89)
            .Should().Be(
                "1,234,567.89",
                "an EUR reader was never shown the apostrophe, and it is a franc convention, not a universal one");
    }

    [Fact]
    public void An_unconfigured_currency_still_prints_the_code_alone()
    {
        // The fallback the zero fix must not swallow. Printing minor units for a currency whose
        // exponent is unknown would be wrong by a factor of a hundred, so the code alone stands.
        var money = new FinancialDocumentMoneyFormatter(RealResolver(), "XXX");

        money.Format(0).Should().Be("XXX");
        money.Format(1_000_00).Should().Be("XXX");
    }

    [Fact]
    public void Numeric_headings_are_aligned_with_the_figures_beneath_them()
    {
        // ".num" alone does not do it. ".lines th" also sets an alignment and is the more specific
        // selector, so every numeric heading sat left of its own column while the values under it
        // were right-aligned. Pinned as a selector because the template is asserted as HTML rather
        // than as pixels.
        var html = Render();

        html.Should().Contain(".lines th.num");
        html.Should().Contain("{text-align:right}");
    }

    [Fact]
    public void The_details_table_carries_the_references_columns()
    {
        var html = Render(document => document.Amounts.TaxRateBasisPoints = 1_000);

        html.Should().Contain(
            "<th>Item</th><th>Description</th><th>Period</th>" +
            "<th class=\"num\">Qty</th><th class=\"num\">Discount</th>" +
            "<th class=\"num\">Price</th><th class=\"num\">Value</th>");
        html.Should().NotContain(">Tax</th>", "VAT is stated once, in the totals, as on the reference");
    }

    [Fact]
    public void Lines_are_numbered_in_tens_with_bare_figures_in_the_money_columns()
    {
        var html = Render();

        html.Should().Contain("<tr><td>10</td><td>Pro</td>");
        html.Should().Contain(
            "<td class=\"num strong\">1&#39;000.00</td>",
            "the currency is stated once per totals row, not in every cell of the table");
    }

    [Fact]
    public void The_totals_are_the_references_three_rows_with_the_currency_in_its_own_column()
    {
        var html = Render(document => document.Amounts.TaxRateBasisPoints = 810);

        html.Should().Contain(
            "<tr><th>Net amount</th><td class=\"cur\">CHF</td><td class=\"num\">1&#39;000.00</td></tr>");
        html.Should().Contain("<th>VAT (8.1%)</th>");
        html.Should().Contain("<tr class=\"grand\"><th>Invoice total</th>");
        html.Should().NotContain(
            "Subtotal",
            "a gross figure above the net invites subtracting the discount rows from it a second time");
    }

    [Fact]
    public void An_untaxed_document_prints_a_vat_row_without_claiming_a_zero_rate()
    {
        var html = Render(document => document.Amounts.TaxRateBasisPoints = null);

        html.Should().Contain("<th>VAT</th>");
        html.Should().NotContain("VAT (0%");
    }

    [Fact]
    public void Bill_to_comes_before_the_facts_and_both_before_the_details()
    {
        // The reference's order: "Bill to" in the left column, the document's facts in the right,
        // then the details table.
        var html = Render();

        html.IndexOf("Bill to", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("Invoice number", StringComparison.Ordinal));
        html.IndexOf("Invoice number", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf(">Details<", StringComparison.Ordinal));
    }

    [Fact]
    public void Bill_to_names_the_contact_under_the_address_as_the_reference_does()
    {
        var html = Render();

        html.IndexOf("Northwind Trading AG", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("Ada Byron", StringComparison.Ordinal));
        html.Should().Contain("<div>ada@northwind.example</div>");
    }

    [Fact]
    public void Labels_are_sentence_case_rather_than_letterspaced_capitals()
    {
        // The single change that made the old output read as a different document from the design.
        var html = Render();

        html.Should().NotContain("text-transform:uppercase");
        html.Should().NotContain("letter-spacing:.12em");
    }

    [Fact]
    public void Payment_instructions_are_the_footers_bank_line()
    {
        var html = Render(document =>
            document.Merchant.PaymentInstructions = "Pay by bank transfer to IBAN CH00.");

        html.IndexOf("Pay by bank transfer to IBAN CH00.", StringComparison.Ordinal)
            .Should().BeGreaterThan(html.IndexOf("<div class=\"foot\">", StringComparison.Ordinal));
    }

    [Fact]
    public void A_merchant_with_no_payment_instructions_renders_no_empty_bank_line()
    {
        // Rather than a blank line, which reads as a value withheld instead of a field this tenant
        // does not use.
        Render(document => document.Merchant.PaymentInstructions = null)
            .Should().NotContain("class=\"pay-body\"");
    }

    [Fact]
    public void The_stylesheet_asks_for_no_font_it_cannot_be_given()
    {
        // Self-contained by the same rule that forbids a remote logo: no @font-face, no network
        // fetch. The design's own face is not installed in the render container.
        var html = Render();

        html.Should().NotContain("@font-face");
        html.Should().NotContain("fonts.googleapis");
    }

    /// <summary>
    /// The real resolver, configured the way an environment configures it.
    /// </summary>
    /// <remarks>
    /// The fake below answers every conversion with true, including the zero the real one refuses.
    /// That is why a nil total rendered as a bare currency code for as long as it did: every test
    /// asserted against a resolver more permissive than the one in production.
    /// </remarks>
    private static ICurrencyMinorUnitResolver RealResolver()
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options
            .SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions
            {
                CurrencyMinorUnits = new Dictionary<string, int> { ["CHF"] = 2, ["EUR"] = 2, ["JPY"] = 0 }
            });

        return new CurrencyMinorUnitResolver(options.Object);
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
