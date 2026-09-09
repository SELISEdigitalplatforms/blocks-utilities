import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { MeterUsage } from "../models/subscription-simulation.model";
import type { PlanMeter } from "../../subscription/models/subscription-plan.model";

vi.mock("../hooks/use-record-usage", () => ({
  useRecordUsage: () => ({ mutateAsync: vi.fn() }),
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
});
