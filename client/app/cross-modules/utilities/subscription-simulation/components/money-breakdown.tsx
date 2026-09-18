import type { SubscriptionPreviewTax } from "../models/subscription-simulation.model";
import { formatMoney } from "../../subscription/utilities/subscription-format";

const Row = ({ label, value }: { label: string; value: string }) => (
  <div className="flex items-baseline justify-between gap-4">
    <span className="text-muted-foreground">{label}</span>
    <span className="text-right font-medium">{value}</span>
  </div>
);

/**
 * "810" basis points read as "8.1%" -- the canonical integer form the server sends, turned into
 * the percentage a human reads. Purely a display transform: the amount actually charged never
 * comes from this division, only from the minor-unit figures the server already computed.
 */
const formatTaxPercent = (rateBasisPoints: number) => `${rateBasisPoints / 100}%`;

const formatTaxLabel = (tax: SubscriptionPreviewTax) =>
  `VAT (${formatTaxPercent(tax.rateBasisPoints)}, ${
    tax.mode === "Inclusive" ? "included" : "added"
  })`;

export const formatDate = (isoDate: string) => new Date(isoDate).toLocaleString();

/**
 * A renewal or charge date, without the time of day. A billing date is a calendar fact; the
 * seconds it happens to carry are noise beside a figure the subscriber is reading.
 */
export const formatDay = (isoDate: string) => new Date(isoDate).toLocaleDateString();

/**
 * One money breakdown, shared by every quote this module shows -- the due-now figures, the
 * next-renewal figures below them, and the discount tester's before/after pair. The same
 * subtotal/discount/net/tax/total shape, so a reader reads them all the same way.
 *
 * Net subtotal always shows, even with no discount, so the chain from subtotal to total is
 * explicit rather than something a reader has to add up themselves. Tax shows whenever the price
 * carries one at all -- including a zero amount, such as a card-free trial's due-now figure --
 * because hiding a configured-but-zero tax would read as "this price has no tax," which is not
 * what it means.
 */
export const MoneyBreakdown = ({
  label,
  labelValue,
  subtotalParts,
  currencyCode,
  subtotalMinor,
  builtInDiscountMinor,
  promotionalDiscountMinor,
  netSubtotalMinor,
  tax,
  totalLabel,
  totalMinor,
}: {
  label?: string;
  /**
   * Shown on the right of the section heading -- the date this section's charge falls on. A
   * heading with a bare label sits directly above rows that all carry a figure on the right,
   * which reads as a row whose own value failed to load rather than as a heading.
   */
  labelValue?: string;
  /**
   * What the subtotal is made of, when it is made of more than one thing. A calendar-aligned
   * yearly signup adds a stub priced from the linked monthly amount to a whole year priced from
   * the annual one; shown as a single figure under a heading reading "CHF 1,000.00 every year",
   * their sum looks like an arithmetic error, and the small built-in discount on the stub alone
   * looks like a rounding bug. Named, both read as what they are. Omitted everywhere else, where
   * the subtotal is one period at one price and naming it twice would be noise.
   */
  subtotalParts?: { label: string; amountMinor: number }[];
  currencyCode: string;
  subtotalMinor: number;
  builtInDiscountMinor: number;
  promotionalDiscountMinor: number;
  netSubtotalMinor: number;
  tax: SubscriptionPreviewTax | null;
  totalLabel: string;
  totalMinor: number;
}) => (
  <div className="space-y-1">
    {label ? (
      <div className="flex items-baseline justify-between gap-4">
        <p className="text-xs font-medium text-muted-foreground">{label}</p>
        {labelValue ? (
          <span className="text-xs text-muted-foreground">{labelValue}</span>
        ) : null}
      </div>
    ) : null}
    {subtotalParts?.map((part) => (
      <Row
        key={part.label}
        label={part.label}
        value={formatMoney(part.amountMinor, currencyCode)}
      />
    ))}
    <Row label="Subtotal" value={formatMoney(subtotalMinor, currencyCode)} />
    {builtInDiscountMinor > 0 ? (
      <Row
        label="Built-in discount"
        value={`-${formatMoney(builtInDiscountMinor, currencyCode)}`}
      />
    ) : null}
    {promotionalDiscountMinor > 0 ? (
      <Row
        label="Promotional discount"
        value={`-${formatMoney(promotionalDiscountMinor, currencyCode)}`}
      />
    ) : null}
    <Row label="Net subtotal" value={formatMoney(netSubtotalMinor, currencyCode)} />
    {tax ? (
      <Row label={formatTaxLabel(tax)} value={formatMoney(tax.amountMinor, currencyCode)} />
    ) : null}
    <Row label={totalLabel} value={formatMoney(totalMinor, currencyCode)} />
  </div>
);
