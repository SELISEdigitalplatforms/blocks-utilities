import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ServiceGroupCard } from "./service-group-card";

vi.mock("./endpoint-row", () => ({
  EndpointRow: ({ endpoint }: { endpoint: { itemId: string } }) => (
    <div data-testid="endpoint-row">{endpoint.itemId}</div>
  ),
}));
vi.mock("./security-presets-popover", () => ({
  SecurityPresetsPopover: () => <div data-testid="presets" />,
}));

const endpoints = [
  { itemId: "e1", route: "/a" },
  { itemId: "e2", route: "/b" },
] as never;

const baseProps = {
  controller: "UserController",
  endpoints,
  selectedIds: new Set<string>(),
  onSelectEndpoint: vi.fn(),
  onSelectGroup: vi.fn(),
  onToggleMfa: vi.fn(),
  onToggleCaptcha: vi.fn(),
  onBulkGroupMfa: vi.fn(),
  onBulkGroupCaptcha: vi.fn(),
};

describe("ServiceGroupCard", () => {
  beforeEach(() => vi.clearAllMocks());

  it("renders the controller name and endpoint count", () => {
    render(<ServiceGroupCard {...baseProps} />);
    expect(screen.getByText("UserController")).toBeInTheDocument();
    expect(screen.getByText("2 Endpoints")).toBeInTheDocument();
  });

  it("selects the whole group when the header checkbox is toggled", async () => {
    const onSelectGroup = vi.fn();
    const user = userEvent.setup();
    render(<ServiceGroupCard {...baseProps} onSelectGroup={onSelectGroup} />);

    await user.click(screen.getByRole("checkbox"));
    expect(onSelectGroup).toHaveBeenCalledWith(["e1", "e2"], true);
  });

  it("reveals the endpoint rows when expanded", async () => {
    const user = userEvent.setup();
    render(<ServiceGroupCard {...baseProps} />);

    await user.click(screen.getByText("UserController"));
    expect(await screen.findAllByTestId("endpoint-row")).toHaveLength(2);
  });
});
