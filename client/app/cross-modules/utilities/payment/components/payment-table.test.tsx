import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { PaymentListItem } from "../models/payment.model";
import { PaymentTable } from "./payment-table";

const payment = (
  overrides: Partial<PaymentListItem> = {},
): PaymentListItem => ({
  paymentDetailId: "payment-1",
  providerName: "ADYEN-ONLINE",
  amount: 10,
  currencyCode: "CHF",
  paymentDateUtc: "2026-07-23T11:00:00Z",
  paymentStatus: "CAPTURED",
  hasPendingRefund: false,
  ...overrides,
});

const renderTable = (
  item: PaymentListItem,
  onRefund = vi.fn(),
) => {
  render(
    <PaymentTable
      items={[item]}
      sortBy="paymentDate"
      sortDirection="desc"
      onSort={vi.fn()}
      onRefund={onRefund}
    />,
  );

  return onRefund;
};

describe("PaymentTable refund action", () => {
  it("shows a disabled requested state while a refund is pending", () => {
    const onRefund = renderTable(
      payment({ hasPendingRefund: true }),
    );

    const actions = screen.getAllByRole("button", {
      name: "Refund requested",
    });

    actions.forEach((action) => expect(action).toBeDisabled());
    fireEvent.click(actions[0]);
    expect(onRefund).not.toHaveBeenCalled();
  });

  it("allows a captured payment without a pending refund", () => {
    const item = payment();
    const onRefund = renderTable(item);
    const actions = screen.getAllByRole("button", {
      name: "Refund",
    });

    fireEvent.click(actions[0]);

    expect(onRefund).toHaveBeenCalledWith(item);
  });
});
