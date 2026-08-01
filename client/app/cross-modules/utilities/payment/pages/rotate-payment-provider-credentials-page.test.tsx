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

vi.mock("../hooks/use-rotate-payment-provider-credentials", () => ({
  useRotatePaymentProviderCredentials: () => ({
    mutateAsync: mutateAsyncMock,
    isPending,
  }),
}));

import { MemoryRouter } from "react-router";
import { RotatePaymentProviderCredentialsPage } from "./rotate-payment-provider-credentials-page";

const provider = (overrides: Partial<PaymentProvider> = {}): PaymentProvider => ({
  paymentProviderId: "provider-1",
  version: 4,
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
      <RotatePaymentProviderCredentialsPage />
    </MemoryRouter>,
  );

const rotate = () =>
  fireEvent.click(screen.getByRole("button", { name: /Rotate credentials/ }));

const HEX_64 = "a".repeat(64);

describe("RotatePaymentProviderCredentialsPage", () => {
  beforeEach(() => {
    toastMock.mockReset();
    navigateMock.mockReset();
    refetchMock.mockReset();
    mutateAsyncMock
      .mockReset()
      .mockResolvedValue({ providerName: "ADYEN-ONLINE", version: 5 });
    isPending = false;
    providersState = { data: [provider()], isLoading: false, isError: false };
  });

  it("should surface the reason the provider could not be loaded", () => {
    providersState = {
      isLoading: false,
      isError: true,
      error: new Error("vault unavailable"),
    };

    renderPage();

    expect(screen.getByText("vault unavailable")).toBeTruthy();
  });

  it("should not offer rotation for a provider that does not exist", () => {
    providersState = {
      data: [provider({ paymentProviderId: "someone-else" })],
      isLoading: false,
      isError: false,
    };

    renderPage();

    expect(
      screen.queryByRole("button", { name: /Rotate credentials/ }),
    ).toBeNull();
  });

  it("should start with every credential field empty", () => {
    renderPage();

    // Existing secrets are never returned, so nothing can be prefilled.
    expect((screen.getByLabelText(/New API key/) as HTMLInputElement).value).toBe(
      "",
    );
  });

  it("should send the version it loaded so a concurrent rotation is rejected", async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText(/New API key/), {
      target: { value: "new-api-key" },
    });

    rotate();

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          paymentProviderId: "provider-1",
          request: expect.objectContaining({ version: 4 }),
        }),
      ),
    );
  });

  it("should omit the credentials left blank so they are not rotated", async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText(/New API key/), {
      target: { value: "new-api-key" },
    });

    rotate();

    await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalled());
    const [{ request }] = mutateAsyncMock.mock.calls[0];
    expect(request.apiKey).toBe("new-api-key");
    expect(request.webhookHmacKey).toBeUndefined();
    expect(request.tokenHmacKey).toBeUndefined();
  });

  it("should rotate the Adyen token HMAC when one is supplied", async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText(/New token webhook HMAC/), {
      target: { value: HEX_64 },
    });

    rotate();

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          request: expect.objectContaining({ tokenHmacKey: HEX_64 }),
        }),
      ),
    );
  });

  it("should not offer the Adyen-only token HMAC for Stripe", () => {
    providersState = {
      data: [provider({ providerName: "STRIPE" })],
      isLoading: false,
      isError: false,
    };

    renderPage();

    expect(screen.queryByLabelText(/New token webhook HMAC/)).toBeNull();
  });

  it("should confirm the new version and return to the list", async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText(/New API key/), {
      target: { value: "new-api-key" },
    });

    rotate();

    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "success",
          description: expect.stringContaining("version 5"),
        }),
      ),
    );
    expect(navigateMock).toHaveBeenCalledWith("/app/tenant-1/payment/providers");
  });

  it("should stay on the page and show why a rotation failed", async () => {
    mutateAsyncMock.mockRejectedValue(new Error("key rejected by provider"));
    renderPage();
    fireEvent.change(screen.getByLabelText(/New API key/), {
      target: { value: "new-api-key" },
    });

    rotate();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "key rejected by provider",
    );
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("should fall back to a generic message when the failure carries none", async () => {
    mutateAsyncMock.mockRejectedValue("not an error");
    renderPage();
    fireEvent.change(screen.getByLabelText(/New API key/), {
      target: { value: "new-api-key" },
    });

    rotate();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The provider credentials could not be rotated.",
    );
  });

  it("should leave the credentials untouched when cancelled", () => {
    renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(navigateMock).toHaveBeenCalledWith("/app/tenant-1/payment/providers");
    expect(mutateAsyncMock).not.toHaveBeenCalled();
  });

  it("should lock the controls while the rotation is in flight", () => {
    isPending = true;
    renderPage();

    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
    expect(
      screen.getByRole("button", { name: /Rotating credentials/ }),
    ).toBeDisabled();
  });
});
