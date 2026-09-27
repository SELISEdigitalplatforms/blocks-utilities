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

  /**
   * Three outcomes, not two. A use past a reporting pace is allowed, so shown as plain success the
   * whole throttle behaviour would be invisible and the cap would appear to do nothing.
   */
  describe("against a meter with a pace", () => {
    const paced = { ...meter, subLimitWindow: "Hour", subLimitQuantity: 10 } as PlanMeter;

    const consume = (quantity: string) => {
      render(
        <UsageMeterRow meter={paced} entitlementKey={undefined} usage={usage} organizationId={undefined} />,
      );
      fireEvent.change(screen.getByLabelText("Quantity to consume for Screenings"), {
        target: { value: quantity },
      });
      fireEvent.click(screen.getByRole("button", { name: "Consume" }));
    };

    it("reads as fine within the pace", async () => {
      recordUsage.mockResolvedValue({ ...usage, used: 43, remaining: 107, subLimitExceeded: false });
      consume("1");

      expect(await screen.findByText(/^Recorded\./)).toBeInTheDocument();
      expect(screen.queryByText("Over pace")).not.toBeInTheDocument();
    });

    it("reads as over pace when the use was allowed but past it", async () => {
      recordUsage.mockResolvedValue({ ...usage, used: 54, remaining: 96, subLimitExceeded: true });
      consume("12");

      expect(await screen.findByText("Over pace")).toBeInTheDocument();
      expect(screen.getByText(/Past the pace of 10 per hour/)).toBeInTheDocument();
    });

    it("reads as refused by the pace, not the allowance, when allowance remains", async () => {
      recordUsage.mockResolvedValue({ ...usage, allowed: false });
      consume("12");

      expect(await screen.findByText(/Refused by the pace limit of 10 per hour/)).toBeInTheDocument();
      expect(screen.queryByText("Over pace")).not.toBeInTheDocument();
    });
  });
});
