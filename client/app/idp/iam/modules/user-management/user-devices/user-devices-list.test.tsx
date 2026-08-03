import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { UserDevicesList } from "./user-devices-list";

const future = new Date(Date.now() + 86400000).toISOString();
const past = new Date(Date.now() - 86400000).toISOString();

const rows = [
  {
    IpAddresses: "10.0.0.2",
    IssuedUtc: new Date(Date.now() - 3600000).toISOString(),
    ExpiresUtc: future,
    DeviceInformation: { Device: "mobile", Model: "", Browser: "Safari", OS: "iOS" },
  },
  {
    IpAddresses: "10.0.0.3",
    IssuedUtc: new Date(Date.now() - 7200000).toISOString(),
    ExpiresUtc: past,
    DeviceInformation: { Device: "", Model: "", Browser: "", OS: "" },
  },
];

describe("UserDevicesList", () => {
  it("renders the loading skeleton", () => {
    const { container } = render(<UserDevicesList isLoading data={[]} />);
    expect(container.querySelectorAll(".rounded-xl").length).toBeGreaterThan(0);
  });

  it("renders device rows with active and expired status", () => {
    render(<UserDevicesList isLoading={false} data={rows} />);
    expect(screen.getByText("10.0.0.2")).toBeInTheDocument();
    expect(screen.getByText("active")).toBeInTheDocument();
    expect(screen.getByText("expired")).toBeInTheDocument();
    expect(screen.getByText("Unknown Device")).toBeInTheDocument();
    expect(screen.getByText("Unknown Browser")).toBeInTheDocument();
  });

  it("shows the empty state", () => {
    render(<UserDevicesList isLoading={false} data={[]} />);
    expect(screen.getByText("No results.")).toBeInTheDocument();
  });
});
