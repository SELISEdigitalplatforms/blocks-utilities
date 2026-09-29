import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";

const toast = vi.hoisted(() => vi.fn());
vi.mock("@/hooks/use-toast", () => ({ toast }));

const assignMembers = vi.fn();
const listMemberBasedSubscriptions = vi.fn();
const listMembers = vi.fn();
const cancel = vi.fn();

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

vi.mock("../services/subscription-simulation.service", async () => {
  const actual = await vi.importActual<
    typeof import("../services/subscription-simulation.service")
  >("../services/subscription-simulation.service");

  return {
    ...actual,
    subscriptionSimulationService: {
      assignMembers: (...args: unknown[]) => assignMembers(...args),
      listMemberBasedSubscriptions: (...args: unknown[]) => listMemberBasedSubscriptions(...args),
      listMembers: (...args: unknown[]) => listMembers(...args),
      cancel: (...args: unknown[]) => cancel(...args),
    },
  };
});

import { AssignMembersDialog, MembersCard } from "./members-card";

const renderDialog = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <AssignMembersDialog subscriptionId="sub-1" open onOpenChange={vi.fn()} />
    </QueryClientProvider>,
  );

describe("AssignMembersDialog", () => {
  beforeEach(() => {
    assignMembers.mockReset();
  });

  it("sends every name in one call", async () => {
    assignMembers.mockResolvedValue({ subscriptionId: "sub-1", assigned: [], refused: [] });
    renderDialog();

    fireEvent.change(screen.getByLabelText("User ids"), {
      target: { value: "u1\nu2, u3" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Assign 3 people" }));

    await waitFor(() =>
      expect(assignMembers).toHaveBeenCalledWith("sub-1", { userIds: ["u1", "u2", "u3"] }),
    );
  });

  it("counts a name given twice once, as the server places it once", async () => {
    renderDialog();

    fireEvent.change(screen.getByLabelText("User ids"), { target: { value: "u1\nu2\nu1" } });

    expect(screen.getByRole("button", { name: "Assign 2 people" })).toBeInTheDocument();
  });

  /**
   * The whole reason assignment answers per person. A batch that seats two and refuses one is a
   * success, and a toast saying so would hide who missed out and why.
   */
  it("shows who was placed and who was refused, with each refusal's reason", async () => {
    assignMembers.mockResolvedValue({
      subscriptionId: "sub-1",
      assigned: [
        { subscriptionId: "sub-1", userId: "u1", seatNumber: 1, assignedAtUtc: "", releasedAtUtc: null },
        { subscriptionId: "sub-1", userId: "u2", seatNumber: 2, assignedAtUtc: "", releasedAtUtc: null },
      ],
      refused: [
        {
          userId: "u3",
          reasonCode: "subscription_member_limit_reached",
          reason: "Every place is taken.",
        },
      ],
    });
    renderDialog();

    fireEvent.change(screen.getByLabelText("User ids"), { target: { value: "u1\nu2\nu3" } });
    fireEvent.click(screen.getByRole("button", { name: "Assign 3 people" }));

    const assigned = await screen.findByRole("region", { name: "Assigned" });
    const refused = screen.getByRole("region", { name: "Refused" });

    expect(assigned).toHaveTextContent("u1");
    expect(assigned).toHaveTextContent("place 2");
    expect(refused).toHaveTextContent("u3");
    expect(refused).toHaveTextContent("subscription_member_limit_reached");
    expect(refused).toHaveTextContent("Every place is taken.");
  });
});

describe("MembersCard", () => {
  const holder = (userId: string, seatNumber: number) => ({
    subscriptionId: "sub-1",
    userId,
    seatNumber,
    assignedAtUtc: "",
    releasedAtUtc: null,
  });

  /**
   * Found testing in the portal: with a cut from five to four scheduled and four people on, the
   * card showed place 5 as empty with Assign enabled, and assigning to it was then refused.
   */
  it("shows a place a scheduled decrease is removing as removed, not empty, and offers no assign", async () => {
    listMemberBasedSubscriptions.mockResolvedValue([
      { subscriptionId: "sub-1", status: "Active", planCode: "u", planName: "user-test-4", checkoutUrl: null },
    ]);
    listMembers.mockResolvedValue({
      subscriptionId: "sub-1",
      purchased: 5,
      held: 4,
      available: 0,
      scheduledPlaces: 4,
      scheduledAtUtc: "2026-10-29T04:05:00Z",
      seats: [holder("a", 1), holder("b", 2), holder("c", 3), holder("d", 4)],
    });

    render(
      <QueryClientProvider client={new QueryClient()}>
        <MembersCard plans={[]} organizationId={undefined} />
      </QueryClientProvider>,
    );

    expect(await screen.findByText(/4 of 5 places held · drops to 4 on/)).toBeInTheDocument();
    expect(screen.getByText(/Removed on/)).toBeInTheDocument();
    expect(screen.queryByText("Empty")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Assign/ })).toBeDisabled();
  });
});

describe("MembersCard cancel", () => {
  /**
   * Found testing in the portal: current never returns a user-wise subscription, so the current
   * subscription card could never offer to cancel one, and nothing else did.
   */
  it("offers to cancel each user-wise subscription", async () => {
    listMemberBasedSubscriptions.mockResolvedValue([
      { subscriptionId: "sub-1", status: "Active", planCode: "u", planName: "user-test-4", checkoutUrl: null },
    ]);
    listMembers.mockResolvedValue({
      subscriptionId: "sub-1",
      purchased: 2,
      held: 0,
      available: 2,
      seats: [],
    });

    render(
      <QueryClientProvider client={new QueryClient()}>
        <MembersCard plans={[]} organizationId={undefined} />
      </QueryClientProvider>,
    );

    fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));

    expect(await screen.findByRole("dialog", { name: "Cancel user-test-4" })).toBeInTheDocument();
  });
});

