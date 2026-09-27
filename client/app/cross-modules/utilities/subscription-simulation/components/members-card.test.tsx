import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

const assignMembers = vi.fn();

vi.mock("../services/subscription-simulation.service", async () => {
  const actual = await vi.importActual<
    typeof import("../services/subscription-simulation.service")
  >("../services/subscription-simulation.service");

  return {
    ...actual,
    subscriptionSimulationService: {
      assignMembers: (...args: unknown[]) => assignMembers(...args),
    },
  };
});

import { AssignMembersDialog } from "./members-card";

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
