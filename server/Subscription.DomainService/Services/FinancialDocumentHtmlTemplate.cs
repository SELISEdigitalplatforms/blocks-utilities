using System.Globalization;
using System.Text;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// Renders an issued document to the HTML the PDF is made from.
/// </summary>
/// <remarks>
/// Pure and static: a document, a money formatter and an already-resolved logo in, a string out.
/// That is what makes the layout testable without a browser, and it is also what makes it safe —
/// nothing here reads a database, a clock or configuration, so the same three inputs always render
/// the same bytes. The logo is resolved by <see cref="IFinancialDocumentLogoResolver"/> before this
/// is ever called, for the same reason: a renderer that fetches its own assets is a renderer that
/// fails when storage does, and this one cannot, by construction.
/// <para>
/// The template is the application's own, not the payment provider's. That was the point of the
/// whole exercise: the provider's invoice carried their branding, their field names and their idea
/// of which discounts were worth showing, and it disappeared the day we changed processor.
/// </para>
/// <para>
/// Self-contained by construction — inline CSS, no external images, no network fonts, no scripts. A
/// logo is either an already-embedded <c>data:</c> URI or absent; nothing here is ever a URL. A
/// renderer that has to fetch anything is a renderer that fails when the network does, and an
/// invoice that renders differently depending on what a CDN returned is not a financial record.
/// </para>
/// <para>
/// Colors and logo come from <see cref="FinancialDocumentMerchant"/> — the branding snapshotted
/// onto the document at issue, never the merchant profile as it stands today. A tenant that
/// rebrands tomorrow must not repaint an invoice already sent, for the same reason its address and
/// payment instructions do not move either.
/// </para>
/// </remarks>
public static class FinancialDocumentHtmlTemplate
{
    public static string Render(
        SubscriptionFinancialDocument document,
        FinancialDocumentMoneyFormatter money,
        FinancialDocumentLogoResolution? logo = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(money);

        var palette = new Palette(document.Merchant);
        var html = new StringBuilder(8_192);

        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.Append("<title>").Append(Escape(document.DocumentNumber)).Append("</title>");
        html.Append("<style>").Append(Styles(palette)).Append("</style></head><body>");

        AppendHeader(html, document, logo?.DataUri);

        // Order follows the reference design: identity, then who the document is between, then the
        // amount, then what it is made of. The previous order put the amount before the facts that
        // qualify it, which read as a headline with its own footnotes underneath.
        AppendSubject(html, document);
        AppendParties(html, document);
        AppendHeadline(html, document, money);

        if (document.Trial is { } trial)
        {
            AppendTrial(html, trial, document);
        }

        AppendLines(html, document, money);

        if (document.Settlement is { } settlement)
        {
            AppendSettlement(html, settlement, money);
        }

        AppendTotals(html, document, money);
        AppendPaymentInstructions(html, document);
        AppendFooter(html, document, money);

        html.Append("</body></html>");

        return html.ToString();
    }

    /// <summary>The two colors a document renders with, resolved once and read everywhere below.</summary>
    private readonly record struct Palette(string Primary, string Accent)
    {
        public Palette(FinancialDocumentMerchant merchant)
            : this(
                ValidHex(merchant.PrimaryColor) ?? FinancialDocumentBrandingDefaults.PrimaryColor,
                ValidHex(merchant.AccentColor) ?? FinancialDocumentBrandingDefaults.AccentColor)
        {
        }

        // Defensive, not redundant with the validator: this template has no way to know whether the
        // value in front of it passed through UpdateMerchantProfileRequestValidator, a test fixture,
        // or a document issued before the check existed. A malformed value falls back to the shared
        // default rather than reaching the CSS unescaped.
        private static string? ValidHex(string? value) =>
            value is { Length: 7 } && value[0] == '#' &&
                value[1..].All(Uri.IsHexDigit)
                ? value
                : null;
    }

