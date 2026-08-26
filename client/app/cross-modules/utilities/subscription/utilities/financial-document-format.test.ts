import { describe, expect, it } from "vitest";

import type {
  FinancialDocumentAmounts,
  SubscriptionFinancialDocument,
} from "../models/subscription-billing.model";
import {
  amountsReconcile,
  describeDocumentStatus,
  describeDocumentType,
  describeMissingProfileFields,
  describePeriod,
  describeTax,
  describeTrial,
  documentAmountRows,
  settlementRows,
} from "./financial-document-format";

/**
 * Currency formatting puts a non-breaking space between the code and the number, which no test
 * should have to spell.
 */
const plain = (value: string) => value.replace(/\u00a0/g, " ");

const amounts = (overrides: Partial<FinancialDocumentAmounts> = {}): FinancialDocumentAmounts => ({
  grossSubtotalMinor: 100_000,
  automaticDiscountMinor: 0,
  quantityDiscountMinor: 0,
  promotionalDiscountMinor: 0,
  netSubtotalMinor: 100_000,
  taxRateBasisPoints: null,
  taxMode: null,
  taxAmountMinor: 0,
  creditAppliedMinor: 0,
  totalMinor: 100_000,
  ...overrides,
});

describe("document amount rows", () => {
  it("gives every discount source that gave something its own row", () => {
    // Three promises to the subscriber, three rows. One combined "discount" figure cannot be read
    // back into which of them they actually got.
    const rows = documentAmountRows(
      amounts({
        automaticDiscountMinor: 8_000,
        automaticDiscountBasisPoints: 800,
        quantityDiscountMinor: 5_000,
        quantityDiscountBasisPoints: 500,
        promotionalDiscountMinor: 2_000,
        promotionCode: "launch20",
        netSubtotalMinor: 85_000,
        totalMinor: 85_000,
      }),
      "CHF",
    );

    expect(rows.map((row) => row.label)).toEqual([
      "Subtotal",
      "Automatic price discount (8%)",
      "Volume discount (5%)",
      "Promotional discount (launch20)",
      "Net subtotal",
      "Tax",
      "Total",
    ]);
  });

  it("omits a discount source that gave nothing", () => {
    // Rendering "Volume discount CHF 0.00" invites the subscriber to ask what it means.
    const labels = documentAmountRows(amounts(), "CHF").map((row) => row.label);

    expect(labels).not.toContain("Volume discount");
    expect(labels).not.toContain("Promotional discount");
  });

  it("shows discounts and credit as negative amounts", () => {
    const rows = documentAmountRows(
      amounts({
        automaticDiscountMinor: 8_000,
        netSubtotalMinor: 92_000,
        creditAppliedMinor: 1_000,
        totalMinor: 91_000,
      }),
      "CHF",
    );

    expect(plain(rows[1].amount)).toContain("-");
    expect(plain(rows.find((row) => row.label.includes("credit"))!.amount)).toContain("-");
  });

  it("puts credit below tax, because it pays a bill rather than reducing one", () => {
    const labels = documentAmountRows(
      amounts({
        taxRateBasisPoints: 770,
        taxAmountMinor: 7_700,
        creditAppliedMinor: 1_000,
        totalMinor: 106_700,
      }),
      "CHF",
    ).map((row) => row.label);

    expect(labels.indexOf("Account credit applied")).toBeGreaterThan(
      labels.findIndex((label) => label.startsWith("Tax")),
    );
  });

  it("marks only the total as the total", () => {
    const rows = documentAmountRows(amounts(), "CHF");

    expect(rows.filter((row) => row.isTotal)).toHaveLength(1);
    expect(rows[rows.length - 1].label).toBe("Total");
  });
});

describe("tax label", () => {
  it("says whether the rate was added or already inside the price", () => {
    // The same rate means two different things, and a subscriber checking the arithmetic needs to
    // know which.
    expect(describeTax(amounts({ taxRateBasisPoints: 770, taxMode: "Exclusive" })))
      .toBe("Tax (7.7%, added)");
    expect(describeTax(amounts({ taxRateBasisPoints: 770, taxMode: "Inclusive" })))
      .toBe("Tax (7.7%, included)");
  });

  it("says nothing about a rate that does not exist", () => {
    expect(describeTax(amounts())).toBe("Tax");
  });
});

describe("reconciliation", () => {
  it("accepts a document whose parts add up", () => {
    expect(
      amountsReconcile(
        amounts({
          automaticDiscountMinor: 8_000,
          promotionalDiscountMinor: 2_000,
          netSubtotalMinor: 90_000,
          taxAmountMinor: 6_930,
          creditAppliedMinor: 1_000,
          totalMinor: 95_930,
        }),
      ),
    ).toBe(true);
  });

  it("rejects one whose gross and discounts do not reach its net", () => {
    // What a pre-breakdown charge looks like: a total that is right and components that are not.
    expect(
      amountsReconcile(amounts({ automaticDiscountMinor: 8_000, netSubtotalMinor: 100_000 })),
    ).toBe(false);
  });

  it("rejects one whose net and tax do not reach its total", () => {
    expect(amountsReconcile(amounts({ taxAmountMinor: 7_700 }))).toBe(false);
  });
});

