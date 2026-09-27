import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import type { MeterUsage } from "../models/subscription-simulation.model";

const getCurrentUsage = vi.hoisted(() => vi.fn());
const getMyUsage = vi.hoisted(() => vi.fn());

vi.mock("../services/subscription-simulation.service", () => ({
  subscriptionSimulationService: { getCurrentUsage, getMyUsage },
}));

vi.mock("../hooks/use-record-usage", () => ({
  useRecordUsage: () => ({ mutateAsync: vi.fn() }),
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

import { UsageSection } from "./usage-section";

const tokens = {
  meterKey: "tokens",
  displayName: "Tokens",
  unitLabel: "token",
  aggregation: "Sum",
  includedQuantity: 1000,
  overageAllowed: false,
  thresholdPercents: [],
};

const plan = (code: string): SubscriptionPlan =>
  ({ code, meters: [tokens], entitlements: [] }) as unknown as SubscriptionPlan;

const reading = (used: number): MeterUsage[] => [
  {
    allowed: true,
    meterKey: "tokens",
    unitLabel: "token",
    periodKey: "2026-09",
    periodStartUtc: "2026-09-01T00:00:00Z",
    periodEndUtc: "2026-10-01T00:00:00Z",
    included: 1000,
    used,
    remaining: 1000 - used,
    overage: 0,
    replayed: false,
  },
];

describe("UsageSection", () => {
  beforeEach(() => {
    getCurrentUsage.mockReset().mockResolvedValue(reading(700));
    getMyUsage.mockReset().mockResolvedValue(reading(30));
  });

  /**
   * The two reads share a response shape. Under one cache key they would overwrite each other and
   * the toggle would show whichever landed last, labelled as the other.
   */
  it("switches between the organization's figures and the caller's own, each from its own read", async () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <UsageSection plan={plan("org")} memberPlans={[plan("ai")]} organizationId="org-1" />
      </QueryClientProvider>,
    );

    expect(await screen.findByText(/700\/1000 tokens used/)).toBeInTheDocument();
    expect(getMyUsage).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Mine" }));

    expect(await screen.findByText(/30\/1000 tokens used/)).toBeInTheDocument();
    expect(screen.getByText("GET /api/subscription-usage/mine")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Organization" }));

    await waitFor(() => expect(screen.getByText(/700\/1000 tokens used/)).toBeInTheDocument());
    expect(getCurrentUsage).toHaveBeenCalledWith("org-1");
    expect(getMyUsage).toHaveBeenCalledWith("org-1");
  });

  it("offers no toggle, and shows the caller's own, when only places exist", async () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <UsageSection plan={undefined} memberPlans={[plan("ai")]} organizationId={undefined} />
      </QueryClientProvider>,
    );

    expect(await screen.findByText(/30\/1000 tokens used/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Mine" })).not.toBeInTheDocument();
    expect(getCurrentUsage).not.toHaveBeenCalled();
  });
});
