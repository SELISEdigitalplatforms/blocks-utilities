import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { EndpointRow } from "./endpoint-row";

const endpoint = {
  itemId: "e1",
  method: "get",
  endpoint: "/api/things",
  description: "List things",
  isMfaRequired: false,
  isCaptchaRequired: false,
} as never;

const setup = (over: Record<string, unknown> = {}) => {
  const props = {
    endpoint,
    isSelected: false,
    onSelect: vi.fn(),
    onToggleMfa: vi.fn(),
    onToggleCaptcha: vi.fn(),
    ...over,
  };
  render(<EndpointRow {...(props as never)} />);
  return props;
};

describe("EndpointRow", () => {
  it("renders the method badge, path and description", () => {
    setup();
    expect(screen.getByText("GET")).toBeInTheDocument();
    expect(screen.getByText("/api/things")).toBeInTheDocument();
    expect(screen.getByText("List things")).toBeInTheDocument();
    expect(screen.queryByText("Critical")).not.toBeInTheDocument();
  });

  it("marks DELETE endpoints as critical", () => {
    setup({ endpoint: { ...endpoint, method: "delete" } });
    expect(screen.getByText("Critical")).toBeInTheDocument();
  });

  it("invokes onSelect when the checkbox is toggled", async () => {
    const user = userEvent.setup();
    const props = setup();
    await user.click(screen.getByRole("checkbox"));
    expect(props.onSelect).toHaveBeenCalledWith("e1", true);
  });

  it("invokes the MFA and Captcha toggles", async () => {
    const user = userEvent.setup();
    const props = setup();
    const switches = screen.getAllByRole("switch");
    await user.click(switches[0]);
    await user.click(switches[1]);
    expect(props.onToggleMfa).toHaveBeenCalledWith(endpoint, true);
    expect(props.onToggleCaptcha).toHaveBeenCalledWith(endpoint, true);
  });
});