describe("document type and status", () => {
  it("names each type the way somebody reading a list would", () => {
    expect(describeDocumentType("Invoice")).toBe("Invoice");
    expect(describeDocumentType("TrialInvoice")).toBe("Trial invoice");
    expect(describeDocumentType("CreditNote")).toBe("Credit note");
  });

  it("never calls a trial invoice or a credit note paid", () => {
    // One was never charged and the other went the other way. "Paid" on either is wrong in a way
    // somebody would eventually act on.
    expect(describeDocumentStatus({ documentType: "TrialInvoice", status: "Issued" }))
      .toBe("No payment due");
    expect(describeDocumentStatus({ documentType: "CreditNote", status: "Issued" }))
      .toBe("Credited");
  });

  it("reports a refund against the invoice it came off", () => {
    expect(describeDocumentStatus({ documentType: "Invoice", status: "Issued" })).toBe("Paid");
    expect(describeDocumentStatus({ documentType: "Invoice", status: "PartiallyRefunded" }))
      .toBe("Partially refunded");
    expect(describeDocumentStatus({ documentType: "Invoice", status: "Refunded" }))
      .toBe("Refunded in full");
  });
});

describe("period", () => {
  it("prefers the local dates the document was issued with", () => {
    // Formatted in the subscriber's zone when the document was issued. Recomputing it in the
    // reader's browser would give a third answer for a period that ends at midnight UTC.
    expect(
      describePeriod({
        periodLocalStart: "2026-01-01",
        periodLocalEnd: "2027-01-01",
        periodStartUtc: "2025-12-31T23:00:00Z",
        periodEndUtc: "2026-12-31T23:00:00Z",
        timeZoneId: "Pacific/Auckland",
      }),
    ).toBe("2026-01-01 → 2027-01-01 (Pacific/Auckland)");
  });

  it("falls back to the UTC dates when a document carries no local ones", () => {
    expect(
      describePeriod({
        periodLocalStart: null,
        periodLocalEnd: null,
        periodStartUtc: "2026-01-01T00:00:00Z",
        periodEndUtc: "2027-01-01T00:00:00Z",
        timeZoneId: "UTC",
      }),
    ).toBe("2026-01-01 → 2027-01-01");
  });

  it("says nothing rather than guessing when there is no period at all", () => {
    expect(
      describePeriod({
        periodLocalStart: null,
        periodLocalEnd: null,
        periodStartUtc: null,
        periodEndUtc: null,
        timeZoneId: null,
      }),
    ).toBe("—");
  });
});

describe("settlement rows", () => {
  it("states both sides of a mid-period change", () => {
    const settlement: NonNullable<SubscriptionFinancialDocument["settlement"]> = {
      outgoing: {
        grossAmountMinor: 10_000,
        builtInDiscountMinor: 1_000,
        promotionalDiscountMinor: 500,
        taxAmountMinor: 850,
        periodTotalMinor: 9_350,
        proratedValueMinor: 4_670,
      },
      target: {
        grossAmountMinor: 20_000,
        builtInDiscountMinor: 1_600,
        promotionalDiscountMinor: 0,
        taxAmountMinor: 1_840,
        periodTotalMinor: 20_240,
        proratedValueMinor: 10_120,
      },
      creditConsumedMinor: 1_000,
      netSettlementMinor: 4_450,
    };

    const rows = settlementRows(settlement, "CHF");

    // A settlement is a subtraction, not a discounted price. Both columns, or the subscriber cannot
    // check it.
    expect(rows.map((row) => row.label)).toEqual([
      "Period total before discounts",
      "Automatic and volume discounts",
      "Promotional discount",
      "Tax",
      "Full period",
      "Counted in this settlement",
    ]);
    expect(plain(rows[1].outgoing)).toContain("-");
    expect(plain(rows[5].target)).toContain("101.20");
  });
});

describe("trial", () => {
  it("says whether a card was taken, which is what the subscriber cares about", () => {
    expect(
      describeTrial({
        startsAtUtc: "2026-08-01T00:00:00Z",
        endsAtUtc: "2026-08-15T00:00:00Z",
        requiresPaymentMethod: false,
        firstBillingAtUtc: "2026-08-15T00:00:00Z",
      }),
    ).toBe(
      "Trial 2026-08-01 → 2026-08-15 — no payment method was required, " +
        "first billing expected 2026-08-15. Nothing was charged.",
    );

    expect(
      describeTrial({
        startsAtUtc: "2026-08-01T00:00:00Z",
        endsAtUtc: "2026-08-15T00:00:00Z",
        requiresPaymentMethod: true,
        firstBillingAtUtc: null,
      }),
    ).toContain("a payment method was taken up front");
  });
});

describe("missing profile fields", () => {
  it("words the fields the way a person would say them", () => {
    // "LegalName" is not something anybody types into a support ticket.
    expect(describeMissingProfileFields(["LegalName"]))
      .toBe("Add the legal name before starting a paid subscription.");
    expect(describeMissingProfileFields(["BillingContactName", "BillingContactEmail"]))
      .toBe(
        "Add the billing contact name and billing contact email before starting a paid subscription.",
      );
    expect(
      describeMissingProfileFields(["LegalName", "BillingContactName", "BillingContactEmail"]),
    ).toContain("legal name, billing contact name and billing contact email");
  });

  it("says nothing when nothing is missing", () => {
    expect(describeMissingProfileFields([])).toBe("");
  });

  it("passes through a field name it does not recognise rather than dropping it", () => {
    // A server that starts requiring a new field must not silently produce an empty sentence.
    expect(describeMissingProfileFields(["SomethingNew"])).toContain("SomethingNew");
  });
});
