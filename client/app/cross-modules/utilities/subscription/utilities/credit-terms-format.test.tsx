import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PlanSummaryCard, type PlanSummaryData } from "../components/plan-summary-card";
import { describeCreditTerms, type CreditTermsSummary } from "./credit-terms-format";

/**
 * Credit terms in the plan preview, the Review step and the plan detail page (spec 002).
 *
 * Guards against a reviewer approving a plan without seeing what its credits cost, how many can be
 * bought at once and when they expire — or reading a price for paused credits nobody can buy as an
 * offer that is live.
 */
const credit = (overrides: Partial<CreditTermsSummary> = {}): CreditTermsSummary => ({
  meterKey: "ai-credit",
  enabled: true,
  unitPrices: [{ currencyCode: "USD", unitAmountMinor: 10 }],
  minQuantity: 10,
  maxQuantity: 1_000,
  expiryPolicy: 2,
  taxRateBasisPoints: null,
  taxMode: null,
  ...overrides,
});

const plan = (credits: CreditTermsSummary[]): PlanSummaryData => ({
  displayName: "Pro",
  code: "pro",
  organizationLabel: "Tenant-wide",
  trialDurationKind: null,
  trialDurationCount: null,
  trialRequiresPaymentMethod: true,
  quantityItems: [],
  meters: [
    {
      meterKey: "ai-credit",
      displayName: "AI",
      unitLabel: "credit",
      includedQuantity: 100,
      overageAllowed: false,
    },
  ],
  entitlements: [],
  prices: [],
  trialGrants: [],
  credits,
});

describe("describeCreditTerms", () => {
  it("states the price, the purchase bounds and the expiry", () => {
    expect(describeCreditTerms(credit())).toBe(
      "$0.10 per credit · 10–1,000 per purchase · credits carry forward until the plan changes",
    );
  });

  it("reads a stored plan's expiry name the same as the form's number", () => {
    expect(describeCreditTerms(credit({ expiryPolicy: "Never" }))).toContain("credits never expire");
    expect(describeCreditTerms(credit({ expiryPolicy: 1 }))).toContain("credits never expire");
  });

  it("says whether the tax is inside the price or added to it", () => {
    expect(describeCreditTerms(credit({ taxRateBasisPoints: 810, taxMode: "Inclusive" }))).toContain(
      "8.1% tax included",
    );
    expect(describeCreditTerms(credit({ taxRateBasisPoints: 810, taxMode: "Exclusive" }))).toContain(
      "8.1% tax added",
    );
  });

  it("leads with paused, so a reviewer cannot read it as a live offer", () => {
    expect(describeCreditTerms(credit({ enabled: false }))).toMatch(/^Paused · /);
  });

  it("names a missing maximum while the author is still filling it in", () => {
    expect(describeCreditTerms(credit({ maxQuantity: null }))).toContain("no maximum set");
  });
});

describe("the plan summary", () => {
  it("lists each meter's credit terms under its own name", () => {
    render(<PlanSummaryCard plan={plan([credit()])} />);

    expect(screen.getByText("Extra credits")).toBeInTheDocument();
    // The meter's own allowance row also starts "AI:", so the credits line is found by its terms.
    expect(screen.getByText(/\$0\.10 per credit/).closest("p")).toHaveTextContent(/^AI: \$0\.10 per credit/);
  });

  it("shows no credits section for a plan that sells none", () => {
    render(<PlanSummaryCard plan={plan([])} />);

    expect(screen.queryByText("Extra credits")).not.toBeInTheDocument();
  });
});
