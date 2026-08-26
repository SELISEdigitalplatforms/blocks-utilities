import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
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

/**
 * @param at
 * The address the page was reached at. Routed through `/app/:itemId/...` because that is the shape
 * the shell actually mounts, and the organization scope lives in its query string — a page rendered
 * at "/" cannot tell a link that keeps the scope from one that drops it.
 */
const renderPage = (at = "/app/project-1/subscription/billing-profile") =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={[at]}>
        <Routes>
          <Route
            path="/app/:itemId/subscription/billing-profile"
            element={<SubscriptionBillingProfilePage />}
          />
        </Routes>
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

  it("names the organization being edited, and its id", async () => {
    useBillingProfile.mockReturnValue({ data: profile(), isLoading: false, error: null });
    useUpdateBillingProfile.mockReturnValue(mutation());

    renderPage("/app/project-1/subscription/billing-profile?organizationId=org-1");

    // Every organization keeps its own profile, and the page used to show one without saying which.
    // The id as well as the name, because two organizations may well share a name.
    const scope = await screen.findByTestId("profile-scope");
    expect(scope).toHaveTextContent("Billing profile for Northwind");
    expect(scope).toHaveTextContent("org-1");
  });

  it("warns when the server answered about a different organization than the link asked for", async () => {
    useBillingProfile.mockReturnValue({
      data: profile({ organizationId: "org-1" }),
      isLoading: false,
      error: null,
    });
    useUpdateBillingProfile.mockReturnValue(mutation());

    renderPage("/app/project-1/subscription/billing-profile?organizationId=org-2");

    const mismatch = await screen.findByTestId("profile-scope-mismatch");
    expect(mismatch).toHaveTextContent("org-2");
    expect(mismatch).toHaveTextContent("org-1");

    // And no green light. Naming another organization is honoured for the platform console alone,
    // so "ready to be invoiced" here would be a promise about somebody else entirely.
    expect(screen.queryByTestId("profile-complete")).not.toBeInTheDocument();
  });

  it("keeps the organization on the way back to the catalogue", async () => {
    useBillingProfile.mockReturnValue({ data: profile(), isLoading: false, error: null });
    useUpdateBillingProfile.mockReturnValue(mutation());

    renderPage("/app/project-1/subscription/billing-profile?organizationId=org-1");

    // The old link pointed at /dashboard/subscription/plans, a route that does not exist, and
    // carried no organization if it had.
    expect(await screen.findByRole("link", { name: /Back to plans/ })).toHaveAttribute(
      "href",
      "/app/project-1/subscription/plans?organizationId=org-1",
    );
  });

  it("offers the way back to the same organizations catalogue after a save", async () => {
    const mutate = vi.fn((_request, options?: { onSuccess?: () => void }) =>
      options?.onSuccess?.(),
    );
    useBillingProfile.mockReturnValue({ data: profile(), isLoading: false, error: null });
    useUpdateBillingProfile.mockReturnValue(mutation({ mutate }));

    renderPage("/app/project-1/subscription/billing-profile?organizationId=org-1");
    await userEvent.click(
      await screen.findByRole("button", { name: "Save billing profile" }),
    );

    // Usually somebody is here because a subscription was refused for the want of these details.
    // Retrying it against a different organization would be refused for the same reason again.
    expect(await screen.findByRole("link", { name: /Continue to plans/ })).toHaveAttribute(
      "href",
      "/app/project-1/subscription/plans?organizationId=org-1",
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