    private static void AppendHeader(
        StringBuilder html,
        SubscriptionFinancialDocument document,
        string? logoDataUri)
    {
        html.Append("<div class=\"head\"><div class=\"brand\">");

        if (logoDataUri is { Length: > 0 })
        {
            // The data URI was already validated by the resolver against a signature allow-list, but
            // it is still interpolated through Escape: the URI itself can never legally need
            // escaping, and a template that decides per-value which inputs to trust is a template
            // that will eventually trust the wrong one.
            html.Append("<img class=\"logo\" alt=\"")
                .Append(Escape(Fallback(document.Merchant.DisplayName, document.Merchant.LegalName)))
                .Append("\" src=\"").Append(Escape(logoDataUri)).Append("\">");
        }
        else
        {
            html.Append("<div class=\"merchant\">")
                .Append(Escape(Fallback(
                    document.Merchant.DisplayName,
                    Fallback(document.Merchant.LegalName, "Subscription billing"))))
                .Append("</div>");
        }

        html.Append("</div><div class=\"kind\">").Append(Escape(TitleOf(document.DocumentType)))
            .Append("</div></div>");
    }

    private static void AppendParties(StringBuilder html, SubscriptionFinancialDocument document)
    {
        html.Append("<div class=\"cols\">");

        html.Append("<div class=\"col\"><div class=\"strong\">")
            .Append(Escape(Fallback(
                document.Merchant.DisplayName,
                Fallback(document.Merchant.LegalName, "Subscription billing"))))
            .Append("</div>");
        AppendAddress(html, document.Merchant.Address);

        if (document.Merchant.SupportEmail is { Length: > 0 } merchantEmail)
        {
            html.Append("<div class=\"muted\">").Append(Escape(merchantEmail)).Append("</div>");
        }

        if (document.Merchant.TaxRegistrationId is { Length: > 0 } merchantTaxId)
        {
            html.Append("<div class=\"muted\">Tax ID ").Append(Escape(merchantTaxId)).Append("</div>");
        }

        html.Append("</div>");

        html.Append("<div class=\"col\"><div class=\"label\">Bill to</div>");
        html.Append("<div class=\"strong\">")
            .Append(Escape(Fallback(document.Subscriber.LegalName, document.Subscriber.OrganizationId)))
            .Append("</div>");

        if (document.Subscriber.DisplayName is { Length: > 0 } displayName &&
            !string.Equals(displayName, document.Subscriber.LegalName, StringComparison.Ordinal))
        {
            html.Append("<div class=\"muted\">").Append(Escape(displayName)).Append("</div>");
        }

        AppendAddress(html, document.Subscriber.Address);

        if (document.Subscriber.TaxRegistrationId is { Length: > 0 } taxId)
        {
            html.Append("<div class=\"muted\">Tax ID ").Append(Escape(taxId)).Append("</div>");
        }

        // Kept, but compact: who to reconcile a charge with, and who set it in motion. Real audit
        // value that the reference design's two-column layout does not show at all, so it is folded
        // into the "Bill to" column rather than given a third of its own.
        html.Append("<div class=\"label spaced\">Contact</div>");
        AppendPerson(html, document.BillingContact);

        if (document.InitiatedBy.UserId is { Length: > 0 } || document.InitiatedBy.Name is { Length: > 0 })
        {
            html.Append("<div class=\"label spaced\">Initiated by</div>");
            AppendPerson(html, document.InitiatedBy);
        }

        html.Append("</div></div>");
    }

    private static void AppendPerson(StringBuilder html, FinancialDocumentPerson person)
    {
        html.Append("<div>").Append(Escape(Fallback(person.Name, "—"))).Append("</div>");

        if (person.Email is { Length: > 0 } email)
        {
            html.Append("<div class=\"muted\">").Append(Escape(email)).Append("</div>");
        }
    }

    /// <summary>
    /// The one large, colored line the reference design puts the money on.
    /// </summary>
    /// <remarks>
    /// The design's own version reads "CHF 5,000 due September 17, 2026" — but nothing this
    /// application issues is ever awaiting a future payment: a document exists because a charge, a
    /// trial or a refund already happened, so there is no due date to state. What is true, and what
    /// this states instead, is the same figure paired with what actually became of it — paid,
    /// credited, or nothing due for a trial — which is the honest analogue of the same visual weight
    /// the reference design gives the amount.
    /// </remarks>
    private static void AppendHeadline(
        StringBuilder html,
        SubscriptionFinancialDocument document,
        FinancialDocumentMoneyFormatter money)
    {
        html.Append("<div class=\"headline\">")
            .Append(Escape(money.Format(document.Amounts.TotalMinor)))
            .Append(" — ").Append(Escape(StatusText(document))).Append("</div>");
    }

