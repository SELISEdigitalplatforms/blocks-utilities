import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { ArchivePlanDialog } from "./archive-plan-dialog";

const plan = {
  planId: "plan-1",
  code: "pro",
  displayName: "Professional",
  description: null,
  featuresJson: null,
  organizationId: null,
  trialDays: null,
  trialRequiresPaymentMethod: false,
  version: 1,
  hasSubscribers: true,
  status: "Active",
  quantityItems: [],
  meters: [],
  entitlements: [],
  prices: [],
} as unknown as SubscriptionPlan;

const open = (onConfirm = vi.fn().mockResolvedValue(undefined)) => {
  const onOpenChange = vi.fn();

  render(
    <ArchivePlanDialog
      plan={plan}
      isOpen
      onOpenChange={onOpenChange}
      onConfirm={onConfirm}
    />,
  );

  return { onConfirm, onOpenChange };
};

describe("ArchivePlanDialog", () => {
  it("names the exact plan being archived", () => {
    open();

    expect(
      screen.getByRole("heading", { name: /Archive Professional\?/i }),
    ).toBeInTheDocument();
    expect(screen.getByText("pro")).toBeInTheDocument();
  });

  /**
   * All five consequences, asserted individually. They are not obvious from the word "archive" and
   * they are not symmetrical: two reassure, one is irreversible. An author who reads only the
   * heading should still not lose anything they did not expect to lose.
   */
  it("states every consequence, including the two reassuring ones", () => {
    open();

    expect(screen.getByText(/disappears from the plans your customers can choose/i))
      .toBeInTheDocument();
    expect(screen.getByText(/will be/i)).toBeInTheDocument();
    expect(screen.getByText(/refused/i)).toBeInTheDocument();
    expect(screen.getByText(/carries on unchanged/i)).toBeInTheDocument();
    expect(screen.getByText(/This cannot be undone\./i)).toBeInTheDocument();
    expect(screen.getByText(/duplicate it later to build a/i)).toBeInTheDocument();
  });

  it("archives on confirmation and closes", async () => {
    const user = userEvent.setup();
    const { onConfirm, onOpenChange } = open();

    await user.click(screen.getByRole("button", { name: /Archive permanently/i }));

    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith(plan));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("does nothing but close when the plan is kept on sale", async () => {
    const user = userEvent.setup();
    const { onConfirm, onOpenChange } = open();

    await user.click(screen.getByRole("button", { name: /Keep it on sale/i }));

    expect(onConfirm).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  /**
   * Archiving cannot be undone, so a second click arriving before the first request settles must
   * not send a second request.
   */
  it("sends one request however many times the button is clicked", async () => {
    const user = userEvent.setup();
    let release = () => {};
    const onConfirm = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          release = resolve;
        }),
    );

    open(onConfirm);

    const confirm = screen.getByRole("button", { name: /Archive permanently/i });
    await user.click(confirm);
    await user.click(confirm);
    await user.click(confirm);

    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: /Archiving/i })).toBeDisabled();

    release();
  });

  /**
   * A failed archive must leave the dialog open with the reason on it. Closing on failure would
   * dismiss the only place the reason was written.
   */
  it("keeps the dialog open and shows why when archiving fails", async () => {
    const user = userEvent.setup();
    const onConfirm = vi
      .fn()
      .mockRejectedValue(new Error("This plan changed while you were archiving it."));
    const { onOpenChange } = open(onConfirm);

    await user.click(screen.getByRole("button", { name: /Archive permanently/i }));

    expect(
      await screen.findByRole("alert"),
    ).toHaveTextContent("This plan changed while you were archiving it.");
    expect(onOpenChange).not.toHaveBeenCalledWith(false);

    // Still offered, because the conflict is the kind of thing a reload and a retry resolves.
    expect(
      screen.getByRole("button", { name: /Archive permanently/i }),
    ).toBeEnabled();
  });
});
