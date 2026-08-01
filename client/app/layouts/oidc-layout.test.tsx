import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, renderHook, waitFor } from "@testing-library/react";
import { MemoryRouter, Routes, Route } from "react-router";
import { OidcLayout, OIDCProvider, useOIDCContext } from "./oidc-layout";

vi.mock("@/components/logo", () => ({
  Logo: ({ alt }: { alt: string }) => <img alt={alt} src="logo" />,
}));

const extractOIDCParams = vi.fn();
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  extractOIDCParams: () => extractOIDCParams(),
}));

const Consumer = () => {
  const ctx = useOIDCContext();
  return <div data-testid="ctx">{ctx.projectKey}</div>;
};

describe("oidc-layout", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    extractOIDCParams.mockReturnValue({ projectKey: "pk1", themeColor: "#abc" });
  });

  it("throws when useOIDCContext is used outside the provider", () => {
    expect(() => renderHook(() => useOIDCContext())).toThrow(
      "useOIDCContext must be used within OIDCProvider",
    );
  });

  it("provides merged params to consumers and persists them", async () => {
    render(
      <MemoryRouter>
        <OIDCProvider>
          <Consumer />
        </OIDCProvider>
      </MemoryRouter>,
    );
    await waitFor(() =>
      expect(screen.getByTestId("ctx")).toHaveTextContent("pk1"),
    );
    expect(localStorage.getItem("oidc-flow-params")).toContain("pk1");
  });

  it("renders the layout with the logo once loading resolves", async () => {
    render(
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route element={<OidcLayout />}>
            <Route path="/" element={<div>child-outlet</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await screen.findByAltText("OIDC Logo")).toBeInTheDocument();
  });
});
