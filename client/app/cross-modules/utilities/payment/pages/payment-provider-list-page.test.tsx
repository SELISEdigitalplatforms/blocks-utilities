import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PaymentProvider } from "../models/payment-provider.model";

const refetchMock = vi.fn();
let providersState: {
  data?: PaymentProvider[];
  isLoading: boolean;
  isError: boolean;
  isFetching: boolean;
  error?: unknown;
};

vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useParams: () => ({ itemId: "tenant-1" }),
}));

vi.mock("../hooks/use-payment-providers", () => ({
  usePaymentProviders: () => ({ ...providersState, refetch: refetchMock }),
}));

import { MemoryRouter } from "react-router";
import { PaymentProviderListPage } from "./payment-provider-list-page";

const provider = (overrides: Partial<PaymentProvider> = {}): PaymentProvider => ({
  paymentProviderId: "provider-1",
  version: 3,
  providerName: "ADYEN-ONLINE",
  merchantId: "MyMerchant",
  apiBaseUrl: "https://checkout-test.adyen.com/v72",
  returnUrl: null,
  frontendResultUrl: "https://app.example.com/result",
  countryCode: "CH",
  manualCapture: false,
  maxRefundDays: 365,
  storeId: null,
  isEnabled: true,
  ...overrides,
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <PaymentProviderListPage />
    </MemoryRouter>,
  );

const search = (term: string) =>
  fireEvent.change(screen.getByLabelText("Search payment providers"), {
    target: { value: term },
  });

describe("PaymentProviderListPage", () => {
  beforeEach(() => {
    refetchMock.mockReset();
    providersState = {
      data: [provider()],
      isLoading: false,
      isError: false,
      isFetching: false,
    };
  });

  it("should show a loading placeholder while providers are being fetched", () => {
    providersState = { isLoading: true, isError: false, isFetching: true };

    renderPage();

    expect(screen.getByLabelText("Loading providers")).toBeTruthy();
  });

  it("should report a failed load", () => {
    providersState = {
      isLoading: false,
      isError: true,
      isFetching: false,
      error: new Error("gateway down"),
    };

    renderPage();

    expect(screen.getByText("gateway down")).toBeTruthy();
  });

  it("should invite a first registration when none exist", () => {
    providersState = {
      data: [],
      isLoading: false,
      isError: false,
      isFetching: false,
    };

    renderPage();

    expect(screen.getByText("No payment provider registered")).toBeTruthy();
  });

  it("should distinguish a filtered-out list from an empty one", () => {
    renderPage();

    search("nothing-matches");

    expect(screen.getByText("No providers match these filters")).toBeTruthy();
  });

  it("should list a registered provider", () => {
    renderPage();

    expect(screen.getByText("MyMerchant")).toBeTruthy();
  });

  it("should match a search against the merchant", () => {
    providersState = {
      data: [
        provider(),
        provider({ paymentProviderId: "p2", merchantId: "OtherMerchant" }),
      ],
      isLoading: false,
      isError: false,
      isFetching: false,
    };

    renderPage();
    search("othermerchant");

    expect(screen.getByText("OtherMerchant")).toBeTruthy();
    expect(screen.queryByText("MyMerchant")).toBeNull();
  });

  it("should match a search against the country", () => {
    providersState = {
      data: [
        provider(),
        provider({ paymentProviderId: "p2", merchantId: "German", countryCode: "DE" }),
      ],
      isLoading: false,
      isError: false,
      isFetching: false,
    };

    renderPage();
    search("de");

    expect(screen.getByText("German")).toBeTruthy();
    expect(screen.queryByText("MyMerchant")).toBeNull();
  });

  it("should treat a provider with no country as not matching a country search", () => {
    providersState = {
      // "MyMerchant" itself contains "ch", so the merchant must not confound this.
      data: [provider({ merchantId: "Alpha", countryCode: null })],
      isLoading: false,
      isError: false,
      isFetching: false,
    };

    renderPage();
    search("ch");

    expect(screen.getByText("No providers match these filters")).toBeTruthy();
  });

  it("should ignore surrounding whitespace in the search", () => {
    renderPage();

    search("   MyMerchant   ");

    expect(screen.getByText("MyMerchant")).toBeTruthy();
  });
});
