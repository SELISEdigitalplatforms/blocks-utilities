import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/blocks-kit";

import { ClientCredentials } from "./client-credentials";

const getAuthConfig = vi.fn();
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: (...a: unknown[]) => getAuthConfig(...a),
}));
vi.mock("./client-credentials-list", () => ({
  ClientCredentialList: () => <div data-testid="cc-list" />,
}));

describe("ClientCredentials", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tenant-3" } as never });
  });

  it("renders the credential list while loading", () => {
    getAuthConfig.mockReturnValue({ isLoading: true });
    render(<ClientCredentials />);
    expect(screen.getByTestId("cc-list")).toBeInTheDocument();
    expect(getAuthConfig).toHaveBeenCalledWith({ projectKey: "tenant-3" });
  });

  it("renders the credential list once loaded", () => {
    getAuthConfig.mockReturnValue({ isLoading: false });
    render(<ClientCredentials />);
    expect(screen.getByTestId("cc-list")).toBeInTheDocument();
  });
});
