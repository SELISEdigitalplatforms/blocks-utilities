import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { SubscriptionBillingProfile } from "../models/subscription-billing.model";

const { useBillingProfile, useUpdateBillingProfile } = vi.hoisted(() => ({
  useBillingProfile: vi.fn(),
  useUpdateBillingProfile: vi.fn(),
}));

vi.mock("../hooks/use-billing-profile", () => ({
  useBillingProfile,
  useUpdateBillingProfile,
}));

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({
    data: { organizations: [{ itemId: "org-1", name: "Northwind" }] },
    isError: false,
  }),
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

import { SubscriptionBillingProfilePage } from "./subscription-billing-profile-page";

const profile = (
  overrides: Partial<SubscriptionBillingProfile> = {},
): SubscriptionBillingProfile => ({
  organizationId: "org-1",
  legalName: "Northwind Trading AG",
  displayName: "Northwind",
  billingContactName: "Ada Byron",
  billingContactEmail: "ada@northwind.example",
  address: {
    line1: "1 Bahnhofstrasse",
    line2: null,
    city: "Zurich",
    region: null,
    postalCode: "8001",
    countryCode: "CH",
  },
  taxRegistrationId: "CHE-123.456.789",
  isComplete: true,
  missingFields: [],
  lastUpdatedDateUtc: "2026-08-25T10:00:00Z",
  ...overrides,
});

const mutation = (overrides: Record<string, unknown> = {}) => ({
  mutate: vi.fn(),
  isPending: false,
  error: null,
  ...overrides,
});

const renderPage = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <SubscriptionBillingProfilePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );

describe("billing profile page", () => {
  it("loads the stored profile into the form", async () => {
    useBillingProfile.mockReturnValue({ data: profile(), isLoading: false, error: null });
    useUpdateBillingProfile.mockReturnValue(mutation());

    renderPage();

    expect(await screen.findByDisplayValue("Northwind Trading AG")).toBeInTheDocument();
    expect(screen.getByDisplayValue("ada@northwind.example")).toBeInTheDocument();
    expect(screen.getByDisplayValue("CHE-123.456.789")).toBeInTheDocument();
  });

  it("says which fields are still needed before a paid subscription", async () => {
    useBillingProfile.mockReturnValue({
      data: profile({
        legalName: "",
        billingContactEmail: "",
        isComplete: false,
        missingFields: ["LegalName", "BillingContactEmail"],
      }),
      isLoading: false,
      error: null,
    });
    useUpdateBillingProfile.mockReturnValue(mutation());

    renderPage();

    // Said here rather than discovered as a validation error at checkout, which is the only moment
    // asking costs nothing.
    const banner = await screen.findByTestId("profile-incomplete");
    expect(banner).toHaveTextContent("legal name and billing contact email");
  });

  it("says plainly that editing does not rewrite issued invoices", async () => {
    useBillingProfile.mockReturnValue({ data: profile(), isLoading: false, error: null });
    useUpdateBillingProfile.mockReturnValue(mutation());

    renderPage();

    expect(await screen.findByTestId("profile-complete")).toHaveTextContent(
      /Invoices already sent keep the details they were issued with/,
    );
  });

  it("submits trimmed values and normalises the country code", async () => {
    const mutate = vi.fn();
    useBillingProfile.mockReturnValue({
      data: profile({ address: null, taxRegistrationId: null }),
      isLoading: false,
      error: null,
    });
    useUpdateBillingProfile.mockReturnValue(mutation({ mutate }));

    renderPage();

    const countryCode = await screen.findByLabelText("Country code");
    await userEvent.type(countryCode, "ch");
    await userEvent.click(screen.getByRole("button", { name: "Save billing profile" }));

    await waitFor(() => expect(mutate).toHaveBeenCalled());

    const [request] = mutate.mock.calls[0];
    expect(request.legalName).toBe("Northwind Trading AG");
    expect(request.address.countryCode).toBe("CH");
  });

  it("sends an address object even when every line is blank, so clearing it clears it", async () => {
    const mutate = vi.fn();
    useBillingProfile.mockReturnValue({
      data: profile({ address: null }),
      isLoading: false,
      error: null,
    });
    useUpdateBillingProfile.mockReturnValue(mutation({ mutate }));

    renderPage();
    await userEvent.click(
      await screen.findByRole("button", { name: "Save billing profile" }),
    );

    await waitFor(() => expect(mutate).toHaveBeenCalled());

    // Omitting it would leave whatever the server already held, which is not what somebody who
    // emptied the fields asked for.
    const [request] = mutate.mock.calls[0];
    expect(request.address).toEqual({
      line1: null,
      line2: null,
      city: null,
      region: null,
      postalCode: null,
      countryCode: null,
    });
  });

  it("shows the server's refusal rather than swallowing it", async () => {
    useBillingProfile.mockReturnValue({ data: profile(), isLoading: false, error: null });
    useUpdateBillingProfile.mockReturnValue(
      mutation({ error: new Error("The billing profile is invalid.") }),
    );

    renderPage();

    expect(await screen.findByTestId("profile-error")).toHaveTextContent(
      "The billing profile is invalid.",
    );
  });

  it("confirms a save so somebody knows the next invoice will use it", async () => {
    const mutate = vi.fn((_request, options?: { onSuccess?: () => void }) =>
      options?.onSuccess?.(),
    );
    useBillingProfile.mockReturnValue({ data: profile(), isLoading: false, error: null });
    useUpdateBillingProfile.mockReturnValue(mutation({ mutate }));

    renderPage();
    await userEvent.click(
      await screen.findByRole("button", { name: "Save billing profile" }),
    );

    expect(await screen.findByTestId("profile-saved")).toBeInTheDocument();
  });
});
