import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PlanSummaryCard, type PlanSummaryData } from "./plan-summary-card";

const plan = (overrides: Partial<PlanSummaryData> = {}): PlanSummaryData => ({
  displayName: "Personal",
  code: "personal",
  organizationLabel: "AmLora Test Org",
  trialDays: null,
  trialRequiresPaymentMethod: true,
  quantityItems: [],
  meters: [],
  entitlements: [],
  prices: [],
  ...overrides,
});

const meter = (overrides: Partial<PlanSummaryData["meters"][number]> = {}) => ({
  meterKey: "ses-signatures",
  displayName: "Simple Signatures (SES)",
  unitLabel: "signature",
  includedQuantity: 150,
  overageAllowed: true,
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
            { itemKey: "", unitLabel: "user", defaultQuantity: 1 },
            { itemKey: "", unitLabel: "workspace", defaultQuantity: 3 },
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

  it("describes a meter's allowance and what happens past it", () => {
    render(<PlanSummaryCard plan={plan({ meters: [meter()] })} />);

    expect(
      screen.getByText(/150 signatures included, then overage billed/),
    ).toBeInTheDocument();
  });
});
