import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { UserHistoryList } from "./user-history-list";

const rows = [
  {
    Event: "issued_refresh_token",
    LastUpdatedDate: new Date(Date.now() - 3600000).toISOString(),
    IpAddresses: "10.0.0.1",
    DeviceInformation: {
      Device: "desktop",
      Model: "pro",
      Browser: "Chrome",
      OS: "Linux",
    },
  },
];

describe("UserHistoryList", () => {
  it("renders the loading skeleton", () => {
    const { container } = render(<UserHistoryList isLoading data={[]} />);
    expect(container.querySelectorAll(".rounded-xl").length).toBeGreaterThan(0);
  });

  it("renders a history row with mapped event and device info", () => {
    render(<UserHistoryList isLoading={false} data={rows} />);
    expect(screen.getByText("Refresh Token Issued")).toBeInTheDocument();
    expect(screen.getByText("10.0.0.1")).toBeInTheDocument();
    expect(screen.getByText(/Desktop Pro/)).toBeInTheDocument();
    expect(screen.getByText(/Chrome/)).toBeInTheDocument();
  });

  it("shows the empty state", () => {
    render(<UserHistoryList isLoading={false} data={[]} />);
    expect(screen.getByText("No results.")).toBeInTheDocument();
  });
});