    private static void AppendSubject(StringBuilder html, SubscriptionFinancialDocument document)
    {
        html.Append("<table class=\"meta\">");
        AppendMetaRow(html, "Invoice number", document.DocumentNumber);
        AppendMetaRow(html, "Date of issue", Date(document.IssuedAtUtc));
        AppendMetaRow(html, "Currency", document.CurrencyCode);
        AppendMetaRow(html, "Plan", $"{document.Subject.PlanName} ({document.Subject.PlanCode})");
        AppendMetaRow(html, "Billing cadence", Cadence(document.Subject));
        AppendMetaRow(html, "Subscription", document.SubscriptionId);

        if (document.OriginalDocumentNumber is { Length: > 0 } originalNumber)
        {
            // Required on a credit note: it is meaningless on its own, and the invoice it adjusts is
            // the first thing anybody reconciling it looks for.
            AppendMetaRow(html, "Adjusts invoice", originalNumber);
        }

        var period = document.Period;
        if (period.StartUtc != default || period.EndUtc != default)
        {
            // Stated twice on purpose. The local dates are the boundary the subscriber experienced;
            // the UTC instants are the only version two documents can be compared on.
            AppendMetaRow(
                html,
                "Service period",
                $"{Fallback(period.LocalStart, Date(period.StartUtc))} to " +
                $"{Fallback(period.LocalEnd, Date(period.EndUtc))} ({Escape(period.TimeZoneId)})");
            AppendMetaRow(
                html,
                "Service period (UTC)",
                $"{Instant(period.StartUtc)} to {Instant(period.EndUtc)}");
        }

        if (period.IsProrated && period.ProratedDays is { } days &&
            period.ProratedTotalDays is { } total)
        {
            AppendMetaRow(
                html,
                "Prorated",
                $"{days.ToString(CultureInfo.InvariantCulture)} of " +
                $"{total.ToString(CultureInfo.InvariantCulture)} days");
        }

        html.Append("</table>");
    }

    private static void AppendTrial(
        StringBuilder html,
        FinancialDocumentTrial trial,
        SubscriptionFinancialDocument document)
    {
        html.Append("<div class=\"note\"><div class=\"label\">Trial</div><table class=\"meta\">");
        AppendMetaRow(html, "Trial period", $"{Date(trial.StartsAtUtc)} to {Date(trial.EndsAtUtc)}");
        AppendMetaRow(html, "Timezone", document.Period.TimeZoneId);
        AppendMetaRow(
            html,
            "Payment method",
            trial.RequiresPaymentMethod ? "Required up front" : "Not required");

        if (trial.FirstBillingAtUtc is { } firstBilling)
        {
            AppendMetaRow(html, "First billing expected", Date(firstBilling));
        }

        AppendMetaRow(html, "Amount due", "Nothing is charged for a trial period.");
        html.Append("</table></div>");
    }

    private static void AppendLines(
        StringBuilder html,
        SubscriptionFinancialDocument document,
        FinancialDocumentMoneyFormatter money)
    {
        if (document.Lines.Count == 0)
        {
            return;
        }

        html.Append("<table class=\"lines\"><thead><tr>");
        html.Append("<th>Description</th><th class=\"num\">Qty</th>");
        html.Append("<th class=\"num\">Unit price</th><th class=\"num\">Tax</th>");
        html.Append("<th class=\"num\">Amount</th></tr></thead><tbody>");

        // The document carries one rate for all of its lines rather than a rate per line, so the
        // column states that rate rather than implying a per-line figure the record does not hold.
        var lineTax = LineTaxLabel(document.Amounts);

        foreach (var line in document.Lines)
        {
            html.Append("<tr><td>").Append(Escape(line.Description));

            // The item key under the description, the way the design carries "25–40 Users" under
            // its plan name: it is what tells two lines with the same wording apart.
            if (line.ItemKey is { Length: > 0 } itemKey)
            {
                html.Append("<span class=\"sub\">").Append(Escape(itemKey)).Append("</span>");
            }

            html.Append("</td>");
            html.Append("<td class=\"num\">")
                .Append(line.Quantity is { } quantity
                    ? Escape(quantity.ToString(CultureInfo.InvariantCulture))
                    : "&mdash;")
                .Append("</td>");
            html.Append("<td class=\"num\">")
                .Append(line.UnitAmountMinor is { } unit ? Escape(money.Format(unit)) : "&mdash;")
                .Append("</td>");
            html.Append("<td class=\"num\">").Append(lineTax).Append("</td>");
            html.Append("<td class=\"num\">").Append(Escape(money.Format(line.AmountMinor)))
                .Append("</td></tr>");
        }

        html.Append("</tbody></table>");
    }

