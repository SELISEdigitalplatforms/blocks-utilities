import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";

vi.mock("../hooks/use-billing-profile", () => ({
  useBillingProfile: () => ({ data: undefined, isLoading: false, error: null }),
  useUpdateBillingProfile: () => ({ mutate: vi.fn(), isPending: false, error: null }),
}));

import { SubscriptionBillingProfilePage } from "./subscription-billing-profile-page";

const renderPage = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={["/app/project-1/subscription/billing-profile"]}>
        <Routes>
          <Route
            path="/app/:itemId/subscription/billing-profile"
            element={<SubscriptionBillingProfilePage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );

describe("SubscriptionBillingProfilePage language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
    useI18nStore.setState({ remote: {} });
  });

  it("shows English by default", () => {
    renderPage();

    expect(screen.getByRole("heading", { name: "Billing profile" })).toBeInTheDocument();
  });

  it("shows German text when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.subscription.pages", {
      "BILLING_PROFILE_PAGE.TITLE": "Rechnungsprofil",
      "BILLING_PROFILE_PAGE.SAVE": "Rechnungsprofil speichern",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    renderPage();

    expect(screen.getByRole("heading", { name: "Rechnungsprofil" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Rechnungsprofil speichern" })).toBeInTheDocument();
  });
});
