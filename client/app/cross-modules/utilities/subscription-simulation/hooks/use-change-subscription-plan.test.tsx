import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

vi.mock("../services/subscription-simulation.service", () => ({
  subscriptionSimulationService: { changePlan: vi.fn().mockResolvedValue({ subscriptionId: "sub-1" }) },
}));

import { useChangeSubscriptionPlan } from "./use-change-subscription-plan";

describe("useChangeSubscriptionPlan", () => {
  /**
   * Found on dev: after a change the Members card kept the old period's figures until a reload,
   * because only the subscription was re-read and every place had opened a fresh window.
   */
  it("re-reads the places and the usage as well as the subscription", async () => {
    const client = new QueryClient();
    const invalidate = vi.spyOn(client, "invalidateQueries");
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );

    const { result } = renderHook(() => useChangeSubscriptionPlan(), { wrapper });
    result.current.mutate({
      subscriptionId: "sub-1",
      request: { planCode: "p", priceId: "price-1", quantities: [] },
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const keys = invalidate.mock.calls.map(([filters]) => filters?.queryKey?.[0]);
    expect(keys).toEqual(
      expect.arrayContaining([
        "subscription-simulation-current",
        "subscription-simulation-members",
        "subscription-usage",
      ]),
    );
  });
});
