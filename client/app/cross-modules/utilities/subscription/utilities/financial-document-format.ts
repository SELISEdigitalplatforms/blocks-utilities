import type {
  FinancialDocumentAmounts,
  SubscriptionFinancialDocument,
} from "../models/subscription-billing.model";
import { formatMoney } from "./subscription-format";
import { joinList, tSubscription } from "./subscription-i18n";

/**
 * How a document type is named to somebody reading a list.
 *
 * "Trial invoice" rather than "TrialInvoice", and never "Invoice" for a credit note — the two point
 * in opposite directions and a list that calls them the same thing is a list nobody can total.
 */
export const describeDocumentType = (documentType: string): string => {
  switch (documentType) {
    case "TrialInvoice":
      return tSubscription("FINANCIAL_DOCUMENT_FORMAT.TYPE_TRIAL_INVOICE");
    case "CreditNote":
      return tSubscription("FINANCIAL_DOCUMENT_FORMAT.TYPE_CREDIT_NOTE");
    default:
      return tSubscription("FINANCIAL_DOCUMENT_FORMAT.TYPE_INVOICE");
  }
};

/**
 * What the document's status means for the money, in a phrase.
 *
 * A trial invoice and a credit note are never "paid": one was never charged and the other went the
 * other way. Saying "Paid" on either would be wrong in a way somebody would eventually act on.
 */
export const describeDocumentStatus = (
  document: Pick<SubscriptionFinancialDocument, "documentType" | "status">,
): string => {
  if (document.documentType === "TrialInvoice") {
    return tSubscription("FINANCIAL_DOCUMENT_FORMAT.STATUS_NO_PAYMENT_DUE");
  }

  if (document.documentType === "CreditNote") {
    return tSubscription("FINANCIAL_DOCUMENT_FORMAT.STATUS_CREDITED");
  }

  switch (document.status) {
    case "Refunded":
      return tSubscription("FINANCIAL_DOCUMENT_FORMAT.STATUS_REFUNDED_IN_FULL");
    case "PartiallyRefunded":
      return tSubscription("FINANCIAL_DOCUMENT_FORMAT.STATUS_PARTIALLY_REFUNDED");
    default:
      return tSubscription("FINANCIAL_DOCUMENT_FORMAT.STATUS_PAID");
  }
};

/**
 * The service period as the subscriber experienced it.
 *
 * Prefers the local dates the document itself carries, because they were formatted in the
 * subscriber's own zone when it was issued — a period ending at midnight UTC ends on a different
 * date in Auckland, and recomputing it in the reader's browser would give a third answer.
 */
export const describePeriod = (
  document: Pick<
    SubscriptionFinancialDocument,
    "periodLocalStart" | "periodLocalEnd" | "periodStartUtc" | "periodEndUtc" | "timeZoneId"
  >,
): string => {
  const start = document.periodLocalStart ?? document.periodStartUtc?.slice(0, 10);
  const end = document.periodLocalEnd ?? document.periodEndUtc?.slice(0, 10);

  if (!start || !end) {
    return "—";
  }

  const zone = document.timeZoneId && document.timeZoneId !== "UTC"
    ? ` (${document.timeZoneId})`
    : "";

  return `${start} → ${end}${zone}`;
};

/** One row of the money breakdown, ready to render. */
export interface DocumentAmountRow {
  label: string;
  /** Formatted, sign included. Discounts and credit are shown negative. */
  amount: string;
  /** True for the total, which a table renders differently. */
  isTotal?: boolean;
}

/**
 * The document's figures as the rows of a totals table.
 *
 * Built here rather than in the component so the ordering and the sign conventions have one home and
 * can be tested without rendering anything. Two rules worth naming: every discount source that gave
 * something gets its own row, because "CHF 100 came off" cannot be read back into which promise the
 * subscriber was kept; and credit sits *below* tax, because it pays a bill rather than changing what
 * the bill was for.
 */
