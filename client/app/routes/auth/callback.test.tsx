import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import LoginCallbackPage from "./callback";

let search = "code=c1&state=s1&tenant_id=t1";
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return {
    ...actual,
    useSearchParams: () => [new URLSearchParams(search)],
  };
});

const setAuthenticated = vi.fn();
vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: () => ({ setAuthenticated }),
}));

vi.mock("@/constants/endpoint.constant", () => ({
  API_BASES: { IDP: "https://api.example.com" },
}));

const renderPage = () =>
  render(
    <MemoryRouter>
      <LoginCallbackPage />
    </MemoryRouter>,
  );

describe("LoginCallbackPage", () => {
  const originalLocation = window.location;
  beforeEach(() => {
    vi.clearAllMocks();
    search = "code=c1&state=s1&tenant_id=t1";
    Object.defineProperty(window, "location", {
      configurable: true,
      value: { ...originalLocation, href: "" },
    });
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    Object.defineProperty(window, "location", {
      configurable: true,
      value: originalLocation,
    });
  });

  it("authenticates and redirects to the console on success", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true });
    vi.stubGlobal("fetch", fetchMock);
    renderPage();
    await waitFor(() => expect(setAuthenticated).toHaveBeenCalled());
    expect(window.location.href).toBe("/console");
    const calledUrl = fetchMock.mock.calls[0][0] as string;
    expect(calledUrl).toContain("code=c1");
    expect(calledUrl).toContain("tenant_id=t1");
  });

  it("redirects to the login page when the callback fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false }));
    renderPage();
    await waitFor(() =>
      expect(window.location.href).toBe("/login?error=callback_failed"),
    );
  });

  it("redirects with a callback error when the request throws", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("net")));
    renderPage();
    await waitFor(() =>
      expect(window.location.href).toBe("/login?error=callback_error"),
    );
  });
});
