import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";

const toast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({ toast: (...args: unknown[]) => toast(...args) }));

const mutateAsync = vi.fn();
vi.mock("../hooks/use-subscribe-to-plan", () => ({
  useSubscribeToPlan: () => ({ mutateAsync, isPending: false }),
}));

import { SubscribeDialog } from "./subscribe-dialog";

const plan = {
  planId: "plan-1",
  code: "professional",
  displayName: "Professional",
  prices: [
    {
      priceId: "price-1",
      currencyCode: "CHF",
      unitAmountMinor: 4_500,
      interval: "Month",
      intervalCount: 1,
      displayPriceNote: null,
    },
  ],
  quantityItems: [],
} as unknown as SubscriptionPlan;

/**
 * Mounted on the route the shell actually serves, because the notice links to a sibling screen and
 * the link is only correct if the project segment and the organization are both on it.
 */
const renderDialog = (organizationId: string | undefined = "org-1") =>
  render(
    <MemoryRouter
      initialEntries={[
        `/app/project-1/subscription/simulation${
          organizationId ? `?organizationId=${organizationId}` : ""
        }`,
      ]}
    >
      <Routes>
        <Route
          path="/app/:itemId/subscription/simulation"
          element={
            <SubscribeDialog
              plan={plan}
              organizationId={organizationId}
              open
              onOpenChange={() => {}}
              onSubscribed={() => {}}
            />
          }
        />
      </Routes>
    </MemoryRouter>,
  );

/** The refusal as it reaches the client: a 400, whose body is the API envelope. */
const profileRefusal = (fields: string[]) => ({
  status: 400,
  errors: {
    success: false,
    data: null,
    error: {
      code: "subscription_billing_profile_incomplete",
      message: "This organization's billing profile is missing details an invoice must carry.",
      fields: { BillingProfile: fields },
    },
  },
});

describe("subscribe dialog", () => {
  it("does not ask for a billing name or email", () => {
    renderDialog();

    // They looked like the details an invoice needs and satisfied none of them: the server reads the
    // organization's billing profile, and these went to the billing account. Two forms for one
    // answer, free to disagree, and the one on screen was not the one that counted.
    expect(screen.queryByLabelText(/Billing email/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Billing name/)).not.toBeInTheDocument();
  });

  it("sends no contact fields, leaving the server to use the saved profile", async () => {
    mutateAsync.mockResolvedValueOnce({ status: "Active", checkoutUrl: null });

    renderDialog();
    await userEvent.click(screen.getByRole("button", { name: "Subscribe" }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());

    const [request] = mutateAsync.mock.calls.at(-1)!;
    expect(request).not.toHaveProperty("billingEmail");
    expect(request).not.toHaveProperty("billingName");
  });

  it("turns an incomplete profile into the page that fixes it, for that organization", async () => {
    mutateAsync.mockRejectedValueOnce(profileRefusal(["LegalName", "BillingContactEmail"]));

    renderDialog("org-2");
    await userEvent.click(screen.getByRole("button", { name: "Subscribe" }));

    const notice = await screen.findByTestId("billing-profile-incomplete");
    expect(notice).toBeInTheDocument();
    expect(screen.getByTestId("billing-profile-missing")).toHaveTextContent(
      "legal name and billing contact email",
    );

    // The organization the refusal was about, not whichever one the URL is showing: fixing the
    // wrong profile leaves the retry refused for the same reason.
    expect(screen.getByRole("link", { name: /Complete the billing profile/ })).toHaveAttribute(
      "href",
      "/app/project-1/subscription/billing-profile?organizationId=org-2",
    );
  });

  it("sends a missing seller identity to the tenant's own page instead", async () => {
    mutateAsync.mockRejectedValueOnce(profileRefusal(["merchantLegalName"]));

    renderDialog();
    await userEvent.click(screen.getByRole("button", { name: "Subscribe" }));

    // The seller is the tenant, so this link carries no organization at all — and the subscriber's
    // own fields are not listed, because none of them are missing.
    expect(await screen.findByRole("link", { name: /Name the seller/ })).toHaveAttribute(
      "href",
      "/app/project-1/subscription/merchant-profile",
    );
    expect(screen.queryByTestId("billing-profile-missing")).not.toBeInTheDocument();
  });

  it("still reports a refusal it has no answer for", async () => {
    mutateAsync.mockRejectedValueOnce({
      status: 400,
      errors: {
        error: { code: "subscription_discount_unknown", message: "That code does not exist." },
      },
    });

    renderDialog();
    await userEvent.click(screen.getByRole("button", { name: "Subscribe" }));

    expect(await screen.findByText("That code does not exist.")).toBeInTheDocument();
    expect(screen.queryByTestId("billing-profile-incomplete")).not.toBeInTheDocument();
  });
});
