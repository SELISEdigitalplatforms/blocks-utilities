import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";

import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { PlanCard } from "./plan-card";

const plan = (overrides: Partial<SubscriptionPlan> = {}): SubscriptionPlan =>
  ({
    planId: "plan-1",
    code: "pro",
    displayName: "Professional",
    description: null,
    featuresJson: null,
    organizationId: null,
    trialDays: null,
    trialRequiresPaymentMethod: false,
    version: 1,
    hasSubscribers: false,
    status: "Active",
    quantityItems: [],
    meters: [],
    entitlements: [],
    prices: [],
    ...overrides,
  }) as SubscriptionPlan;

const renderCard = (
  subject: SubscriptionPlan,
  onArchive: ((plan: SubscriptionPlan) => void) | undefined = vi.fn(),
) =>
  render(
    <MemoryRouter>
      <PlanCard
        plan={subject}
        organizationLabel="Tenant-wide"
        detailPath="/plans/plan-1"
        editPath="/plans/plan-1/edit"
        duplicatePath="/plans/create"
        onArchive={onArchive}
      />
    </MemoryRouter>,
  );

describe("PlanCard", () => {
  it("shows the plan, its code, scope and status", () => {
    renderCard(plan());

    expect(screen.getByRole("link", { name: "Professional" })).toHaveAttribute(
      "href",
      "/plans/plan-1",
    );
    expect(screen.getByText("pro")).toBeInTheDocument();
    expect(screen.getByText("Tenant-wide")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("reports pricing, cadence and configuration counts", () => {
    renderCard(
      plan({
        prices: [
          {
            priceId: "price-1",
            currencyCode: "CHF",
            unitAmountMinor: 4_500,
            interval: "Month",
            intervalCount: 1,
            quantityItemKey: null,
          },
          {
            priceId: "price-2",
            currencyCode: "CHF",
            unitAmountMinor: 48_000,
            interval: "Year",
            intervalCount: 1,
            quantityItemKey: null,
          },
        ],
        meters: [{ meterKey: "api", overageAllowed: true }],
      } as unknown as Partial<SubscriptionPlan>),
    );

    expect(screen.getByText(/From/)).toBeInTheDocument();
    expect(screen.getByText(/Billed every month, every year/)).toBeInTheDocument();
    expect(screen.getByText(/1 metered allowance with overage pricing/)).toBeInTheDocument();
    expect(screen.getByText("2 prices")).toBeInTheDocument();
  });

  it("names the family and level when the plan has one", () => {
    renderCard(plan({ familyCode: "core", familyRank: 2 }));

    expect(screen.getByText("core · level 2")).toBeInTheDocument();
  });

  it("offers edit, duplicate and archive on an active plan", async () => {
    const user = userEvent.setup();
    const onArchive = vi.fn();
    renderCard(plan(), onArchive);

    await user.click(screen.getByRole("button", { name: /Actions for Professional/i }));

    expect(screen.getByRole("menuitem", { name: /Edit/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /^Duplicate$/i })).toBeInTheDocument();

    await user.click(screen.getByRole("menuitem", { name: /Archive/i }));

    expect(onArchive).toHaveBeenCalledWith(expect.objectContaining({ planId: "plan-1" }));
  });

  /**
   * Absent rather than disabled. The server refuses every catalogue mutation on an archived plan,
   * so a greyed-out Edit would invite a click and then an explanation; nothing at all says it is
   * not on offer. There is deliberately no restore.
   */
  it("offers only viewing and duplication on an archived plan", async () => {
    const user = userEvent.setup();
    const onArchive = vi.fn();
    renderCard(plan({ status: "Archived" }), onArchive);

    expect(screen.getByText("Archived")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Actions for Professional/i }));

    expect(screen.getByRole("menuitem", { name: /Duplicate as new plan/i })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /Edit/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /Archive/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /Restore/i })).not.toBeInTheDocument();
    expect(onArchive).not.toHaveBeenCalled();
  });

  /**
   * A plan cached before the status field existed reads as on sale. The alternative would mute the
   * whole catalogue after a deploy.
   */
  it("treats a plan with no status as active", () => {
    renderCard(plan({ status: undefined }));

    expect(screen.getByText("Active")).toBeInTheDocument();
  });
});