    /// <summary>
    /// The two sides of a plan or quantity change.
    /// </summary>
    /// <remarks>
    /// A settlement is a subtraction, not a discounted price, so a single subtotal cannot explain it.
    /// The subscriber asking why they were charged a part-month figure is asking about the period they
    /// left and the period they joined, and this is the only place a document can answer that.
    /// </remarks>
    private static void AppendSettlement(
        StringBuilder html,
        Payment.DomainService.Entities.SubscriptionSettlementBreakdown settlement,
        FinancialDocumentMoneyFormatter money)
    {
        html.Append("<div class=\"label spaced\">How this change was settled</div>");
        html.Append("<table class=\"lines\"><thead><tr><th></th>");
        html.Append("<th class=\"num\">Previous terms</th>");
        html.Append("<th class=\"num\">New terms</th></tr></thead><tbody>");

        AppendSettlementRow(html, "Period total before discounts", money,
            settlement.Outgoing.GrossAmountMinor, settlement.Target.GrossAmountMinor);
        AppendSettlementRow(html, "Automatic and volume discounts", money,
            -settlement.Outgoing.BuiltInDiscountMinor, -settlement.Target.BuiltInDiscountMinor);
        AppendSettlementRow(html, "Promotional discount", money,
            -settlement.Outgoing.PromotionalDiscountMinor,
            -settlement.Target.PromotionalDiscountMinor);
        AppendSettlementRow(html, "Tax", money,
            settlement.Outgoing.TaxAmountMinor, settlement.Target.TaxAmountMinor);
        AppendSettlementRow(html, "Full period", money,
            settlement.Outgoing.PeriodTotalMinor, settlement.Target.PeriodTotalMinor);
        AppendSettlementRow(html, "Counted in this settlement", money,
            settlement.Outgoing.ProratedValueMinor, settlement.Target.ProratedValueMinor);

        html.Append("</tbody></table><table class=\"totals\">");
        AppendTotalRow(html, "Unused value on previous terms", money,
            -settlement.Outgoing.ProratedValueMinor);
        AppendTotalRow(html, "Remaining value on new terms", money,
            settlement.Target.ProratedValueMinor);

        if (settlement.CreditConsumedMinor != 0)
        {
            AppendTotalRow(html, "Account credit applied", money, -settlement.CreditConsumedMinor);
        }

        AppendTotalRow(html, "Net settlement", money, settlement.NetSettlementMinor, strong: true);
        html.Append("</table>");
    }

    private static void AppendSettlementRow(
        StringBuilder html,
        string label,
        FinancialDocumentMoneyFormatter money,
        long outgoing,
        long target)
    {
        html.Append("<tr><td>").Append(Escape(label)).Append("</td>");
        html.Append("<td class=\"num\">").Append(Escape(money.Format(outgoing))).Append("</td>");
        html.Append("<td class=\"num\">").Append(Escape(money.Format(target))).Append("</td></tr>");
    }

