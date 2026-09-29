import { describe, expect, it } from "vitest";
import type { PendingPlanChange } from "../models/subscription-simulation.model";
import { describePendingMove } from "./plan-change-label";

const pending = (targetPlanCode: string, interval: PendingPlanChange["interval"]): PendingPlanChange => ({
  targetPlanCode,
  targetPlanName: targetPlanCode === "pro" ? "Pro" : "Team",
  targetPriceId: "price-1",
  interval,
  intervalCount: 1,
  quantities: [],
  requestedAtUtc: "2026-09-29T00:00:00Z",
  effectiveAtUtc: "2027-09-29T00:00:00Z",
});

describe("describePendingMove", () => {
  it("names the plan a change moves to", () => {
    expect(describePendingMove({ planCode: "pro" }, pending("team", "Month"))).toBe("Team");
  });

  /** Found on dev: a yearly-to-monthly switch on Pro read "Moving to Pro". */
  it("names the cadence when the plan stays the same", () => {
    expect(describePendingMove({ planCode: "pro" }, pending("pro", "Month"))).toBe("billing every month");
  });
});