describe("MembersCard place usage", () => {
  it("shows what each place has spent of every meter and pace", async () => {
    listMemberBasedSubscriptions.mockResolvedValue([
      { subscriptionId: "sub-1", status: "Active", planCode: "u", planName: "user-test-4", checkoutUrl: null },
    ]);
    listMembers.mockResolvedValue({
      subscriptionId: "sub-1",
      purchased: 2,
      held: 1,
      available: 1,
      seats: [
        { subscriptionId: "sub-1", userId: "user-a", seatNumber: 1, assignedAtUtc: "", releasedAtUtc: null },
      ],
      usage: [
        {
          seatNumber: 1,
          userId: "user-a",
          meterKey: "tokens",
          unitLabel: "tokens",
          quantityScale: 0,
          included: 100,
          used: 12,
          remaining: 88,
          overage: 0,
          periodEndUtc: "2026-10-29T00:00:00Z",
          updatedAtUtc: "2026-09-29T00:00:00Z",
          subLimits: [
            {
              window: "Hour", windowCount: 5, rolling: true, behaviour: "Refuse", quantity: 10,
              used: 11, remaining: 0, exceeded: true, windowStartUtc: "", windowEndUtc: null,
            },
          ],
        },
      ],
    });

    render(
      <QueryClientProvider client={new QueryClient()}>
        <MembersCard plans={[]} organizationId={undefined} />
      </QueryClientProvider>,
    );

    const line = (await screen.findByText("tokens")).closest("li");
    expect(line).toHaveTextContent("tokens 12 / 100 tokens · 11 / 10 in any 5 hours");
    expect(screen.getByText(/11 \/ 10 in any 5 hours/)).toHaveClass("text-destructive");
  });
});

describe("MembersCard per-person actions", () => {
  /**
   * current never returns a user-wise subscription, so the current subscription card could never
   * offer these for one — and a plan change, a withdrawn cancellation or a closed usage period on
   * one could not be tried at all.
   */
  it("offers a plan change, the harness actions and a way back from a scheduled cancellation", async () => {
    listMemberBasedSubscriptions.mockResolvedValue([
      {
        subscriptionId: "sub-1",
        status: "Active",
        planCode: "u",
        planName: "user-test-5",
        checkoutUrl: null,
        cancelAtPeriodEnd: true,
        currentPeriodEndUtc: "2026-10-29T00:00:00Z",
        pendingPlanChange: null,
        quantities: [],
      },
    ]);
    listMembers.mockResolvedValue({ subscriptionId: "sub-1", purchased: 1, held: 0, available: 1, seats: [] });

    render(
      <QueryClientProvider client={new QueryClient()}>
        <MembersCard plans={[]} organizationId={undefined} />
      </QueryClientProvider>,
    );

    expect(await screen.findByRole("button", { name: "Keep subscription" })).toBeInTheDocument();
    expect(screen.getByText(/Cancellation scheduled for/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close usage period" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Run due jobs" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Change plan" }));
    expect(await screen.findByRole("dialog", { name: "Change plan" })).toBeInTheDocument();
  });
});

describe("MembersCard immediate cancel", () => {
  /**
   * An ended subscription drops out of the card, so whether its places were released could not be
   * seen anywhere. Reading the roster back is how the harness shows it.
   */
  it("reads the places back once it has ended and says whether any are still held", async () => {
    listMemberBasedSubscriptions.mockResolvedValue([
      { subscriptionId: "sub-1", status: "Active", planCode: "u", planName: "user-test-5", checkoutUrl: null },
    ]);
    listMembers.mockResolvedValue({ subscriptionId: "sub-1", purchased: 3, held: 0, available: 3, seats: [] });
    cancel.mockResolvedValue({
      subscriptionId: "sub-1",
      status: "Canceled",
      planName: "user-test-5",
      currentPeriodEndUtc: "2026-10-29T00:00:00Z",
    });

    render(
      <QueryClientProvider client={new QueryClient()}>
        <MembersCard plans={[]} organizationId={undefined} />
      </QueryClientProvider>,
    );

    fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
    fireEvent.click(await screen.findByRole("radio", { name: /Cancel immediately/ }));
    fireEvent.click(screen.getByRole("button", { name: "Cancel subscription" }));

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(expect.objectContaining({ title: "Every place released" })),
    );
  });
});
