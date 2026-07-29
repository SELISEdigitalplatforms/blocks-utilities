import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/genesis-os";

import { OIDC } from "./oidc";

const getAuthConfig = vi.fn();
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: (...a: unknown[]) => getAuthConfig(...a),
}));
vi.mock("./oidc-list", () => ({
  OidcList: () => <div data-testid="oidc-list" />,
}));

describe("OIDC", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getAuthConfig.mockReturnValue({ data: undefined, isLoading: false });
    useProjectStore.setState({ selectedProject: { tenantId: "tenant-9" } as never });
  });

  it("renders the OIDC list and queries with the current tenant", () => {
    render(<OIDC />);
    expect(screen.getByTestId("oidc-list")).toBeInTheDocument();
    expect(getAuthConfig).toHaveBeenCalledWith({ projectKey: "tenant-9" });
  });
});
