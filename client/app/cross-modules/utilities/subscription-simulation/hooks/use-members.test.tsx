import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

const listMembers = vi.hoisted(() => vi.fn());

vi.mock("../services/subscription-simulation.service", () => ({
  subscriptionSimulationService: { listMembers },
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

import { useSubscriptionsWithMyPlace } from "./use-members";

const seat = (userId: string, seatNumber: number) => ({
  subscriptionId: "",
  userId,
  seatNumber,
  assignedAtUtc: "",
  releasedAtUtc: null,
});

const wrapper = ({ children }: { children: ReactNode }) => (
  <QueryClientProvider client={new QueryClient()}>{children}</QueryClientProvider>
);

describe("useSubscriptionsWithMyPlace", () => {
  /**
   * Found testing in the portal: the usage view read limits and an entitlement from a user-wise
   * plan the tester held no place on, and every use was blocked as "subscription not active".
   */
  it("keeps only the subscriptions this user holds a place on", async () => {
    listMembers.mockImplementation(async (subscriptionId: string) => ({
      subscriptionId,
      purchased: 3,
      held: 1,
      available: 2,
      seats: subscriptionId === "mine" ? [seat("user-1", 1)] : [seat("someone-else", 1)],
    }));

    const { result } = renderHook(
      () =>
        useSubscriptionsWithMyPlace(
          [{ subscriptionId: "theirs" }, { subscriptionId: "mine" }],
          "user-1",
        ),
      { wrapper },
    );

    await waitFor(() => expect(result.current).toEqual([{ subscriptionId: "mine" }]));
  });

  it("keeps none while nobody is signed in", () => {
    const { result } = renderHook(
      () => useSubscriptionsWithMyPlace([{ subscriptionId: "mine" }], undefined),
      { wrapper },
    );

    expect(result.current).toEqual([]);
  });
});
