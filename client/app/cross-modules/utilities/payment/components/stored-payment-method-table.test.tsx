import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { StoredPaymentMethod } from "../models/stored-payment-method.model";
import { StoredPaymentMethodTable } from "./stored-payment-method-table";

const method = (
  overrides: Partial<StoredPaymentMethod> = {},
): StoredPaymentMethod => ({
  paymentMethodId: "pm-abcdef1234",
  brand: "visa",
  lastFour: "4242",
  type: "scheme",
  expiryMonth: "3",
  expiryYear: "2030",
  fundingSource: "credit",
  issuerCountry: "CH",
  status: "Active",
  ...overrides,
});

const renderTable = (
  methods: StoredPaymentMethod[],
  removingPaymentMethodId: string | null = null,
) => {
  const onRemove = vi.fn();
  render(
    <StoredPaymentMethodTable
      methods={methods}
      removingPaymentMethodId={removingPaymentMethodId}
      onRemove={onRemove}
    />,
  );
  return onRemove;
};

describe("StoredPaymentMethodTable", () => {
  it("should mask the card number down to the last four digits", () => {
    renderTable([method()]);

    // The full number must never be renderable, so the component only ever
    // receives and shows the last four.
    expect(screen.getAllByText("•••• •••• •••• 4242").length).toBeGreaterThan(0);
  });

  it("should say so when there are no masked details to show", () => {
    renderTable([method({ lastFour: null })]);

    expect(
      screen.getAllByText("Masked details unavailable").length,
    ).toBeGreaterThan(0);
  });

  it("should title-case the brand and drop the credit suffix", () => {
    renderTable([method({ brand: "mc_credit" })]);

    expect(screen.getAllByText("Mc").length).toBeGreaterThan(0);
  });

  it("should fall back to a generic label when the brand is unknown", () => {
    renderTable([method({ brand: null })]);

    expect(screen.getAllByText("Payment method").length).toBeGreaterThan(0);
  });

  it("should pad a single digit expiry month", () => {
    renderTable([method({ expiryMonth: "3", expiryYear: "2030" })]);

    expect(screen.getAllByText("03/2030").length).toBeGreaterThan(0);
  });

  it("should show a dash when the expiry is incomplete", () => {
    renderTable([method({ expiryMonth: "3", expiryYear: null })]);

    expect(screen.getAllByText("—").length).toBeGreaterThan(0);
  });

  it.each([
    ["type", "type"],
    ["fundingSource", "funding source"],
    ["issuerCountry", "issuer country"],
  ])("should show a placeholder when %s is missing", (field) => {
    renderTable([method({ [field]: null } as Partial<StoredPaymentMethod>)]);

    expect(screen.getAllByText("Not available").length).toBeGreaterThan(0);
  });

  it("should treat a blank string the same as a missing value", () => {
    renderTable([method({ type: "   " })]);

    expect(screen.getAllByText("Not available").length).toBeGreaterThan(0);
  });

  it("should name the card in the remove control so it is distinguishable", () => {
    renderTable([method()]);

    expect(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    ).toBeTruthy();
  });

  it("should still label the remove control when the digits are unknown", () => {
    renderTable([method({ lastFour: null })]);

    expect(
      screen.getByRole("button", { name: "Remove Visa ending in unknown digits" }),
    ).toBeTruthy();
  });

  it("should hand the whole method back when remove is pressed", () => {
    const target = method();
    const onRemove = renderTable([target]);

    fireEvent.click(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    );

    expect(onRemove).toHaveBeenCalledWith(target);
  });

  it("should disable the remove control for the method being removed", () => {
    renderTable([method()], "pm-abcdef1234");

    // Both the table row control and the card control refer to the same method.
    expect(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    ).toBeDisabled();
    expect(
      screen.getByRole("button", { name: /Remove payment method/ }),
    ).toBeDisabled();
  });

  it("should leave other methods removable while one is being removed", () => {
    renderTable(
      [method(), method({ paymentMethodId: "pm-other", lastFour: "1111" })],
      "pm-abcdef1234",
    );

    expect(
      screen.getByRole("button", { name: "Remove Visa ending in 1111" }),
    ).not.toBeDisabled();
  });

  it("should render one row per method", () => {
    renderTable([
      method(),
      method({ paymentMethodId: "pm-second", lastFour: "1111" }),
    ]);

    const table = screen.getByRole("table");
    // One header row plus one row per method.
    expect(within(table).getAllByRole("row")).toHaveLength(3);
  });

  it("should render nothing but the header for an empty list", () => {
    renderTable([]);

    const table = screen.getByRole("table");
    expect(within(table).getAllByRole("row")).toHaveLength(1);
  });

  it("should show the status of each method", () => {
    renderTable([method({ status: "Expired" })]);

    expect(screen.getAllByText("Expired").length).toBeGreaterThan(0);
  });
});