    private static void AppendTotals(
        StringBuilder html,
        SubscriptionFinancialDocument document,
        FinancialDocumentMoneyFormatter money)
    {
        var amounts = document.Amounts;

        html.Append("<table class=\"totals\">");
        AppendTotalRow(html, "Subtotal", money, amounts.GrossSubtotalMinor);

        // Each source on its own line, always. "Discount" as one figure cannot be read back into
        // "the annual price gave 8% and the coupon gave nothing", and which it was is the question
        // somebody reconciling this in two years is actually asking.
        if (amounts.AutomaticDiscountMinor != 0)
        {
            AppendTotalRow(
                html,
                RateLabel("Automatic price discount", amounts.AutomaticDiscountBasisPoints),
                money,
                -amounts.AutomaticDiscountMinor);
        }

        if (amounts.QuantityDiscountMinor != 0)
        {
            AppendTotalRow(
                html,
                RateLabel("Volume discount", amounts.QuantityDiscountBasisPoints),
                money,
                -amounts.QuantityDiscountMinor);
        }

        if (amounts.PromotionalDiscountMinor != 0)
        {
            AppendTotalRow(
                html,
                amounts.PromotionCode is { Length: > 0 } code
                    ? $"Promotional discount ({code})"
                    : "Promotional discount",
                money,
                -amounts.PromotionalDiscountMinor);
        }

        AppendTotalRow(html, "Net subtotal", money, amounts.NetSubtotalMinor);
        AppendTotalRow(html, TaxLabel(amounts), money, amounts.TaxAmountMinor);

        if (amounts.CreditAppliedMinor != 0)
        {
            // Below tax, because credit pays a bill rather than changing what the bill was for. Put
            // above, it would look like it reduced the taxable base, which it does not.
            AppendTotalRow(html, "Account credit applied", money, -amounts.CreditAppliedMinor);
        }

        AppendTotalRow(
            html,
            document.DocumentType == FinancialDocumentType.CreditNote ? "Total credited" : "Total",
            money,
            amounts.TotalMinor,
            strong: true);

        html.Append("</table>");
    }

    /// <summary>
    /// The rate shown against each line, or an em dash when the document carries none.
    /// </summary>
    /// <remarks>
    /// "10% incl." in the reference design. Abbreviated rather than spelled out because it sits in
    /// a narrow numeric column, and the unabbreviated form is already stated once in the totals,
    /// where there is room for it.
    /// </remarks>
    private static string LineTaxLabel(FinancialDocumentAmounts amounts)
    {
        if (amounts.TaxRateBasisPoints is not > 0)
        {
            return "&mdash;";
        }

        var mode = string.Equals(amounts.TaxMode, "Inclusive", StringComparison.OrdinalIgnoreCase)
            ? "incl."
            : "excl.";

        return Escape($"{Percent(amounts.TaxRateBasisPoints.Value)} {mode}");
    }

    /// <summary>
    /// How to pay, in the place the reference design puts it: after the totals, before the footer.
    /// </summary>
    /// <remarks>
    /// The design prints bank fields with em dashes where a value has yet to be issued. This prints
    /// only what the merchant actually snapshotted onto the document, because a labelled row with a
    /// dash beside it reads as a value that exists and was withheld, rather than as a field this
    /// tenant does not use. Absent instructions render nothing at all.
    /// </remarks>
    private static void AppendPaymentInstructions(
        StringBuilder html,
        SubscriptionFinancialDocument document)
    {
        if (document.Merchant.PaymentInstructions is not { Length: > 0 } instructions)
        {
            return;
        }

        html.Append("<div class=\"pay\"><div class=\"pay-title\">How to pay</div>");
        html.Append("<div class=\"pay-body\">").Append(Escape(instructions)).Append("</div></div>");
    }

    private static void AppendFooter(
        StringBuilder html,
        SubscriptionFinancialDocument document,
        FinancialDocumentMoneyFormatter money)
    {
        html.Append("<div class=\"foot\">");
        html.Append("<div class=\"summary-line\">").Append(Escape(document.DocumentNumber))
            .Append(" · ").Append(Escape(money.Format(document.Amounts.TotalMinor)))
            .Append(" · ").Append(Escape(StatusText(document))).Append("</div>");

        if (document.Merchant.SupportEmail is { Length: > 0 } supportEmail)
        {
            html.Append("<div class=\"muted\">Questions? ").Append(Escape(supportEmail))
                .Append("</div>");
        }

        html.Append("<div class=\"muted\">Document ").Append(Escape(document.ItemId))
            .Append("</div></div>");
    }

