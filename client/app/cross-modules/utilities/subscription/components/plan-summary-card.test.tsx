import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PlanSummaryCard, type PlanSummaryData } from "./plan-summary-card";

const plan = (overrides: Partial<PlanSummaryData> = {}): PlanSummaryData => ({
  displayName: "Personal",
  code: "personal",
  organizationLabel: "AmLora Test Org",
  trialDurationKind: null,
  trialDurationCount: null,
  trialRequiresPaymentMethod: true,
  quantityItems: [],
  meters: [],
  entitlements: [],
  prices: [],
  trialGrants: [],
  ...overrides,
});

const meter = (overrides: Partial<PlanSummaryData["meters"][number]> = {}) => ({
  meterKey: "ses-signatures",
  displayName: "Simple Signatures (SES)",
  unitLabel: "signature",
  includedQuantity: 150,
  overageAllowed: true,
  rateTables: [{ currencyCode: "CHF" }],
  ...overrides,
});

describe("PlanSummaryCard", () => {
  /**
   * The regression this guards. Keying these rows by a field the author is still typing left
   * duplicate/empty keys in the list, and React stranded the half-filled row: the card showed
   * both the abandoned "0 s included" version and the finished one for a single meter.
   */
  it("shows one row per meter after a half-filled row is completed", () => {
    const { rerender } = render(
      <PlanSummaryCard
        plan={plan({
          // Mid-typing: display name entered, meter key still blank.
          meters: [meter({ meterKey: "", unitLabel: "", includedQuantity: 0 })],
        })}
      />,
    );

    rerender(<PlanSummaryCard plan={plan({ meters: [meter()] })} />);

    expect(screen.getAllByText(/Simple Signatures \(SES\)/)).toHaveLength(1);
    expect(screen.queryByText(/0 s included/)).not.toBeInTheDocument();
  });

  /**
   * The exact sequence from the builder: two meters added before either key is typed, then
   * filled in one at a time. This card stays mounted across all of it, unlike the Review step
   * which mounts once at the end — which is why the two disagreed.
   */
  it("shows two rows after two blank-keyed meters are filled in one at a time", () => {
    const blank = (displayName: string) =>
      meter({ meterKey: "", displayName, unitLabel: "", includedQuantity: 0 });

    const { rerender } = render(
      <PlanSummaryCard
        plan={plan({ meters: [blank("Simple Signatures (SES)"), blank("Advanced Signatures (AES)")] })}
      />,
    );

    rerender(
      <PlanSummaryCard
        plan={plan({
          meters: [
            meter({ meterKey: "ses-signatures", displayName: "Simple Signatures (SES)" }),
            blank("Advanced Signatures (AES)"),
          ],
        })}
      />,
    );

    rerender(
      <PlanSummaryCard
        plan={plan({
          meters: [
            meter({ meterKey: "ses-signatures", displayName: "Simple Signatures (SES)" }),
            meter({
              meterKey: "aes-signatures",
              displayName: "Advanced Signatures (AES)",
              includedQuantity: 2,
            }),
          ],
        })}
      />,
    );

    expect(screen.getAllByText(/Simple Signatures \(SES\)/)).toHaveLength(1);
    expect(screen.getAllByText(/Advanced Signatures \(AES\)/)).toHaveLength(1);
    expect(screen.queryByText(/0 s included/)).not.toBeInTheDocument();
  });

  it("keeps two meters distinct while both keys are still blank", () => {
    render(
      <PlanSummaryCard
        plan={plan({
          meters: [
            meter({ meterKey: "", displayName: "Simple Signatures (SES)" }),
            meter({ meterKey: "", displayName: "Advanced Signatures (AES)" }),
          ],
        })}
      />,
    );

    expect(screen.getByText(/Simple Signatures \(SES\)/)).toBeInTheDocument();
    expect(screen.getByText(/Advanced Signatures \(AES\)/)).toBeInTheDocument();
  });

  it("renders quantity items and entitlements that have no key yet", () => {
    render(
      <PlanSummaryCard
        plan={plan({
          quantityItems: [
            { itemKey: "", unitLabel: "user", defaultQuantity: 1, maxQuantity: null },
            { itemKey: "", unitLabel: "workspace", defaultQuantity: 3, maxQuantity: null },
          ],
          entitlements: [
            { key: "", limitKind: "Boolean", limit: null, unitLabel: null },
            { key: "shared-templates", limitKind: "Boolean", limit: null, unitLabel: null },
          ],
        })}
      />,
    );

    expect(screen.getByText(/1 user included by default/)).toBeInTheDocument();
    expect(screen.getByText(/3 workspaces included by default/)).toBeInTheDocument();
    expect(screen.getByText("shared-templates:")).toBeInTheDocument();
  });

  it("shows the ceiling on a quantity item that has one", () => {
    // The cap is part of what the plan sells, and the one quantity rule that refuses a
    // subscription outright rather than just costing more — a buyer comparing tiers has to
    // see it.
    render(
      <PlanSummaryCard
        plan={plan({
          quantityItems: [
            {
              itemKey: "team-members",
              unitLabel: "team member",
              defaultQuantity: 1,
              maxQuantity: 20,
            },
          ],
        })}
      />,
    );

    expect(
      screen.getByText(/1 team member included by default, up to 20/),
    ).toBeInTheDocument();
  });

  it("describes a meter's allowance and what happens past it", () => {
    render(<PlanSummaryCard plan={plan({ meters: [meter()] })} />);

    expect(
      screen.getByText(/150 signatures included, then overage billed/),
    ).toBeInTheDocument();
  });

  it("says what a trial actually includes, which is not the plan's own allowance", () => {
    render(
      <PlanSummaryCard
        plan={plan({
          trialDurationKind: "Days",
          trialDurationCount: 1,
          meters: [meter()],
          trialGrants: [{ meterKey: "ses-signatures", includedQuantity: 5 }],
        })}
      />,
    );

    expect(screen.getByText("During the trial")).toBeInTheDocument();
    expect(
      screen.getByText(/5 signatures, instead of the usual 150/),
    ).toBeInTheDocument();
  });

  it("warns that a meter with no grant gives a whole month away during the trial", () => {
    render(
      <PlanSummaryCard
        plan={plan({ trialDurationKind: "Days", trialDurationCount: 14, meters: [meter()] })}
      />,
    );

    expect(
      screen.getByText(/150 signatures — the full monthly allowance, with no separate trial limit/),
    ).toBeInTheDocument();
  });

  it("says nothing about trial allowances on a plan that measures nothing", () => {
    render(
      <PlanSummaryCard plan={plan({ trialDurationKind: "Days", trialDurationCount: 14 })} />,
    );

    expect(screen.queryByText(/During the/)).not.toBeInTheDocument();
  });
});

