import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PaymentProvider } from "../models/payment-provider.model";

const toastMock = vi.fn();
const navigateMock = vi.fn();
const mutateAsyncMock = vi.fn();
const refetchMock = vi.fn();
let isPending = false;
let providersState: {
  data?: PaymentProvider[];
  isLoading: boolean;
  isError: boolean;
  error?: unknown;
};

vi.mock("@/hooks/use-toast", () => ({
  toast: (...args: unknown[]) => toastMock(...args),
}));

vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => navigateMock,
  useParams: () => ({ itemId: "tenant-1", paymentProviderId: "provider-1" }),
}));

vi.mock("../hooks/use-payment-providers", () => ({
  usePaymentProviders: () => ({ ...providersState, refetch: refetchMock }),
}));

vi.mock("../hooks/use-update-payment-provider", () => ({
  useUpdatePaymentProvider: () => ({
    mutateAsync: mutateAsyncMock,
    isPending,
  }),
}));

import { MemoryRouter } from "react-router";
import { UpdatePaymentProviderPage } from "./update-payment-provider-page";

const provider = (overrides: Partial<PaymentProvider> = {}): PaymentProvider => ({
  paymentProviderId: "provider-1",
  version: 7,
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
      <UpdatePaymentProviderPage />
    </MemoryRouter>,
  );

const save = () =>
  fireEvent.click(screen.getByRole("button", { name: /Save changes/ }));

describe("UpdatePaymentProviderPage", () => {
  beforeEach(() => {
    toastMock.mockReset();
    navigateMock.mockReset();
    refetchMock.mockReset();
    mutateAsyncMock
      .mockReset()
      .mockResolvedValue({ providerName: "ADYEN-ONLINE", version: 8 });
    isPending = false;
    providersState = { data: [provider()], isLoading: false, isError: false };
  });

  it("should surface the reason a load failed", () => {
    providersState = {
      isLoading: false,
      isError: true,
      error: new Error("gateway timeout"),
    };

    renderPage();

    expect(screen.getByText("gateway timeout")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Save changes/ })).toBeNull();
  });

  it("should fall back to a generic load message when the failure carries none", () => {
    providersState = { isLoading: false, isError: true, error: "not an error" };

    renderPage();

    expect(
      screen.getByText("The payment provider could not be loaded."),
    ).toBeTruthy();
  });

  it("should say so when the provider in the route does not exist", () => {
    providersState = {
      data: [provider({ paymentProviderId: "someone-else" })],
      isLoading: false,
      isError: false,
    };

    renderPage();

    expect(screen.queryByRole("button", { name: /Save changes/ })).toBeNull();
  });

  it("should prefill the form from the stored provider", () => {
    renderPage();

    expect(
      (screen.getByLabelText(/^Frontend result URL/) as HTMLInputElement).value,
    ).toBe("https://app.example.com/result");
    expect(
      (screen.getByLabelText(/Country code/) as HTMLInputElement).value,
    ).toBe("CH");
  });

  it("should send the version it loaded so a concurrent edit is rejected", async () => {
    renderPage();

    save();

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          paymentProviderId: "provider-1",
          request: expect.objectContaining({ version: 7 }),
        }),
      ),
    );
  });

  it("should upper-case the country code", async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText(/Country code/), {
      target: { value: "de" },
    });

    save();

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          request: expect.objectContaining({ countryCode: "DE" }),
        }),
      ),
    );
  });

  it("should omit an emptied optional rather than sending a blank string", async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText(/Country code/), {
      target: { value: "  " },
    });

    save();

    await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalled());
    const [{ request }] = mutateAsyncMock.mock.calls[0];
    expect(request.countryCode).toBeUndefined();
  });

  it("should confirm the new version and return to the list", async () => {
    renderPage();

    save();

    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "success",
          description: expect.stringContaining("version 8"),
        }),
      ),
    );
    expect(navigateMock).toHaveBeenCalledWith("/app/tenant-1/payment/providers");
  });

  it("should stay on the page and show why an update failed", async () => {
    mutateAsyncMock.mockRejectedValue(new Error("version conflict"));
    renderPage();

    save();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "version conflict",
    );
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("should fall back to a generic message when the failure carries none", async () => {
    mutateAsyncMock.mockRejectedValue("not an error");
    renderPage();

    save();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The provider configuration could not be updated.",
    );
  });

  it("should leave the provider untouched when cancelled", () => {
    renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(navigateMock).toHaveBeenCalledWith("/app/tenant-1/payment/providers");
    expect(mutateAsyncMock).not.toHaveBeenCalled();
  });

  it("should lock the controls while the update is in flight", () => {
    isPending = true;
    renderPage();

    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
    expect(
      screen.getByRole("button", { name: /Saving changes/ }),
    ).toBeDisabled();
  });
});
