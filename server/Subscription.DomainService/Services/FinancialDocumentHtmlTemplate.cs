using System.Globalization;
using System.Text;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// Renders an issued document to the HTML the PDF is made from.
/// </summary>
/// <remarks>
/// Pure and static: a document and a money formatter in, a string out. That is what makes the layout
/// testable without a browser, and it is also what makes it safe — nothing here reads a database, a
/// clock or configuration, so the same document always renders the same bytes.
/// <para>
/// The template is the application's own, not the payment provider's. That was the point of the whole
/// exercise: the provider's invoice carried their branding, their field names and their idea of which
/// discounts were worth showing, and it disappeared the day we changed processor.
/// </para>
/// <para>
/// Self-contained by construction — inline CSS, no images, no fonts, no scripts. A renderer that has
/// to fetch anything is a renderer that fails when the network does, and an invoice that renders
/// differently depending on what a CDN returned is not a financial record.
/// </para>
/// </remarks>
public static class FinancialDocumentHtmlTemplate
{
    public static string Render(
        SubscriptionFinancialDocument document,
        FinancialDocumentMoneyFormatter money)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(money);

        var html = new StringBuilder(8_192);

        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.Append("<title>").Append(Escape(document.DocumentNumber)).Append("</title>");
        html.Append("<style>").Append(Styles).Append("</style></head><body>");

        AppendHeader(html, document);
        AppendParties(html, document);
        AppendSubject(html, document);

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
        AppendFooter(html, document);

        html.Append("</body></html>");

        return html.ToString();
    }

    private static void AppendHeader(StringBuilder html, SubscriptionFinancialDocument document)
    {
        html.Append("<div class=\"head\"><div>");
        html.Append("<div class=\"merchant\">")
            .Append(Escape(Fallback(document.Merchant.LegalName, "Subscription billing")))
            .Append("</div>");
        AppendAddress(html, document.Merchant.Address);

        if (document.Merchant.TaxRegistrationId is { Length: > 0 } merchantTaxId)
        {
            html.Append("<div class=\"muted\">Tax ID ").Append(Escape(merchantTaxId)).Append("</div>");
        }

        html.Append("</div><div class=\"right\">");
        html.Append("<div class=\"kind\">").Append(Escape(TitleOf(document.DocumentType)))
            .Append("</div>");
        html.Append("<div class=\"number\">").Append(Escape(document.DocumentNumber))
            .Append("</div>");
        html.Append("<table class=\"meta\">");
        AppendMetaRow(html, "Issued", Date(document.IssuedAtUtc));
        AppendMetaRow(html, "Currency", document.CurrencyCode);

        if (document.OriginalDocumentNumber is { Length: > 0 } originalNumber)
        {
            // Required on a credit note: it is meaningless on its own, and the invoice it adjusts is
            // the first thing anybody reconciling it looks for.
            AppendMetaRow(html, "Adjusts invoice", originalNumber);
        }

        html.Append("</table></div></div>");
    }

    private static void AppendParties(StringBuilder html, SubscriptionFinancialDocument document)
    {
        html.Append("<div class=\"cols\">");

        html.Append("<div class=\"col\"><div class=\"label\">Billed to</div>");
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

        html.Append("</div>");

        html.Append("<div class=\"col\"><div class=\"label\">Contact</div>");
        AppendPerson(html, document.BillingContact);
        html.Append("<div class=\"label spaced\">Initiated by</div>");
        AppendPerson(html, document.InitiatedBy);
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

    private static void AppendSubject(StringBuilder html, SubscriptionFinancialDocument document)
    {
        html.Append("<table class=\"meta wide\">");
        AppendMetaRow(html, "Plan", $"{document.Subject.PlanName} ({document.Subject.PlanCode})");
        AppendMetaRow(html, "Billing cadence", Cadence(document.Subject));
        AppendMetaRow(html, "Subscription", document.SubscriptionId);

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
        html.Append("<div class=\"note\"><div class=\"label\">Trial</div><table class=\"meta wide\">");
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
        html.Append("<th>Description</th><th class=\"num\">Quantity</th>");
        html.Append("<th class=\"num\">Unit</th><th class=\"num\">Amount</th></tr></thead><tbody>");

        foreach (var line in document.Lines)
        {
            html.Append("<tr><td>").Append(Escape(line.Description)).Append("</td>");
            html.Append("<td class=\"num\">")
                .Append(line.Quantity is { } quantity
                    ? Escape(quantity.ToString(CultureInfo.InvariantCulture))
                    : "&mdash;")
                .Append("</td>");
            html.Append("<td class=\"num\">")
                .Append(line.UnitAmountMinor is { } unit ? Escape(money.Format(unit)) : "&mdash;")
                .Append("</td>");
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

        AppendMetaRow(html, "Status", StatusText(document));
        html.Append("</table>");
    }

    private static void AppendFooter(StringBuilder html, SubscriptionFinancialDocument document)
    {
        html.Append("<div class=\"foot\">");

        if (document.Merchant.PaymentInstructions is { Length: > 0 } instructions)
        {
            html.Append("<div>").Append(Escape(instructions)).Append("</div>");
        }

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

    private static string Date(DateTime instantUtc) =>
        instantUtc == default
            ? "—"
            : instantUtc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    private const string Styles =
        "*{box-sizing:border-box}" +
        "body{font:12px/1.5 'Helvetica Neue',Helvetica,Arial,sans-serif;color:#1a1a1a;" +
        "margin:0;padding:32px}" +
        ".head{display:flex;justify-content:space-between;align-items:flex-start;" +
        "border-bottom:2px solid #1a1a1a;padding-bottom:16px;margin-bottom:20px}" +
        ".right{text-align:right}" +
        ".merchant{font-size:16px;font-weight:600}" +
        ".kind{font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:#666}" +
        ".number{font-size:18px;font-weight:600;margin-bottom:6px}" +
        ".cols{display:flex;gap:32px;margin-bottom:20px}" +
        ".col{flex:1}" +
        ".label{font-size:10px;letter-spacing:.12em;text-transform:uppercase;color:#666;" +
        "margin-bottom:4px}" +
        ".spaced{margin-top:12px}" +
        ".strong{font-weight:600}" +
        ".muted{color:#666}" +
        "table{border-collapse:collapse;width:100%}" +
        ".meta th{text-align:left;font-weight:400;color:#666;padding:2px 12px 2px 0;" +
        "white-space:nowrap;vertical-align:top}" +
        ".meta td{padding:2px 0;vertical-align:top}" +
        ".meta.wide{margin-bottom:20px}" +
        ".note{background:#f6f6f6;padding:12px 14px;margin-bottom:20px}" +
        ".lines{margin-bottom:16px}" +
        ".lines th{text-align:left;font-size:10px;letter-spacing:.1em;text-transform:uppercase;" +
        "color:#666;border-bottom:1px solid #ddd;padding:6px 8px}" +
        ".lines td{border-bottom:1px solid #f0f0f0;padding:6px 8px}" +
        ".num{text-align:right;white-space:nowrap}" +
        ".totals{width:auto;margin-left:auto;min-width:280px}" +
        ".totals th{text-align:left;font-weight:400;color:#444;padding:3px 24px 3px 0;" +
        "white-space:nowrap}" +
        ".totals td{padding:3px 0}" +
        ".totals tr.grand th,.totals tr.grand td{font-weight:600;font-size:14px;" +
        "border-top:1px solid #1a1a1a;padding-top:8px}" +
        ".foot{margin-top:32px;padding-top:12px;border-top:1px solid #ddd;font-size:10px;" +
        "color:#666}";
}