describe("volume bands", () => {
  const banded = {
    itemKey: "user",
    unitLabel: "user",
    defaultQuantity: 1,
    maxQuantity: null,
    quantityDiscountTiers: [
      { minimumQuantity: 1, maximumQuantity: 4, discountBasisPoints: 0 },
      { minimumQuantity: 5, maximumQuantity: 9, discountBasisPoints: 500 },
      { minimumQuantity: 10, maximumQuantity: null, discountBasisPoints: 2000 },
    ],
  };

  it("reads out every band a buyer would be charged under", () => {
    // A band list authored through the API used to be invisible on every screen describing the
    // plan, so nobody reviewing a plan could see what it actually charged at ten users.
    render(<PlanSummaryCard plan={plan({ quantityItems: [banded] })} />);

    expect(screen.getByText("1–4 users — no discount")).toBeInTheDocument();
    expect(screen.getByText("5–9 users — 5% off")).toBeInTheDocument();
    expect(screen.getByText("10+ users — 20% off")).toBeInTheDocument();
  });

  it("says nothing about bands on an item that has none", () => {
    render(
      <PlanSummaryCard
        plan={plan({
          quantityItems: [{ itemKey: "user", unitLabel: "user", defaultQuantity: 1, maxQuantity: null }],
        })}
      />,
    );

    expect(screen.queryByText(/off$/)).not.toBeInTheDocument();
  });
});
