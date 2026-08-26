import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { EMPTY_PAYMENT_FILTERS } from "../models/payment.model";
import type { PaymentFilters } from "../models/payment.model";
import { PaymentFiltersPanel } from "./payment-filters";

// The organization filter reaches IAM through react-query, which these tests render without a
// client. Stubbed rather than provided, because none of them are about that filter.
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({
    data: { organizations: [{ itemId: "organization-2", name: "Test Org" }] },
    isError: false,
  }),
}));

const renderPanel = (
  overrides: Partial<PaymentFilters> = {},
  activeFilterCount = 0,
) => {
  const onChange = vi.fn();
  const onApply = vi.fn();
  const onReset = vi.fn();

  render(
    <PaymentFiltersPanel
      value={{ ...EMPTY_PAYMENT_FILTERS, ...overrides }}
      activeFilterCount={activeFilterCount}
      onChange={onChange}
      onApply={onApply}
      onReset={onReset}
    />,
  );

  return { onChange, onApply, onReset };
};

const apply = () =>
  fireEvent.click(screen.getByRole("button", { name: "Apply filters" }));

/** The exact-match fields live behind the "More filters" disclosure. */
const expand = () =>
  fireEvent.click(screen.getByRole("button", { name: /More filters/ }));

describe("PaymentFiltersPanel", () => {
  it("should apply a valid filter set", () => {
    const { onApply } = renderPanel({ orderId: "order-1" });

    apply();

    expect(onApply).toHaveBeenCalled();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("should refuse a maximum amount below the minimum", () => {
    const { onApply } = renderPanel({ minAmount: "100", maxAmount: "10" });

    apply();

    expect(screen.getByRole("alert")).toHaveTextContent(
      "Maximum amount must be greater than or equal to minimum amount.",
    );
    expect(onApply).not.toHaveBeenCalled();
  });

  it("should accept a maximum amount equal to the minimum", () => {
    const { onApply } = renderPanel({ minAmount: "50", maxAmount: "50" });

    apply();

    expect(onApply).toHaveBeenCalled();
  });

  it("should accept a minimum with no maximum", () => {
    const { onApply } = renderPanel({ minAmount: "50" });

    apply();

    expect(onApply).toHaveBeenCalled();
  });

  it("should refuse an end date before the start date", () => {
    const { onApply } = renderPanel({
      paymentDateFrom: "2026-07-10",
      paymentDateTo: "2026-07-01",
    });

    apply();

    expect(screen.getByRole("alert")).toHaveTextContent(
      "The end date must be the same as or later than the start date.",
    );
    expect(onApply).not.toHaveBeenCalled();
  });

  it("should accept an end date equal to the start date", () => {
    const { onApply } = renderPanel({
      paymentDateFrom: "2026-07-10",
      paymentDateTo: "2026-07-10",
    });

    apply();

    expect(onApply).toHaveBeenCalled();
  });

  it.each([["CH"], ["CHFX"], ["ch1"]])(
    "should refuse the malformed currency %s",
    (currencyCode) => {
      const { onApply } = renderPanel({ currencyCode });

      apply();

      expect(screen.getByRole("alert")).toHaveTextContent(
        "Currency must contain exactly three letters.",
      );
      expect(onApply).not.toHaveBeenCalled();
    },
  );

  it("should accept a three-letter currency", () => {
    const { onApply } = renderPanel({ currencyCode: "CHF" });

    apply();

    expect(onApply).toHaveBeenCalled();
  });

  it("should clear the message as soon as an input changes", () => {
    renderPanel({ minAmount: "100", maxAmount: "10" });
    apply();
    expect(screen.getByRole("alert")).toBeTruthy();
    expand();

    fireEvent.change(screen.getByPlaceholderText("Exact order ID"), {
      target: { value: "order-2" },
    });

    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("should report a changed field without losing the others", () => {
    const { onChange } = renderPanel({ orderId: "order-1" });
    expand();

    fireEvent.change(screen.getByPlaceholderText("Exact payment detail ID"), {
      target: { value: "payment-9" },
    });

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ orderId: "order-1", paymentDetailId: "payment-9" }),
    );
  });

  it("should count the active filters for the reader", () => {
    renderPanel({ orderId: "order-1" }, 3);

    expect(screen.getByText("3")).toBeTruthy();
  });

  it("should offer no reset when nothing is filtered", () => {
    renderPanel();

    expect(screen.getByRole("button", { name: /Reset/ })).toBeDisabled();
  });

  it("should reset once something is filtered", () => {
    const { onReset } = renderPanel({ orderId: "order-1" }, 1);

    fireEvent.click(screen.getByRole("button", { name: /Reset/ }));

    expect(onReset).toHaveBeenCalled();
  });

});
