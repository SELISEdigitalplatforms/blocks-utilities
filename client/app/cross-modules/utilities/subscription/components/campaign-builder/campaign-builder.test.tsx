import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../../models/subscription-plan.model";
import { CampaignBuilder } from "./campaign-builder";
import type { CampaignDraft } from "./campaign-draft";

const plan: SubscriptionPlan = {
  planId: "plan-1",
  code: "pro",
  displayName: "Pro",
  description: null,
  entitlements: [{ key: "seats", limitKind: "Count", limit: 5, meterKey: null, unitLabel: "seat" }],
  prices: [
    {
      priceId: "price-monthly-calendar",
      currencyCode: "USD",
      unitAmountMinor: 1_000,
      interval: "Month",
      intervalCount: 1,
      billingAlignment: "CalendarMonth",
      quantityItemKey: null,
    },
    {
      priceId: "price-monthly-anniversary",
      currencyCode: "USD",
      unitAmountMinor: 1_000,
      interval: "Month",
      intervalCount: 1,
      billingAlignment: "Anniversary",
      quantityItemKey: null,
    },
  ],
} as unknown as SubscriptionPlan;

const click = (name: RegExp) => fireEvent.click(screen.getByRole("button", { name }));

const renderBuilder = (onSubmit = vi.fn<(draft: CampaignDraft) => Promise<void>>()) => {
  const onCancel = vi.fn();
  render(
    <CampaignBuilder
      plans={[plan]}
      organizationId="org-1"
      isSubmitting={false}
      submissionError={null}
      onSubmit={onSubmit}
      onCancel={onCancel}
    />,
  );
  return { onSubmit, onCancel };
};

describe("CampaignBuilder", () => {
  it("blocks advancing past Identity until a code and display name are entered", () => {
    renderBuilder();

    expect(screen.getByRole("button", { name: /^Next$/ })).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/Code/), { target: { value: "launch-25" } });
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: "Launch" } });

    expect(screen.getByRole("button", { name: /^Next$/ })).toBeEnabled();
  });

  it("cancels from the first step instead of going back", () => {
    const { onCancel } = renderBuilder();

    click(/^Cancel$/);

    expect(onCancel).toHaveBeenCalledOnce();
  });

  it("walks a Standard discount through all four steps and submits it unchanged by any campaign rule", async () => {
    const { onSubmit } = renderBuilder();

    fireEvent.change(screen.getByLabelText(/Code/), { target: { value: "launch-25" } });
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: "Launch offer" } });
    click(/^Next$/);

    // Benefit: the default 10% carries through untouched.
    await screen.findByText(/What redeeming this discount takes off/);
    fireEvent.change(screen.getByLabelText(/Starts at \(optional\)/), {
      target: { value: "2026-10-01T09:30" },
    });
    fireEvent.change(screen.getByLabelText(/Expires at \(optional\)/), {
      target: { value: "2026-10-31T18:00" },
    });
    click(/^Next$/);

    // Eligibility: unrestricted is valid for a Standard discount.
    await screen.findByText(/redeemed in/);
    click(/^Next$/);

    await screen.findByText(/Check everything before creating/);
    click(/^Create discount$/);

    await waitFor(() => expect(onSubmit).toHaveBeenCalledOnce());
    const [draft] = onSubmit.mock.calls[0]!;
    expect(draft.campaignKind).toBe("Standard");
    expect(draft.code).toBe("launch-25");
    expect(draft.startsAtUtc).toBe("2026-10-01T09:30");
    expect(draft.expiresAtUtc).toBe("2026-10-31T18:00");
  });

  it("locks a free-opening-period campaign's benefit and eligibility fields the moment it is picked", async () => {
    renderBuilder();

    fireEvent.change(screen.getByLabelText(/Code/), { target: { value: "free1" } });
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: "Free month" } });
    fireEvent.click(screen.getByLabelText(/Free opening month/));
    click(/^Next$/);

    await screen.findByText(/always a full 100% reduction/);
    // Next is enabled without touching anything else -- the kind change already locked the
    // reduction to something valid.
    expect(screen.getByRole("button", { name: /^Next$/ })).toBeEnabled();
    click(/^Next$/);

    // Eligibility: only the calendar-aligned monthly price is offered, and the campaign is
    // blocked until one is picked plus the entitlement is named.
    await screen.findByText(/redeemed in/);
    expect(screen.queryByText(/price-monthly-anniversary/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Next$/ })).toBeDisabled();

    fireEvent.click(screen.getByLabelText(/Restrict to pro/));
    fireEvent.change(screen.getByLabelText(/Starts/), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText(/Ends \(inclusive\)/), { target: { value: "2026-12-31" } });
    fireEvent.change(screen.getByLabelText(/Temporary entitlement/), { target: { value: "seats" } });
    fireEvent.change(screen.getByLabelText(/Temporary limit/), { target: { value: "1" } });

    expect(screen.getByRole("button", { name: /^Next$/ })).toBeEnabled();

    const oneUseSwitch = screen.getByLabelText(/One redemption per organization/);
    expect(oneUseSwitch).toBeDisabled();
    expect(oneUseSwitch).toHaveAttribute("data-state", "checked");
  });

  it("shows the submission error returned by the caller without losing the draft", () => {
    render(
      <CampaignBuilder
        plans={[plan]}
        organizationId="org-1"
        isSubmitting={false}
        submissionError="A discount with that code already exists."
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
      />,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("A discount with that code already exists.");
  });
});
