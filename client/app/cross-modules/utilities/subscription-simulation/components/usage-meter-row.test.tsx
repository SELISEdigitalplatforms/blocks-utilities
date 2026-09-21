import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { MeterUsage } from "../models/subscription-simulation.model";
import type { PlanMeter } from "../../subscription/models/subscription-plan.model";

const recordUsage = vi.hoisted(() => vi.fn());
const getEntitlement = vi.hoisted(() => vi.fn());

vi.mock("../hooks/use-record-usage", () => ({
  useRecordUsage: () => ({ mutateAsync: recordUsage }),
}));

vi.mock("../services/subscription-simulation.service", () => ({
  subscriptionSimulationService: { getEntitlement },
}));

import { UsageMeterRow } from "./usage-meter-row";

const meter = {
  meterKey: "screening",
  displayName: "Screenings",
  unitLabel: "screening",
  aggregation: "Sum",
  includedQuantity: 150,
  overageAllowed: true,
  thresholdPercents: [],
} as PlanMeter;

const usage: MeterUsage = {
  allowed: true,
  meterKey: "screening",
  unitLabel: "screening",
  periodKey: "2026-08",
  periodStartUtc: "2026-08-01T00:00:00Z",
  periodEndUtc: "2026-09-01T00:00:00Z",
  included: 150,
  used: 42,
  remaining: 108,
  overage: 0,
  replayed: false,
};

describe("UsageMeterRow", () => {
  it("shows the figures from the current-usage read, not the plan's included quantity", () => {
    render(
      <UsageMeterRow
        meter={meter}
        entitlementKey="screening.limit"
        usage={usage}
        organizationId={undefined}
      />,
    );

    expect(screen.getByText(/42\/150 screenings used this period/)).toBeInTheDocument();
  });

  it("falls back to naming the entitlement when the meter has no usage row yet", () => {
    render(
      <UsageMeterRow
        meter={meter}
        entitlementKey="screening.limit"
        usage={undefined}
        organizationId={undefined}
      />,
    );

    expect(screen.getByText("Entitlement: screening.limit")).toBeInTheDocument();
  });

  it("records past the limit when the meter bills overage", async () => {
    getEntitlement.mockResolvedValue({
      key: "screening.limit",
      allowed: true,
      reason: "Allowed",
      limitKind: "Count",
      limit: 150,
      used: 150,
      remaining: 0,
      overageAllowed: true,
      unitLabel: null,
    });
    recordUsage.mockResolvedValue({ ...usage, used: 200, remaining: 0, overage: 50 });

    render(
      <UsageMeterRow
        meter={meter}
        entitlementKey="screening.limit"
        usage={{ ...usage, used: 150, remaining: 0 }}
        organizationId={undefined}
      />,
    );

    fireEvent.change(screen.getByLabelText("Quantity to consume for Screenings"), {
      target: { value: "50" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Consume" }));

    await waitFor(() => expect(recordUsage).toHaveBeenCalledTimes(1));
    expect(screen.queryByText(/Blocked before recording/)).not.toBeInTheDocument();
  });
});