    private static void AppendAddress(StringBuilder html, BillingAddress? address)
    {
        if (address is null || address.IsEmpty())
        {
            return;
        }

        foreach (var part in new[]
                 {
                     address.Line1,
                     address.Line2,
                     Join(address.PostalCode, address.City),
                     Join(address.Region, address.CountryCode)
                 })
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                html.Append("<div class=\"muted\">").Append(Escape(part)).Append("</div>");
            }
        }
    }

    private static void AppendMetaRow(StringBuilder html, string label, string value)
    {
        html.Append("<tr><th>").Append(Escape(label)).Append("</th><td>")
            .Append(Escape(value)).Append("</td></tr>");
    }

    private static void AppendTotalRow(
        StringBuilder html,
        string label,
        FinancialDocumentMoneyFormatter money,
        long amountMinor,
        bool strong = false)
    {
        html.Append(strong ? "<tr class=\"grand\">" : "<tr>");
        html.Append("<th>").Append(Escape(label)).Append("</th><td class=\"num\">")
            .Append(Escape(money.Format(amountMinor))).Append("</td></tr>");
    }

    private static string RateLabel(string label, int? basisPoints) =>
        basisPoints is > 0
            ? $"{label} ({Percent(basisPoints.Value)})"
            : label;

    private static string TaxLabel(FinancialDocumentAmounts amounts)
    {
        if (amounts.TaxRateBasisPoints is not > 0)
        {
            return "Tax";
        }

        // The mode is on the line because the same rate means two different things: added to the net,
        // or already inside the price. A subscriber checking the arithmetic needs to know which.
        var mode = string.Equals(amounts.TaxMode, "Inclusive", StringComparison.OrdinalIgnoreCase)
            ? "included"
            : "added";

        return $"Tax ({Percent(amounts.TaxRateBasisPoints.Value)}, {mode})";
    }

    private static string Percent(int basisPoints) =>
        (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string StatusText(SubscriptionFinancialDocument document) =>
        document.DocumentType switch
        {
            FinancialDocumentType.TrialInvoice => "No payment due",
            FinancialDocumentType.CreditNote => "Credited",
            _ => document.Status switch
            {
                FinancialDocumentStatus.Refunded => "Paid, since refunded in full",
                FinancialDocumentStatus.PartiallyRefunded => "Paid, partially refunded",
                _ => "Paid"
            }
        };

    private static string TitleOf(FinancialDocumentType documentType) =>
        documentType switch
        {
            FinancialDocumentType.TrialInvoice => "Trial invoice",
            FinancialDocumentType.CreditNote => "Credit note",
            _ => "Invoice"
        };

    private static string Cadence(FinancialDocumentSubject subject) =>
        subject.IntervalCount <= 1
            ? $"Every {subject.Interval.ToString().ToLowerInvariant()}"
            : $"Every {subject.IntervalCount.ToString(CultureInfo.InvariantCulture)} " +
                $"{subject.Interval.ToString().ToLowerInvariant()}s";

    /// <summary>
    /// A date as the reference design writes it: "August 26, 2026".
    /// </summary>
    /// <remarks>
    /// Long form rather than ISO, and invariant rather than localised. The design spells the month
    /// out, which also removes the one ambiguity a numeric date carries across readers — 08-09 is
    /// two different days depending on where it is read, and a month name is the same day
    /// everywhere. The instants beside it stay ISO: those exist to be compared, not read.
    /// </remarks>
    private static string Date(DateTime instantUtc) =>
        instantUtc == default
            ? "—"
            : instantUtc.ToUniversalTime().ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    private static string Instant(DateTime instantUtc) =>
        instantUtc == default
            ? "—"
            : instantUtc.ToUniversalTime()
                .ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Join(string? left, string? right) =>
        string.Join(
            " ",
            new[] { left, right }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string Fallback(string? value, string whenEmpty) =>
        string.IsNullOrWhiteSpace(value) ? whenEmpty : value;

    /// <summary>
    /// Escapes text for HTML.
    /// </summary>
    /// <remarks>
    /// Applied to every interpolated value without exception, including ones that "cannot" contain
    /// markup. Half of them are typed by a subscriber into a billing profile, the rest come from a
    /// catalogue somebody else edits, and a template that decides per-field which to trust is a
    /// template that will eventually trust the wrong one.
    /// </remarks>
    private static string Escape(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&#39;", StringComparison.Ordinal);

    /// <summary>
    /// The document's stylesheet, parameterised by <see cref="Palette"/>.
    /// </summary>
    /// <remarks>
    /// Print-specific rules throughout: repeated table headers across a page break
    /// (<c>thead</c> as a table-header-group), and <c>break-inside:avoid</c> on every row and on the
    /// totals/footer blocks so a line item or a total is never split by a page boundary. Colors are
    /// forced to print with <c>print-color-adjust:exact</c> — Chromium omits background and
    /// non-default text colors from a PDF by default, which would silently discard the whole point
    /// of a branding feature.
    /// </remarks>
    private static string Styles(Palette palette) =>
        $"*{{box-sizing:border-box}}" +
        "@page{size:A4;margin:16mm 14mm}" +
        // No @font-face and no network font, by the same rule that forbids a remote logo. The stack
        // is the one a headless Chromium can actually satisfy; the design's own face is not
        // installed in the render container, so asking for it here would silently fall back anyway.
        "body{font:11px/1.55 -apple-system,'Segoe UI',Helvetica,Arial,sans-serif;color:#1a1a1a;" +
        "margin:0;padding:0;-webkit-print-color-adjust:exact;print-color-adjust:exact}" +
        ".head{display:flex;justify-content:space-between;align-items:flex-start;" +
        "margin-bottom:28px}" +
        ".logo{max-height:34px;max-width:200px}" +
        $".merchant{{font-size:19px;font-weight:700;letter-spacing:.02em;color:{palette.Primary}}}" +
        ".kind{font-size:19px;font-weight:700;color:#1a1a1a}" +
        ".cols{display:flex;gap:40px;margin-bottom:28px}" +
        ".col{flex:1}" +
        // Sentence case, not the small-caps the previous template used. The reference design labels
        // every field as ordinary prose — "Bill to", "Date of issue" — and uppercase letterspacing
        // is the single change that made the old output read as a different document.
        ".label{font-size:11px;color:#697386;margin-bottom:4px}" +
        ".spaced{margin-top:14px}" +
        ".strong{font-weight:600}" +
        ".muted{color:#697386}" +
        // Black, and not the brand colour. The design gives the amount its weight through size
        // alone; colouring it as well made the figure compete with the wordmark above it.
        ".headline{font-size:19px;font-weight:700;color:#1a1a1a;margin-bottom:26px}" +
        "table{border-collapse:collapse;width:100%}" +
        ".meta{margin-bottom:26px;width:auto}" +
        ".meta th{text-align:left;font-weight:400;color:#697386;padding:2px 28px 2px 0;" +
        "white-space:nowrap;vertical-align:top}" +
        ".meta td{padding:2px 0;vertical-align:top;font-weight:600}" +
        $".note{{background:{palette.Accent};padding:12px 14px;margin-bottom:22px;" +
        "break-inside:avoid}" +
        ".lines{margin-bottom:0}" +
        ".lines thead{display:table-header-group}" +
        ".lines th{text-align:left;font-weight:400;color:#697386;" +
        "border-bottom:1px solid #e6e8eb;padding:8px 10px 8px 0}" +
        ".lines td{border-bottom:1px solid #e6e8eb;padding:10px 10px 10px 0;vertical-align:top}" +
        ".lines tr{break-inside:avoid}" +
        ".sub{display:block;color:#697386;margin-top:2px}" +
        ".num{text-align:right;white-space:nowrap}" +
        // Indented to sit under the right-hand half of the line table, which is what makes the
        // totals read as a continuation of it rather than as a second table.
        ".totals{width:55%;margin-left:auto;margin-top:0;break-inside:avoid}" +
        ".totals th{text-align:left;font-weight:400;color:#1a1a1a;padding:8px 24px 8px 0;" +
        "white-space:nowrap;border-bottom:1px solid #e6e8eb}" +
        ".totals td{padding:8px 0;border-bottom:1px solid #e6e8eb}" +
        ".totals tr{break-inside:avoid}" +
        ".totals tr.grand th,.totals tr.grand td{font-weight:700;border-bottom:none}" +
        ".pay{margin-top:34px;break-inside:avoid}" +
        ".pay-title{font-weight:600;margin-bottom:4px}" +
        ".pay-body{color:#697386;margin-bottom:10px;white-space:pre-line}" +
        ".foot{margin-top:40px;padding-top:12px;border-top:1px solid #e6e8eb;" +
        "color:#697386;break-inside:avoid}" +
        ".summary-line{margin-bottom:4px}";
}
