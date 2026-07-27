import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MagicUrlStatusBadge } from "./magic-url-status-badge";
import { MagicUrl } from "@blocks-utilities/magic-url/models/magic-url.model";

const make = (over: Partial<MagicUrl> = {}): MagicUrl =>
  ({
    usageLimit: 0,
    usageCount: 0,
    isExpired: false,
    ...over,
  }) as MagicUrl;

const renderBadge = (item: MagicUrl) => {
  render(<MagicUrlStatusBadge item={item} />);
};

describe("MagicUrlStatusBadge", () => {
  it("prefers an explicit Active status", () => {
    renderBadge(make({ status: "Active" }));
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders Inactive and Disabled statuses verbatim", () => {
    const { unmount } = render(<MagicUrlStatusBadge item={make({ status: "Inactive" })} />);
    expect(screen.getByText("Inactive")).toBeInTheDocument();
    unmount();
    renderBadge(make({ status: "Disabled" }));
    expect(screen.getByText("Disabled")).toBeInTheDocument();
  });

  it("renders an explicit Expired status", () => {
    renderBadge(make({ status: "Expired" }));
    expect(screen.getByText("Expired")).toBeInTheDocument();
  });

  it("falls back to Disabled when manually disabled", () => {
    renderBadge(make({ expiredReason: "ManuallyDisabled" }));
    expect(screen.getByText("Disabled")).toBeInTheDocument();
  });

  it("falls back to Limit Exceeded from the reason", () => {
    renderBadge(make({ expiredReason: "UsageLimitExceeded" }));
    expect(screen.getByText("Limit Exceeded")).toBeInTheDocument();
  });

  it("falls back to Limit Exceeded when the usage count reaches the limit", () => {
    renderBadge(make({ usageLimit: 3, usageCount: 3 }));
    expect(screen.getByText("Limit Exceeded")).toBeInTheDocument();
  });

  it("falls back to Expired from the TimeExpired reason", () => {
    renderBadge(make({ expiredReason: "TimeExpired" }));
    expect(screen.getByText("Expired")).toBeInTheDocument();
  });

  it("falls back to Expired when the expiry date is in the past", () => {
    renderBadge(make({ expiryDate: "2000-01-01T00:00:00Z" }));
    expect(screen.getByText("Expired")).toBeInTheDocument();
  });

  it("falls back to Expired when isExpired is set", () => {
    renderBadge(make({ isExpired: true }));
    expect(screen.getByText("Expired")).toBeInTheDocument();
  });

  it("falls back to Unknown when nothing else matches", () => {
    renderBadge(make({ expiryDate: "2999-01-01T00:00:00Z" }));
    expect(screen.getByText("Unknown")).toBeInTheDocument();
  });
});
