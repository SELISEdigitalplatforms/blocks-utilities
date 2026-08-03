import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const toastMock = vi.fn();
const navigateMock = vi.fn();
const mutateAsyncMock = vi.fn();
let isPending = false;

vi.mock("@/hooks/use-toast", () => ({
  toast: (...args: unknown[]) => toastMock(...args),
}));

// The page header renders a Link, so only navigation is replaced.
vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => navigateMock,
  useParams: () => ({ itemId: "tenant-1" }),
}));

vi.mock("../hooks/use-register-payment-provider", () => ({
  useRegisterPaymentProvider: () => ({
    mutateAsync: mutateAsyncMock,
    isPending,
  }),
}));

import { MemoryRouter } from "react-router";
import { CreatePaymentProviderPage } from "./create-payment-provider-page";

/** The page header renders a Link, which needs a router context. */
const renderPage = () =>
  render(
    <MemoryRouter>
      <CreatePaymentProviderPage />
    </MemoryRouter>,
  );

const ADYEN_TEST_API_BASE_URL = "https://checkout-test.adyen.com/v72";

const fill = (label: string | RegExp, value: string) =>
  fireEvent.change(screen.getByLabelText(label), { target: { value } });

/** Fills the minimum a valid Adyen registration needs. */
const fillValidAdyen = () => {
  fill("Merchant ID", "MyMerchant");
  fill(/Checkout API base URL/, ADYEN_TEST_API_BASE_URL);
  fill(/^Frontend result URL/, "https://app.example.com/result");
  fill("API key", "adyen-api-key");
  fill(/Standard webhook HMAC/, "a".repeat(64));
  fill(/Token webhook HMAC/, "b".repeat(64));
};

const submit = () =>
  fireEvent.click(screen.getByRole("button", { name: /Create provider/ }));

describe("CreatePaymentProviderPage", () => {
  beforeEach(() => {
    toastMock.mockReset();
    navigateMock.mockReset();
    mutateAsyncMock
      .mockReset()
      .mockResolvedValue({ providerName: "ADYEN-ONLINE" });
    isPending = false;
  });

  it("should default to Adyen and prefill its test checkout host", () => {
    renderPage();

    expect(
      (screen.getByLabelText(/Checkout API base URL/) as HTMLInputElement).value,
    ).toBe(ADYEN_TEST_API_BASE_URL);
  });

  it("should show the Adyen-only fields by default", () => {
    renderPage();

    expect(screen.getByLabelText(/Checkout API base URL/)).toBeTruthy();
    expect(screen.getByLabelText(/Token webhook HMAC/)).toBeTruthy();
  });

  it("should send the trimmed identity and the Adyen-only fields", async () => {
    renderPage();
    fillValidAdyen();
    fill("Merchant ID", "  MyMerchant  ");

    submit();

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          providerName: "ADYEN-ONLINE",
          merchantId: "MyMerchant",
          apiBaseUrl: ADYEN_TEST_API_BASE_URL,
          tokenHmacKey: "b".repeat(64),
        }),
      ),
    );
  });

  it("should upper-case the country code", async () => {
    renderPage();
    fillValidAdyen();
    fill(/Country code/, "ch");

    submit();

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({ countryCode: "CH" }),
      ),
    );
  });

  it("should omit optional values left blank rather than sending empty strings", async () => {
    renderPage();
    fillValidAdyen();

    submit();

    await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalled());
    const [request] = mutateAsyncMock.mock.calls[0];
    expect(request.countryCode).toBeUndefined();
    expect(request.storeId).toBeUndefined();
  });

  it("should confirm and return to the provider list on success", async () => {
    renderPage();
    fillValidAdyen();

    submit();

    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "success" }),
      ),
    );
    expect(navigateMock).toHaveBeenCalledWith("/app/tenant-1/payment/providers");
  });

  it("should stay on the page and show why a registration failed", async () => {
    mutateAsyncMock.mockRejectedValue(new Error("merchant already registered"));
    renderPage();
    fillValidAdyen();

    submit();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "merchant already registered",
    );
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("should fall back to a generic message when the failure carries none", async () => {
    mutateAsyncMock.mockRejectedValue("not an error");
    renderPage();
    fillValidAdyen();

    submit();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The payment provider could not be created.",
    );
  });

  it("should refuse to submit without the required credentials", async () => {
    renderPage();

    submit();

    await waitFor(() => expect(mutateAsyncMock).not.toHaveBeenCalled());
  });

  it("should refuse a frontend result URL that is not HTTPS", async () => {
    renderPage();
    fillValidAdyen();
    fill(/^Frontend result URL/, "http://app.example.com/result");

    submit();

    expect(
      await screen.findByText("Enter an absolute HTTPS URL."),
    ).toBeTruthy();
    expect(mutateAsyncMock).not.toHaveBeenCalled();
  });

  it("should leave the form untouched when cancelled", () => {
    renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(navigateMock).toHaveBeenCalledWith("/app/tenant-1/payment/providers");
    expect(mutateAsyncMock).not.toHaveBeenCalled();
  });

  it("should lock the controls while the registration is in flight", () => {
    isPending = true;
    renderPage();

    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
    expect(
      screen.getByRole("button", { name: /Creating provider/ }),
    ).toBeDisabled();
  });
});
