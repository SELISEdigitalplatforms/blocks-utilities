import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { SubscriptionMerchantProfile } from "../models/subscription-billing.model";

const { useMerchantProfile, useUpdateMerchantProfile } = vi.hoisted(() => ({
  useMerchantProfile: vi.fn(),
  useUpdateMerchantProfile: vi.fn(),
}));

vi.mock("../hooks/use-merchant-profile", () => ({
  useMerchantProfile,
  useUpdateMerchantProfile,
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

import { SubscriptionMerchantProfilePage } from "./subscription-merchant-profile-page";

const profile = (
  overrides: Partial<SubscriptionMerchantProfile> = {},
): SubscriptionMerchantProfile => ({
  legalName: "Northwind Software GmbH",
  displayName: "Northwind",
  address: {
    line1: "1 Bahnhofstrasse",
    line2: null,
    city: "Zurich",
    region: null,
    postalCode: "8001",
    countryCode: "CH",
  },
  taxRegistrationId: "CHE-123.456.789",
  supportEmail: "billing@northwind.example",
  paymentInstructions: "IBAN CH00 1234",
  isComplete: true,
  missingFields: [],
  isInheritedFromConfiguration: false,
  lastUpdatedDateUtc: "2026-08-25T10:00:00Z",
  paymentProviderName: "STRIPE",
  paymentProviderStatus: "Ready",
  paymentProviders: [
    { name: "STRIPE", status: "Ready" },
    { name: "ADYEN-ONLINE", status: "NotConfigured" },
  ],
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
        <SubscriptionMerchantProfilePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );

describe("merchant profile page", () => {
  it("loads the tenant's own selling identity into the form", async () => {
    useMerchantProfile.mockReturnValue({ data: profile(), isLoading: false, error: null });
    useUpdateMerchantProfile.mockReturnValue(mutation());

    renderPage();

    expect(await screen.findByDisplayValue("Northwind Software GmbH")).toBeInTheDocument();
    expect(screen.getByDisplayValue("CHE-123.456.789")).toBeInTheDocument();
    expect(screen.getByDisplayValue("IBAN CH00 1234")).toBeInTheDocument();
    expect(await screen.findByTestId("merchant-own")).toBeInTheDocument();
  });

  it("warns that a configured identity is shared with every other tenant", async () => {
    useMerchantProfile.mockReturnValue({
      data: profile({ isInheritedFromConfiguration: true, lastUpdatedDateUtc: null }),
      isLoading: false,
      error: null,
    });
    useUpdateMerchantProfile.mockReturnValue(mutation());

    renderPage();

    // The one thing a console showing inherited values has to make visible: they are not this
    // tenant's, and every document issued under them names somebody else's company.
    const banner = await screen.findByTestId("merchant-inherited");
    expect(banner).toHaveTextContent(/shares that identity/);
  });

  it("says a paid subscription cannot start while no seller is named", async () => {
    useMerchantProfile.mockReturnValue({
      data: profile({
        legalName: "",
        isComplete: false,
        missingFields: ["legalName"],
        isInheritedFromConfiguration: true,
        lastUpdatedDateUtc: null,
      }),
      isLoading: false,
      error: null,
    });
    useUpdateMerchantProfile.mockReturnValue(mutation());

    renderPage();

    expect(await screen.findByTestId("merchant-inherited")).toHaveTextContent(
      /A paid subscription cannot start until a seller is named/,
    );
  });

  it("submits trimmed values and normalises the country code", async () => {
    const mutate = vi.fn();
    useMerchantProfile.mockReturnValue({
      data: profile({ address: null, taxRegistrationId: null, paymentInstructions: null }),
      isLoading: false,
      error: null,
    });
    useUpdateMerchantProfile.mockReturnValue(mutation({ mutate }));

    renderPage();

    const user = userEvent.setup();
    await user.clear(await screen.findByDisplayValue("Northwind Software GmbH"));
    await user.type(screen.getByLabelText("Legal name"), "  Contoso SA  ");
    await user.type(screen.getByLabelText("Country code"), "ch");
    await user.click(screen.getByRole("button", { name: /save merchant profile/i }));

    expect(mutate).toHaveBeenCalledTimes(1);
    const [request] = mutate.mock.calls[0];
    expect(request.legalName).toBe("Contoso SA");
    expect(request.address.countryCode).toBe("CH");

    // Sent as null rather than omitted, so clearing a field actually clears it on the next document
    // rather than leaving the old value in place.
    expect(request.taxRegistrationId).toBeNull();
    expect(request.paymentInstructions).toBeNull();
  });

  it("loads stored branding colors into the pickers, and the shared default when unset", async () => {
    useMerchantProfile.mockReturnValue({
      data: profile({ primaryColor: "#112233", accentColor: null }),
      isLoading: false,
      error: null,
    });
    useUpdateMerchantProfile.mockReturnValue(mutation());

    renderPage();

    // A native color input always normalises its value to lowercase, regardless of what was set —
    // that is the swatch, not the text field beside it, which is what the form actually submits.
    expect(await screen.findByLabelText("Primary color")).toHaveValue("#112233");
    // Nothing was ever saved for the accent color, so the field shows the same shared default a
    // document renders with — not blank, which would look like the color picker was broken.
    expect(screen.getByLabelText("Accent color")).toHaveValue("#d9e7f5");
  });

  it("submits the logo and both colors alongside everything else", async () => {
    const mutate = vi.fn();
    useMerchantProfile.mockReturnValue({
      data: profile({ logoFileId: "logo-1", primaryColor: "#112233", accentColor: "#445566" }),
      isLoading: false,
      error: null,
    });
    useUpdateMerchantProfile.mockReturnValue(mutation({ mutate }));

    renderPage();

    await userEvent.click(
      await screen.findByRole("button", { name: /save merchant profile/i }),
    );

    expect(mutate).toHaveBeenCalledTimes(1);
    const [request] = mutate.mock.calls[0];
    expect(request.logoFileId).toBe("logo-1");
    expect(request.primaryColor).toBe("#112233");
    expect(request.accentColor).toBe("#445566");
  });

  it("surfaces a refusal from the server rather than looking saved", async () => {
    useMerchantProfile.mockReturnValue({ data: profile(), isLoading: false, error: null });
    useUpdateMerchantProfile.mockReturnValue(
      mutation({
        error: new Error("Only the platform console may set the merchant profile."),
      }),
    );

    renderPage();

    expect(await screen.findByTestId("merchant-error")).toHaveTextContent(
      /Only the platform console/,
    );
  });
});