export const documentAmountRows = (
  amounts: FinancialDocumentAmounts,
  currencyCode: string,
): DocumentAmountRow[] => {
  const money = (minor: number) => formatMoney(minor, currencyCode);
  const withRate = (label: string, basisPoints?: number | null) =>
    basisPoints && basisPoints > 0
      ? tSubscription("FINANCIAL_DOCUMENT_FORMAT.LABEL_WITH_RATE", {
          label,
          rate: basisPoints / 100,
        })
      : label;

  const rows: DocumentAmountRow[] = [
    { label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.ROW_SUBTOTAL"), amount: money(amounts.grossSubtotalMinor) },
  ];

  if (amounts.automaticDiscountMinor) {
    rows.push({
      label: withRate(
        tSubscription("FINANCIAL_DOCUMENT_FORMAT.ROW_AUTOMATIC_DISCOUNT"),
        amounts.automaticDiscountBasisPoints,
      ),
      amount: money(-amounts.automaticDiscountMinor),
    });
  }

  if (amounts.quantityDiscountMinor) {
    rows.push({
      label: withRate(
        tSubscription("FINANCIAL_DOCUMENT_FORMAT.ROW_VOLUME_DISCOUNT"),
        amounts.quantityDiscountBasisPoints,
      ),
      amount: money(-amounts.quantityDiscountMinor),
    });
  }

  if (amounts.promotionalDiscountMinor) {
    rows.push({
      label: amounts.promotionCode
        ? tSubscription("FINANCIAL_DOCUMENT_FORMAT.ROW_PROMOTIONAL_DISCOUNT_CODE", {
            code: amounts.promotionCode,
          })
        : tSubscription("FINANCIAL_DOCUMENT_FORMAT.ROW_PROMOTIONAL_DISCOUNT"),
      amount: money(-amounts.promotionalDiscountMinor),
    });
  }

  rows.push({ label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.ROW_NET_SUBTOTAL"), amount: money(amounts.netSubtotalMinor) });
  rows.push({ label: describeTax(amounts), amount: money(amounts.taxAmountMinor) });

  if (amounts.creditAppliedMinor) {
    rows.push({
      label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.ROW_CREDIT_APPLIED"),
      amount: money(-amounts.creditAppliedMinor),
    });
  }

  rows.push({ label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.ROW_TOTAL"), amount: money(amounts.totalMinor), isTotal: true });

  return rows;
};

/**
 * The tax row's label.
 *
 * The mode is on it because the same rate means two different things — added to the net, or already
 * inside the price — and a subscriber checking the arithmetic needs to know which.
 */
export const describeTax = (amounts: FinancialDocumentAmounts): string => {
  if (!amounts.taxRateBasisPoints) {
    return tSubscription("FINANCIAL_DOCUMENT_FORMAT.TAX");
  }

  return tSubscription(
    amounts.taxMode === "Inclusive"
      ? "FINANCIAL_DOCUMENT_FORMAT.TAX_INCLUDED"
      : "FINANCIAL_DOCUMENT_FORMAT.TAX_ADDED",
    { rate: amounts.taxRateBasisPoints / 100 },
  );
};

/**
 * Whether the document's own figures add up.
 *
 * Rendered as a warning rather than hidden. A document whose parts do not reconcile is either a
 * defect in how it was issued or a row from before the breakdown was recorded, and both are things
 * somebody reading their invoice should be told rather than left to work out with a calculator.
 */
export const amountsReconcile = (amounts: FinancialDocumentAmounts): boolean => {
  const net =
    amounts.grossSubtotalMinor -
    amounts.automaticDiscountMinor -
    amounts.quantityDiscountMinor -
    amounts.promotionalDiscountMinor;

  const total = amounts.netSubtotalMinor + amounts.taxAmountMinor - amounts.creditAppliedMinor;

  return net === amounts.netSubtotalMinor && total === amounts.totalMinor;
};

/** One line of the settlement comparison, for the two-sided table a change needs. */
export interface SettlementRow {
  label: string;
  outgoing: string;
  target: string;
}

/**
 * The two sides of a mid-period change.
 *
 * A settlement is a subtraction, not a discounted price, so a single subtotal cannot explain one.
 * The subscriber asking why they were charged a part-month figure is asking about the period they
 * left and the period they joined.
 */
export const settlementRows = (
  settlement: NonNullable<SubscriptionFinancialDocument["settlement"]>,
  currencyCode: string,
): SettlementRow[] => {
  const money = (minor: number) => formatMoney(minor, currencyCode);

  return [
    {
      label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.SETTLEMENT_PERIOD_TOTAL"),
      outgoing: money(settlement.outgoing.grossAmountMinor),
      target: money(settlement.target.grossAmountMinor),
    },
    {
      label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.SETTLEMENT_BUILT_IN_DISCOUNTS"),
      outgoing: money(-settlement.outgoing.builtInDiscountMinor),
      target: money(-settlement.target.builtInDiscountMinor),
    },
    {
      label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.ROW_PROMOTIONAL_DISCOUNT"),
      outgoing: money(-settlement.outgoing.promotionalDiscountMinor),
      target: money(-settlement.target.promotionalDiscountMinor),
    },
    {
      label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.TAX"),
      outgoing: money(settlement.outgoing.taxAmountMinor),
      target: money(settlement.target.taxAmountMinor),
    },
    {
      label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.SETTLEMENT_FULL_PERIOD"),
      outgoing: money(settlement.outgoing.periodTotalMinor),
      target: money(settlement.target.periodTotalMinor),
    },
    {
      label: tSubscription("FINANCIAL_DOCUMENT_FORMAT.SETTLEMENT_COUNTED"),
      outgoing: money(settlement.outgoing.proratedValueMinor),
      target: money(settlement.target.proratedValueMinor),
    },
  ];
};

/**
 * What a trial invoice states, in one sentence.
 *
 * The card question is the part a subscriber actually cares about: whether the trial ends in a charge
 * they have already authorised or in a decision they still have to make.
 */
export const describeTrial = (
  trial: NonNullable<SubscriptionFinancialDocument["trial"]>,
): string => {
  const window = `${trial.startsAtUtc.slice(0, 10)} → ${trial.endsAtUtc.slice(0, 10)}`;
  const key = trial.requiresPaymentMethod
    ? "FINANCIAL_DOCUMENT_FORMAT.TRIAL_CARD"
    : "FINANCIAL_DOCUMENT_FORMAT.TRIAL_NO_CARD";
  const keyWithBilling = trial.requiresPaymentMethod
    ? "FINANCIAL_DOCUMENT_FORMAT.TRIAL_CARD_BILLING"
    : "FINANCIAL_DOCUMENT_FORMAT.TRIAL_NO_CARD_BILLING";

  return trial.firstBillingAtUtc
    ? tSubscription(keyWithBilling, {
        window,
        billing: trial.firstBillingAtUtc.slice(0, 10),
      })
    : tSubscription(key, { window });
};

/**
 * Which fields a billing profile is still missing, worded for a person.
 *
 * The server names the fields so a form can highlight the inputs; this is for the sentence beside
 * them, because "LegalName" is not a thing anybody types into a support ticket.
 */
const missingFieldLabel = (field: string): string => {
  switch (field) {
    case "LegalName":
      return tSubscription("FINANCIAL_DOCUMENT_FORMAT.MISSING_LEGAL_NAME");
    case "BillingContactName":
      return tSubscription("FINANCIAL_DOCUMENT_FORMAT.MISSING_CONTACT_NAME");
    case "BillingContactEmail":
      return tSubscription("FINANCIAL_DOCUMENT_FORMAT.MISSING_CONTACT_EMAIL");
    default:
      return field;
  }
};

export const describeMissingProfileFields = (missingFields: string[]): string => {
  if (missingFields.length === 0) {
    return "";
  }

  const listed = joinList(missingFields.map(missingFieldLabel));

  return tSubscription("FINANCIAL_DOCUMENT_FORMAT.ADD_MISSING_FIELDS", { fields: listed });
};
