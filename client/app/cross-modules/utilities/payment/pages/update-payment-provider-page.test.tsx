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
  paymentMethodConfigurationId: null,
  checkoutPaymentMethodTypes: null,
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

  /**
   * The payment method selection. Stripe's own concept, so the block is absent for Adyen — which
   * is what the factory above registers by default.
   */
  describe("checkout payment methods", () => {
    const stripe = (overrides: Partial<PaymentProvider> = {}) =>
      provider({ providerName: "STRIPE", ...overrides });

    const showStripe = (overrides: Partial<PaymentProvider> = {}) => {
      providersState = {
        data: [stripe(overrides)],
        isLoading: false,
        isError: false,
      };
    };

    const methods = async () => {
      const [{ request }] = mutateAsyncMock.mock.calls[0];
      return request.checkoutPaymentMethodTypes;
    };

    it("should not offer them for a provider that has no such concept", () => {
      renderPage();

      expect(screen.queryByRole("checkbox", { name: "Card" })).toBeNull();
      expect(
        screen.queryByLabelText(/Payment method configuration ID/),
      ).toBeNull();
    });

    it("should offer them for Stripe", () => {
      showStripe();
      renderPage();

      expect(screen.getByRole("checkbox", { name: "Card" })).toBeTruthy();
      expect(screen.getByRole("checkbox", { name: "TWINT" })).toBeTruthy();
    });

    it("should tick the methods the provider already has", () => {
      showStripe({ checkoutPaymentMethodTypes: ["card", "twint"] });
      renderPage();

      expect(screen.getByRole("checkbox", { name: "Card" })).toBeChecked();
      expect(screen.getByRole("checkbox", { name: "TWINT" })).toBeChecked();
      expect(screen.getByRole("checkbox", { name: "PayPal" })).not.toBeChecked();
    });

    it("should send the ticked methods", async () => {
      showStripe({ checkoutPaymentMethodTypes: ["card"] });
      renderPage();

      fireEvent.click(screen.getByRole("checkbox", { name: "TWINT" }));
      save();

      await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalled());
      expect(await methods()).toEqual(["card", "twint"]);
    });

    /**
     * Clearing the selection and never having made one are the same instruction to the server —
     * both mean "offer whatever the account's own configuration enables" — so unticking
     * everything has to omit the field rather than send an empty array, which Stripe rejects.
     */
    it("should omit the list entirely once everything is unticked", async () => {
      showStripe({ checkoutPaymentMethodTypes: ["card"] });
      renderPage();

      fireEvent.click(screen.getByRole("checkbox", { name: "Card" }));
      save();

      await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalled());
      expect(await methods()).toBeUndefined();
    });

    /**
     * Stripe rejects a session naming both, and the server resolves that by letting the ticked
     * list win. The form says so by disabling the input rather than by implying both apply.
     */
    it("should disable the configuration id once a method is ticked", () => {
      showStripe({ paymentMethodConfigurationId: "pmc_123" });
      renderPage();

      const configurationId = screen.getByLabelText(
        /Payment method configuration ID/,
      );
      expect(configurationId).toBeEnabled();
      expect((configurationId as HTMLInputElement).value).toBe("pmc_123");

      fireEvent.click(screen.getByRole("checkbox", { name: "Card" }));

      expect(configurationId).toBeDisabled();
    });

    it("should refuse a configuration id that is not one", async () => {
      showStripe();
      renderPage();

      fireEvent.change(
        screen.getByLabelText(/Payment method configuration ID/),
        { target: { value: "card" } },
      );
      save();

      expect(
        await screen.findByText(
          "A Stripe payment method configuration id starts with pmc_.",
        ),
      ).toBeTruthy();
      expect(mutateAsyncMock).not.toHaveBeenCalled();
    });

    /**
     * The case this form could otherwise destroy. Both fields are settable through the API, where
     * nothing holds them to the list of checkboxes offered here.
     *
     * Ticking any box rewrites the whole list from what the form offers, so a stored method the
     * form did not offer would be dropped by an edit that had nothing to do with it — the
     * operator ticks PayPal and silently loses us_bank_account from a live checkout. Showing it
     * is what makes the rewrite lossless. (An untouched Save was never the risk: form state
     * still holds what it hydrated.)
     */
    it("should show a method it does not itself offer, and keep it across an unrelated edit", async () => {
      showStripe({ checkoutPaymentMethodTypes: ["card", "us_bank_account"] });
      renderPage();

      expect(
        screen.getByRole("checkbox", { name: "us_bank_account" }),
      ).toBeChecked();

      fireEvent.click(screen.getByRole("checkbox", { name: "PayPal" }));
      save();

      await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalled());
      expect(await methods()).toEqual(["card", "paypal", "us_bank_account"]);
    });

    /** Shown, so it can also be removed deliberately. */
    it("should let an unoffered method be removed", async () => {
      showStripe({ checkoutPaymentMethodTypes: ["card", "us_bank_account"] });
      renderPage();

      fireEvent.click(
        screen.getByRole("checkbox", { name: "us_bank_account" }),
      );
      save();

      await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalled());
      expect(await methods()).toEqual(["card"]);
    });

    /**
     * Hidden for Adyen, but still hydrated from the stored provider and sent back — stripping it
     * would clear a value this form never showed the operator.
     */
    it("should preserve methods stored on a provider that does not show them", async () => {
      providersState = {
        data: [provider({ checkoutPaymentMethodTypes: ["card"] })],
        isLoading: false,
        isError: false,
      };
      renderPage();

      expect(screen.queryByRole("checkbox", { name: "Card" })).toBeNull();

      save();

      await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalled());
      expect(await methods()).toEqual(["card"]);
    });

    /** A method Stripe cannot charge again is marked, since ticking it changes no renewal. */
    it("should mark the methods that cannot back a renewal", () => {
      showStripe();
      renderPage();

      expect(screen.getAllByText("one-off payments only")).toHaveLength(2);
    });
  });
});
