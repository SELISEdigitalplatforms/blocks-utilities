import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { describe, expect, it, vi } from "vitest";

const { useSubscriptionPlans } = vi.hoisted(() => ({ useSubscriptionPlans: vi.fn() }));

vi.mock("../hooks/use-subscription-plans", () => ({ useSubscriptionPlans }));

vi.mock("../services/subscription.service", () => ({
  subscriptionService: {
    listDiscounts: vi.fn(async () => []),
    createDiscount: vi.fn(),
    updateDiscount: vi.fn(),
    archiveDiscount: vi.fn(),
  },
}));

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({ data: { organizations: [] }, isError: false }),
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

import { SubscriptionDiscountsPage } from "./subscription-discounts-page";

describe("SubscriptionDiscountsPage sticky prerequisites", () => {
  it("C7: <main> must not use overflow-hidden (would disable sticky descendants)", () => {
    useSubscriptionPlans.mockReturnValue({
      data: [],
      error: null,
      isError: false,
      isFetching: false,
      isLoading: false,
      refetch: vi.fn(),
    });

    render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter initialEntries={["/app/item-1/subscription/discounts"]}>
          <Routes>
            <Route path="/app/:itemId/subscription/discounts" element={<SubscriptionDiscountsPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const main = document.querySelector("main");
    expect(main).not.toBeNull();
    expect(main!.className).not.toContain("overflow-hidden");
  });
});
