using System.Globalization;
using System.Text;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

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

        // The content sits in a one-cell table whose footer row is an empty spacer. Chromium repeats
        // a table's footer group on every printed page, so each page keeps a band at the bottom
        // clear, and the merchant footer (fixed-position, which Chromium also repeats per page) is
        // drawn into that band. Without the spacer a full page runs its last lines under the
        // footer, and a line item hidden behind the letterhead is a line the subscriber never saw.
        html.Append("<table class=\"page\"><tfoot><tr><td><div class=\"foot-space\"></div>")
            .Append("</td></tr></tfoot><tbody><tr><td>");

        // Order and placement follow the AMLORA reference: logo alone at the top, then who is billed
        // beside what the document is, then the details table, then the totals. Seller identity and
        // bank details belong to the page footer, as they do on the reference.
        AppendHeader(html, document, logo?.DataUri);
        AppendParties(html, document, money);

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
        AppendPaymentCard(html, document);

        html.Append("</td></tr></tbody></table>");
        AppendFooter(html, document);

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
        // The logo alone, as on the reference. The document's title moved to the head of the facts
        // column, where the reference prints "Invoice" above the fields it names.
        html.Append("<div class=\"head\">");

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

        html.Append("</div>");
    }

    /// <summary>
    /// "Bill to" on the left and the document's facts on the right, the reference's two columns.
    /// </summary>
    private static void AppendParties(
        StringBuilder html,
        SubscriptionFinancialDocument document,
        FinancialDocumentMoneyFormatter money)
    {
        html.Append("<div class=\"cols\">");
        AppendBillTo(html, document);
        AppendFacts(html, document, money);
        html.Append("</div>");
    }

    private static void AppendBillTo(StringBuilder html, SubscriptionFinancialDocument document)
    {
        html.Append("<div class=\"col\"><div class=\"strong\">Bill to</div>");
        html.Append("<div>")
            .Append(Escape(Fallback(document.Subscriber.LegalName, document.Subscriber.OrganizationId)))
            .Append("</div>");

        if (document.Subscriber.DisplayName is { Length: > 0 } displayName &&
            !string.Equals(displayName, document.Subscriber.LegalName, StringComparison.Ordinal))
        {
            html.Append("<div>").Append(Escape(displayName)).Append("</div>");
        }

        AppendAddress(html, document.Subscriber.Address);

        if (document.Subscriber.TaxRegistrationId is { Length: > 0 } taxId)
        {
            html.Append("<div>VAT No. ").Append(Escape(taxId)).Append("</div>");
        }

        // The reference names the person under the address and ends on their email. The billing
        // contact is that person here; a name identical to the organization's is not repeated.
        var contact = document.BillingContact;
        if (contact.Name is { Length: > 0 } contactName &&
            !string.Equals(contactName, document.Subscriber.LegalName, StringComparison.Ordinal))
        {
            html.Append("<div>").Append(Escape(contactName)).Append("</div>");
        }

        if (contact.Email is { Length: > 0 } contactEmail)
        {
            html.Append("<div>").Append(Escape(contactEmail)).Append("</div>");
        }

        html.Append("</div>");
    }

    /// <summary>
    /// The facts column: the reference's fields in the reference's order, then the ones this
    /// application keeps on top of it.
    /// </summary>
    /// <remarks>
    /// Kept beyond the reference, deliberately: the subscription id and the UTC service period are
    /// what support and reconciliation look a document up by; the status is where a refund shows,
    /// which a paid-at-issue document otherwise cannot say; and who initiated the charge is stated
    /// because the subscription module records the acting person on every invoice and credit note.
    /// The reference's "Delivery date" is not shown: nothing is delivered here beyond the service
    /// period, which the details table already states per line.
    /// </remarks>
    private static void AppendFacts(
        StringBuilder html,
        SubscriptionFinancialDocument document,
        FinancialDocumentMoneyFormatter money)
    {
        var title = TitleOf(document.DocumentType);

        html.Append("<div class=\"col\"><div class=\"kind\">").Append(Escape(title)).Append("</div>");
        html.Append("<table class=\"meta\">");

        AppendMetaRow(html, "Customer number", document.Subscriber.OrganizationId);
        AppendMetaRow(html, "Currency", document.CurrencyCode);
        AppendMetaRow(html, $"{title} total", money.Format(document.Amounts.TotalMinor), strong: true);
        AppendMetaRow(html, $"{title} number", document.DocumentNumber);
        AppendMetaRow(html, $"{title} date", Date(document.IssuedAtUtc));

        if (document.OriginalDocumentNumber is { Length: > 0 } originalNumber)
        {
            // Required on a credit note: it is meaningless on its own, and the invoice it adjusts is
            // the first thing anybody reconciling it looks for.
            AppendMetaRow(html, "Adjusts invoice", originalNumber);
        }

        // The merchant's own registration, where the reference prints it: among the document's
        // facts, bold, rather than under an address block the reference does not have.
        if (document.Merchant.TaxRegistrationId is { Length: > 0 } merchantTaxId)
        {
            AppendMetaRow(html, "VAT No.", merchantTaxId, strong: true);
        }

        AppendMetaRow(html, "Subscription", document.SubscriptionId);

        var period = document.Period;
        if (period.StartUtc != default || period.EndUtc != default)
        {
            // The UTC instants are the only version two documents can be compared on; the local
            // dates the subscriber experienced are in the details table's Period column.
            AppendMetaRow(
                html,
                "Service period (UTC)",
                $"{Instant(period.StartUtc)} to {Instant(period.EndUtc)}");
        }

        AppendMetaRow(html, "Status", StatusText(document));

        if (document.InitiatedBy.UserId is { Length: > 0 } || document.InitiatedBy.Name is { Length: > 0 })
        {
            AppendMetaRow(html, "Initiated by", Fallback(document.InitiatedBy.Name, "—"));
        }

        html.Append("</table></div>");
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
        if (document.Lines.Count == 0 && !HasAnyDiscount(document.Amounts))
        {
            return;
        }

        html.Append("<div class=\"section\">Details</div>");
        html.Append("<table class=\"lines\"><thead><tr>");
        html.Append("<th>Item</th><th>Description</th><th>Period</th>");
        html.Append("<th class=\"num\">Qty</th><th class=\"num\">Discount</th>");
        html.Append("<th class=\"num\">Price</th><th class=\"num\">Value</th></tr></thead><tbody>");

        // One period for the whole document rather than one per line, so every line states it.
        var period = PeriodCell(document.Period);

        for (var index = 0; index < document.Lines.Count; index++)
        {
            var line = document.Lines[index];

            // Numbered in tens, as the reference numbers its first item "10": the convention that
            // leaves room to insert a line between two others without renumbering either.
            html.Append("<tr><td>")
                .Append(((index + 1) * 10).ToString(CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(Escape(line.Description));

            // The item key under the description: it is what tells two lines with the same wording
            // apart.
            if (line.ItemKey is { Length: > 0 } itemKey)
            {
                html.Append("<span class=\"sub\">").Append(Escape(itemKey)).Append("</span>");
            }

            html.Append("</td><td>").Append(period).Append("</td>");
            html.Append("<td class=\"num\">")
                .Append(line.Quantity is { } quantity
                    ? Escape(MeterQuantity.Describe(quantity))
                    : "&mdash;")
                .Append("</td><td class=\"num\"></td>");
            html.Append("<td class=\"num\">")
                .Append(line.UnitAmountMinor is { } unit ? Escape(money.FormatFigure(unit)) : "&mdash;")
                .Append("</td>");
            html.Append("<td class=\"num strong\">").Append(Escape(money.FormatFigure(line.AmountMinor)))
                .Append("</td></tr>");
        }

        // Each discount source as its own row in the same table, the way the reference adjusts a line
        // directly beneath it. Its Value is the deduction itself, so the column adds up to the net
        // amount below: the document records its discounts once for the whole invoice rather than
        // per line, and on a multi-line invoice there is no single line's value to net them into.
        AppendDiscountLines(html, document.Amounts, money);

        html.Append("</tbody></table>");
    }

    /// <summary>
    /// The Period cell: the local dates the subscriber experienced, then their zone and, when the
    /// period is a fraction of an interval, how much of it this covers.
    /// </summary>
    /// <remarks>
    /// The proration note sits here because it is what reconciles Price with Value on the same row —
    /// "10.00" priced and "0.67" charged reads as an error unless the row says it was two days.
    /// </remarks>
    private static string PeriodCell(FinancialDocumentPeriod period)
    {
        if (period.StartUtc == default && period.EndUtc == default)
        {
            return "&mdash;";
        }

        var start = LocalDay(period.LocalStart, period.StartUtc);
        var end = LocalDay(period.LocalEnd, period.EndUtc);
        var cell = new StringBuilder();

        cell.Append(Escape(start is { } first ? Date(first) : "—"))
            .Append("–<br>")
            .Append(Escape(end is { } last && start is { } from ? Date(LastServiceDay(from, last)) : "—"));

        var notes = new List<string>(2);
        if (!string.Equals(period.TimeZoneId, "UTC", StringComparison.Ordinal))
        {
            notes.Add(period.TimeZoneId);
        }

        if (period.IsProrated && period.ProratedDays is { } days &&
            period.ProratedTotalDays is { } total)
        {
            notes.Add(
                $"{days.ToString(CultureInfo.InvariantCulture)} of " +
                $"{total.ToString(CultureInfo.InvariantCulture)} days");
        }

        if (notes.Count > 0)
        {
            cell.Append("<span class=\"sub\">").Append(Escape(string.Join(" · ", notes))).Append("</span>");
        }

        return cell.ToString();
    }

    /// <summary>
    /// The local calendar date the issuer wrote, or the UTC date when a document predates it.
    /// </summary>
    private static DateOnly? LocalDay(string local, DateTime instantUtc) =>
        DateOnly.TryParseExact(
            local,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var day)
            ? day
            : instantUtc == default
                ? null
                : DateOnly.FromDateTime(instantUtc.ToUniversalTime());

    /// <summary>
    /// The last calendar day the service covers, as the reference states a period's end.
    /// </summary>
    /// <remarks>
    /// The stored end is the period's boundary — the instant the next period starts — so its local
    /// date is the first day that is no longer covered. Printed as-is, a period from the 29th to a
    /// boundary on the 1st reads as three days beside a "2 of 30 days" that says two, and a
    /// year from 01.10 reads as ending on the 01.10 of the next year.
    /// </remarks>
    private static DateOnly LastServiceDay(DateOnly localStart, DateOnly localEnd) =>
        localEnd > localStart ? localEnd.AddDays(-1) : localStart;

    private static bool HasAnyDiscount(FinancialDocumentAmounts amounts) =>
        amounts.AutomaticDiscountMinor != 0 ||
        amounts.QuantityDiscountMinor != 0 ||
        amounts.PromotionalDiscountMinor != 0;

    private static void AppendDiscountLines(
        StringBuilder html,
        FinancialDocumentAmounts amounts,
        FinancialDocumentMoneyFormatter money)
    {
        if (amounts.AutomaticDiscountMinor != 0)
        {
            AppendDiscountLine(
                html,
                "Automatic price discount",
                amounts.AutomaticDiscountBasisPoints,
                money,
                -amounts.AutomaticDiscountMinor);
        }

        if (amounts.QuantityDiscountMinor != 0)
        {
            AppendDiscountLine(
                html,
                "Volume discount",
                amounts.QuantityDiscountBasisPoints,
                money,
                -amounts.QuantityDiscountMinor);
        }

        if (amounts.PromotionalDiscountMinor != 0)
        {
            // No rate: the document records what a promotion took off, not the percentage it was
            // authored at, and a rate back-computed from rounded minor units is a figure nobody set.
            AppendDiscountLine(
                html,
                amounts.PromotionCode is { Length: > 0 } code
                    ? $"Promotional discount ({code})"
                    : "Promotional discount",
                null,
                money,
                -amounts.PromotionalDiscountMinor);
        }
    }

    private static void AppendDiscountLine(
        StringBuilder html,
        string label,
        int? basisPoints,
        FinancialDocumentMoneyFormatter money,
        long amountMinor)
    {
        html.Append("<tr><td></td><td>").Append(Escape(label)).Append("</td><td></td><td></td>");
        html.Append("<td class=\"num\">")
            .Append(basisPoints is > 0 ? Escape(Percent(basisPoints.Value)) : string.Empty)
            .Append("</td><td></td>");
        html.Append("<td class=\"num\">").Append(Escape(money.FormatFigure(amountMinor)))
            .Append("</td></tr>");
    }

    /// <summary>
    /// The two sides of a plan or quantity change.
    /// </summary>
    /// <remarks>
    /// A settlement is a subtraction, not a discounted price, so a single subtotal cannot explain it.
    /// The subscriber asking why they were charged a part-month figure is asking about the period they
    /// left and the period they joined, and this is the only place a document can answer that.
    /// </remarks>
    /// <summary>
    /// Renders one settlement — or two, when a prepaid opening-stub upgrade settled the stub and
    /// the annual period it already paid for together.
    /// </summary>
    /// <remarks>
    /// Two sections share one combined total rather than each carrying its own: credit was spent
    /// once against the combination, not against either side in isolation — see
    /// <see cref="Payment.DomainService.Entities.SubscriptionSettlementBreakdown.Annual"/> — so a
    /// per-section credit-and-net line would either double-count it or have to be left blank on
    /// one side, either of which reads as a mistake rather than as the two-part settlement it is.
    /// </remarks>
    private static void AppendSettlement(
        StringBuilder html,
        Payment.DomainService.Entities.SubscriptionSettlementBreakdown settlement,
        FinancialDocumentMoneyFormatter money)
    {
        var isComposite = settlement.Annual is not null;

        html.Append("<div class=\"label spaced\">")
            .Append(Escape(isComposite ? "Opening stub adjustment" : "How this change was settled"))
            .Append("</div>");
        AppendSettlementSection(html, settlement, money, showOwnTotal: !isComposite);

        if (settlement.Annual is { } annual)
        {
            html.Append("<div class=\"label spaced\">Prepaid annual-period adjustment</div>");
            AppendSettlementSection(html, annual, money, showOwnTotal: false);

            html.Append("<table class=\"totals\">");
            if (settlement.CreditConsumedMinor != 0)
            {
                AppendTotalRow(html, "Account credit applied", money, -settlement.CreditConsumedMinor);
            }
            AppendTotalRow(html, "Net settlement", money, settlement.NetSettlementMinor, strong: true);
            html.Append("</table>");
        }
    }

    private static void AppendSettlementSection(
        StringBuilder html,
        Payment.DomainService.Entities.SubscriptionSettlementBreakdown settlement,
        FinancialDocumentMoneyFormatter money,
        bool showOwnTotal)
    {
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
        AppendSettlementRow(html, "VAT", money,
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

        if (showOwnTotal)
        {
            if (settlement.CreditConsumedMinor != 0)
            {
                AppendTotalRow(html, "Account credit applied", money, -settlement.CreditConsumedMinor);
            }

            AppendTotalRow(html, "Net settlement", money, settlement.NetSettlementMinor, strong: true);
        }

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

        // The reference's three rows: net amount, VAT, total. No gross "Subtotal" above them: the
        // lines and their discount rows already add up to the net amount in the table above, and a
        // second, larger figure here invited a subscriber to subtract the discounts from it twice.
        html.Append("<table class=\"totals\">");
        AppendTotalRow(html, "Net amount", money, amounts.NetSubtotalMinor);
        AppendTotalRow(html, VatLabel(amounts), money, amounts.TaxAmountMinor);

        if (amounts.CreditAppliedMinor != 0)
        {
            // Below VAT, because credit pays a bill rather than changing what the bill was for. Put
            // above, it would look like it reduced the taxable base, which it does not.
            AppendTotalRow(html, "Account credit applied", money, -amounts.CreditAppliedMinor);
        }

        AppendTotalRow(
            html,
            document.DocumentType == FinancialDocumentType.CreditNote ? "Total credited" : "Invoice total",
            money,
            amounts.TotalMinor,
            strong: true);

        html.Append("</table>");
    }

    /// <summary>
    /// The masked card the charge was taken from, left-aligned under the totals.
    /// </summary>
    /// <remarks>
    /// Only on an invoice and only when the card was identified at issue: a credit note returns
    /// money rather than taking it, a trial takes none, and a guessed card is worse than none.
    /// Brand and last four only — see <see cref="FinancialDocumentPaymentCard"/>.
    /// </remarks>
    private static void AppendPaymentCard(StringBuilder html, SubscriptionFinancialDocument document)
    {
        if (document.DocumentType != FinancialDocumentType.Invoice ||
            document.PaymentCard is not { LastFour.Length: 4 } card)
        {
            return;
        }

        html.Append("<div class=\"card\"><div class=\"strong\">Payment method</div><div>")
            .Append(Escape($"{BrandName(card.Brand)} •••• {card.LastFour}"))
            .Append("</div></div>");
    }

    /// <summary>
    /// A card network as a subscriber would write it, from the code the provider reported.
    /// </summary>
    /// <remarks>
    /// Adyen and Stripe report the same networks under different codes ("mc" and "mastercard"), so
    /// both spellings map to one name. An unknown code prints as given rather than being dropped,
    /// and an absent one as "Card", so the masked number is never left without a noun.
    /// </remarks>
    private static string BrandName(string? brand) =>
        brand?.Trim().ToUpperInvariant() switch
        {
            null or "" => "Card",
            "VISA" => "Visa",
            "MC" or "MASTERCARD" => "Mastercard",
            "AMEX" or "AMERICAN_EXPRESS" => "American Express",
            "MAESTRO" => "Maestro",
            "DISCOVER" => "Discover",
            "JCB" => "JCB",
            "DINERS" => "Diners Club",
            "CUP" or "UNIONPAY" => "UnionPay",
            "CARTEBANCAIRE" or "CARTES_BANCAIRES" => "Cartes Bancaires",
            _ => brand.Trim()
        };

    /// <summary>
    /// The seller's letterhead, at the foot of every page as the reference prints it.
    /// </summary>
    /// <remarks>
    /// Everything here is the merchant profile as snapshotted at issue: the name, address and
    /// support email on the first line, the payment instructions — the reference's bank line — on
    /// the second, verbatim. A field the merchant left empty is left out rather than dashed, because
    /// a labelled blank reads as a value withheld rather than one this tenant does not use. The
    /// document id stays, small, because it is what support finds the record by.
    /// </remarks>
    private static void AppendFooter(StringBuilder html, SubscriptionFinancialDocument document)
    {
        var merchant = document.Merchant;
        var address = merchant.Address;

        var identity = new[]
            {
                MerchantName(merchant),
                address?.Line1,
                address?.Line2,
                Join(
                    address?.CountryCode is { Length: > 0 } country && address.PostalCode is { Length: > 0 } postal
                        ? $"{country}-{postal}"
                        : address?.PostalCode,
                    address?.City),
                merchant.SupportEmail
            }
            .Where(part => !string.IsNullOrWhiteSpace(part));

        html.Append("<div class=\"foot\">");
        html.Append("<div>").Append(Escape(string.Join(" - ", identity))).Append("</div>");

        if (merchant.PaymentInstructions is { Length: > 0 } instructions)
        {
            html.Append("<div class=\"pay-body\">").Append(Escape(instructions)).Append("</div>");
        }

        html.Append("<div class=\"doc-id\">Document ").Append(Escape(document.ItemId))
            .Append("</div></div>");
    }

    /// <summary>"AMLORA – SELISE Group AG": the trading name, then the registered one.</summary>
    private static string MerchantName(FinancialDocumentMerchant merchant) =>
        merchant.DisplayName is { Length: > 0 } displayName &&
        !string.Equals(displayName, merchant.LegalName, StringComparison.Ordinal) &&
        merchant.LegalName is { Length: > 0 }
            ? $"{displayName} – {merchant.LegalName}"
            : Fallback(merchant.LegalName, merchant.DisplayName ?? string.Empty);

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
                html.Append("<div>").Append(Escape(part)).Append("</div>");
            }
        }
    }

    private static void AppendMetaRow(
        StringBuilder html,
        string label,
        string value,
        bool strong = false)
    {
        html.Append("<tr><th>").Append(Escape(label))
            .Append(strong ? "</th><td class=\"strong\">" : "</th><td>")
            .Append(Escape(value)).Append("</td></tr>");
    }

    private static void AppendTotalRow(
        StringBuilder html,
        string label,
        FinancialDocumentMoneyFormatter money,
        long amountMinor,
        bool strong = false)
    {
        // Label, currency, figure: the reference's three columns, so the code is stated once per row
        // beside a bare number rather than fused into it.
        html.Append(strong ? "<tr class=\"grand\">" : "<tr>");
        html.Append("<th>").Append(Escape(label)).Append("</th><td class=\"cur\">")
            .Append(Escape(money.CurrencyCode)).Append("</td><td class=\"num\">")
            .Append(Escape(money.FormatFigure(amountMinor))).Append("</td></tr>");
    }

    private static string VatLabel(FinancialDocumentAmounts amounts)
    {
        if (amounts.TaxRateBasisPoints is not > 0)
        {
            return "VAT";
        }

        var rate = Percent(amounts.TaxRateBasisPoints.Value);

        // "VAT (8.1%)" as the reference writes it for VAT added to the net. An inclusive price says
        // so, because the same rate then means the figure is already inside the net amount above,
        // and a subscriber checking the arithmetic would otherwise add it a second time.
        return string.Equals(amounts.TaxMode, "Inclusive", StringComparison.OrdinalIgnoreCase)
            ? $"VAT ({rate}, included)"
            : $"VAT ({rate})";
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

    /// <summary>
    /// A date as the reference writes it: "26.08.2026".
    /// </summary>
    /// <remarks>
    /// Day, month, year, dot-separated and zero-padded — the reference's own form, and unambiguous
    /// read as the Swiss format it is. The instants in the facts column stay ISO: those exist to be
    /// compared, not read.
    /// </remarks>
    private static string Date(DateTime instantUtc) =>
        instantUtc == default
            ? "—"
            : Date(DateOnly.FromDateTime(instantUtc.ToUniversalTime()));

    private static string Date(DateOnly day) =>
        day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

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
        // Honoured only when the engine prefers CSS page size; the invoice adapter does not, so the
        // engine's own margins apply and this stays as the intent for any renderer that does.
        "@page{size:A4;margin:16mm 14mm}" +
        // No @font-face and no network font, by the same rule that forbids a remote logo. The stack
        // is the one a headless Chromium can actually satisfy.
        "body{font:11px/1.5 -apple-system,'Segoe UI',Helvetica,Arial,sans-serif;color:#1a1a1a;" +
        "margin:0;padding:0 8mm;-webkit-print-color-adjust:exact;print-color-adjust:exact}" +
        // The page frame: one cell of content above a repeated footer spacer. Its height is the
        // footer's own budget -- two lines of letterhead, a few of payment instructions and the
        // document id.
        "table.page{width:100%;border-collapse:collapse}" +
        "table.page>tfoot{display:table-footer-group}" +
        "table.page>tbody>tr>td,table.page>tfoot>tr>td{padding:0}" +
        ".foot-space{height:30mm}" +
        ".head{margin:8mm 0 18mm}" +
        ".logo{max-height:34px;max-width:200px}" +
        $".merchant{{font-size:19px;font-weight:700;letter-spacing:.02em;color:{palette.Primary}}}" +
        ".cols{display:flex;gap:40px;margin-bottom:34px}" +
        ".col{flex:1}" +
        // Wider than "Bill to", because its values are ids and instants that only read correctly
        // whole: a GUID broken mid-run is one nobody can copy back out of the PDF.
        ".col+.col{flex:1.35}" +
        ".kind{font-size:17px;font-weight:700;margin-bottom:4px}" +
        ".label{font-size:11px;color:#697386;margin-bottom:4px}" +
        ".spaced{margin-top:14px}" +
        ".strong{font-weight:700}" +
        ".muted{color:#697386}" +
        "table{border-collapse:collapse;width:100%}" +
        // The facts column: labels left, values flush right, as on the reference.
        ".meta{width:100%}" +
        ".meta th{text-align:left;font-weight:400;padding:1px 16px 1px 0;white-space:nowrap;" +
        "vertical-align:top}" +
        ".meta td{text-align:right;padding:1px 0;vertical-align:top}" +
        $".note{{background:{palette.Accent};padding:12px 14px;margin-bottom:22px;" +
        "break-inside:avoid}" +
        ".section{font-weight:700;margin-bottom:6px}" +
        ".lines{margin-bottom:0}" +
        ".lines thead{display:table-header-group}" +
        ".lines th{text-align:left;font-weight:700;border-bottom:1px solid #e6e8eb;" +
        "padding:6px 10px 6px 0}" +
        ".lines td{padding:8px 10px 8px 0;vertical-align:top}" +
        ".lines tbody tr:last-child td{border-bottom:1px solid #e6e8eb;padding-bottom:14px}" +
        ".lines tr{break-inside:avoid}" +
        ".sub{display:block;color:#697386;margin-top:2px}" +
        ".num{text-align:right;white-space:nowrap}" +
        // Written out rather than left to ".num" alone: ".lines th" also sets an alignment and is
        // the more specific selector, so a plain ".num" lost to it and every numeric heading sat
        // left of the figures underneath it.
        ".lines th.num,.lines td.num,.totals td.num{text-align:right}" +
        ".lines th:last-child,.lines td:last-child{padding-right:0}" +
        // Indented to sit under the right-hand half of the line table, which is what makes the
        // totals read as a continuation of it rather than as a second table.
        ".totals{width:45%;margin-left:auto;margin-top:6px;break-inside:avoid}" +
        ".totals th{text-align:right;font-weight:400;padding:7px 12px 7px 0;white-space:nowrap}" +
        ".totals td{padding:7px 0}" +
        ".totals td.cur{text-align:left;width:1%;padding-right:12px;white-space:nowrap}" +
        ".totals tr{break-inside:avoid}" +
        ".totals tr.grand th,.totals tr.grand td{font-weight:700;border-top:1px solid #e6e8eb}" +
        // Under the totals on the left, with room above it so it reads as its own fact rather than
        // as a fourth totals row; the page frame's spacer keeps it clear of the footer below.
        ".card{margin-top:28px;break-inside:avoid}" +
        // Fixed, so Chromium prints it at the foot of every page, into the band the page frame's
        // repeated spacer keeps clear.
        ".foot{position:fixed;left:8mm;right:8mm;bottom:0;font-size:10px;line-height:1.45;" +
        "color:#1a1a1a}" +
        ".pay-body{white-space:pre-line}" +
        ".doc-id{color:#697386;margin-top:2px}";
}
