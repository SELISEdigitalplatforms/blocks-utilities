import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PaymentListItem } from "../models/payment.model";

const toastMock = vi.fn();
const mutateAsyncMock = vi.fn();
let isPending = false;
let uuidCounter = 0;

vi.mock("@/hooks/use-toast", () => ({
  toast: (...args: unknown[]) => toastMock(...args),
}));

vi.mock("uuid", () => ({
  v4: () => `uuid-${++uuidCounter}`,
}));

vi.mock("../hooks/use-create-payment-refund", () => ({
  useCreatePaymentRefund: () => ({
    mutateAsync: mutateAsyncMock,
    isPending,
  }),
}));

import { PaymentRefundDialog } from "./payment-refund-dialog";

const payment = (overrides: Partial<PaymentListItem> = {}): PaymentListItem => ({
  paymentDetailId: "payment-1",
  providerName: "ADYEN-ONLINE",
  amount: 100,
  currencyCode: "CHF",
  paymentDateUtc: "2026-07-23T11:00:00Z",
  paymentStatus: "CAPTURED",
  hasPendingRefund: false,
  ...overrides,
});

const renderDialog = (item = payment()) => {
  const onClose = vi.fn();
  render(<PaymentRefundDialog payment={item} onClose={onClose} />);
  return onClose;
};

const amountField = () =>
  screen.getByLabelText("Refund amount") as HTMLInputElement;
const confirm = () => screen.getByRole("button", { name: /Confirm refund/ });

describe("PaymentRefundDialog", () => {
  beforeEach(() => {
    toastMock.mockReset();
    mutateAsyncMock
      .mockReset()
      .mockResolvedValue({ refundId: "refund-9", status: "PENDING" });
    isPending = false;
    uuidCounter = 0;
  });

  it("should prefill the full payment amount", () => {
    renderDialog();

    expect(amountField().value).toBe("100");
  });

  it("should submit the amount and payment it was opened for", async () => {
    renderDialog();

    fireEvent.click(confirm());

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          paymentDetailId: "payment-1",
          request: expect.objectContaining({ amount: 100 }),
        }),
      ),
    );
  });

  it.each([["0"], ["-5"], [""], ["abc"]])(
    "should refuse an amount of %s before calling the service",
    async (value) => {
      renderDialog();
      fireEvent.change(amountField(), { target: { value } });

      fireEvent.click(confirm());

      expect(await screen.findByRole("alert")).toHaveTextContent(
        "Enter a refund amount greater than zero.",
      );
      expect(mutateAsyncMock).not.toHaveBeenCalled();
    },
  );

  it("should refuse to refund more than was paid", async () => {
    renderDialog();
    fireEvent.change(amountField(), { target: { value: "100.01" } });

    fireEvent.click(confirm());

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The refund amount cannot exceed 100 CHF.",
    );
    expect(mutateAsyncMock).not.toHaveBeenCalled();
  });

  it("should allow a partial refund", async () => {
    renderDialog();
    fireEvent.change(amountField(), { target: { value: "25.5" } });

    fireEvent.click(confirm());

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          request: expect.objectContaining({ amount: 25.5 }),
        }),
      ),
    );
  });

  it("should send a trimmed reason", async () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/Reason/), {
      target: { value: "  duplicate charge  " },
    });

    fireEvent.click(confirm());

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          request: expect.objectContaining({ reason: "duplicate charge" }),
        }),
      ),
    );
  });

  it("should omit a reason that is only whitespace", async () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/Reason/), {
      target: { value: "   " },
    });

    fireEvent.click(confirm());

    await waitFor(() =>
      expect(mutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          request: expect.objectContaining({ reason: undefined }),
        }),
      ),
    );
  });

  it("should confirm the outcome and close on success", async () => {
    const onClose = renderDialog();

    fireEvent.click(confirm());

    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "success",
          description: expect.stringContaining("refund-9"),
        }),
      ),
    );
    expect(onClose).toHaveBeenCalled();
  });

  it("should keep the dialog open and show why a submission failed", async () => {
    mutateAsyncMock.mockRejectedValue(new Error("provider declined"));
    const onClose = renderDialog();

    fireEvent.click(confirm());

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "provider declined",
    );
    expect(onClose).not.toHaveBeenCalled();
  });

  it("should fall back to a generic message when the failure carries none", async () => {
    mutateAsyncMock.mockRejectedValue("not an error");
    renderDialog();

    fireEvent.click(confirm());

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The refund request could not be submitted.",
    );
  });

  it("should reuse the same idempotency key when retrying an unchanged request", async () => {
    mutateAsyncMock.mockRejectedValue(new Error("timeout"));
    renderDialog();

    fireEvent.click(confirm());
    await screen.findByRole("alert");
    fireEvent.click(confirm());

    await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalledTimes(2));
    // Retrying the identical request must not be able to refund twice.
    const [first] = mutateAsyncMock.mock.calls[0];
    const [second] = mutateAsyncMock.mock.calls[1];
    expect(second.idempotencyKey).toBe(first.idempotencyKey);
  });

  it("should issue a new idempotency key once the amount is edited after an attempt", async () => {
    mutateAsyncMock.mockRejectedValue(new Error("declined"));
    renderDialog();

    fireEvent.click(confirm());
    await screen.findByRole("alert");
    fireEvent.change(amountField(), { target: { value: "40" } });
    mutateAsyncMock.mockResolvedValue({ refundId: "r-2", status: "PENDING" });
    fireEvent.click(confirm());

    await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalledTimes(2));
    // A different amount is a different request, so it must not be deduplicated
    // against the first attempt.
    const [first] = mutateAsyncMock.mock.calls[0];
    const [second] = mutateAsyncMock.mock.calls[1];
    expect(second.idempotencyKey).not.toBe(first.idempotencyKey);
  });

  it("should clear a validation message as soon as the amount is corrected", async () => {
    renderDialog();
    fireEvent.change(amountField(), { target: { value: "0" } });
    fireEvent.click(confirm());
    await screen.findByRole("alert");

    fireEvent.change(amountField(), { target: { value: "10" } });

    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("should count the reason against its limit", () => {
    renderDialog();

    fireEvent.change(screen.getByLabelText(/Reason/), {
      target: { value: "abcde" },
    });

    expect(screen.getByText("5/280")).toBeTruthy();
  });

  it("should close when cancelled", () => {
    const onClose = renderDialog();

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(onClose).toHaveBeenCalled();
  });

  it("should lock the controls while the refund is in flight", () => {
    isPending = true;
    renderDialog();

    // The confirm control relabels itself while the request is in flight.
    const submitting = screen.getByRole("button", { name: /Submitting refund/ });
    expect(amountField()).toBeDisabled();
    expect(submitting).toBeDisabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: /Confirm refund/ })).toBeNull();
  });

  it("should show the payment it refers to", () => {
    renderDialog(payment({ paymentDetailId: "payment-xyz", amount: 42 }));

    expect(screen.getByText("payment-xyz")).toBeTruthy();
    expect(screen.getByText("42 CHF")).toBeTruthy();
  });
});
