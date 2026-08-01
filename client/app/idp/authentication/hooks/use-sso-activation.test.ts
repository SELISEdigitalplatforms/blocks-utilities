import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { useSsoActivation } from "./use-sso-activation";

const mockPush = vi.fn();
const mockGet = vi.fn();
vi.mock("react-router", () => ({
  useNavigate: vi.fn(() => mockPush),
  useSearchParams: vi.fn(() => [{ get: mockGet }]),
}));

const mockSetAuthenticated = vi.fn();
vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: vi.fn(() => ({ setAuthenticated: mockSetAuthenticated })),
}));

const mockMutateAsync = vi.fn();
const mockReset = vi.fn();
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useSigninBySSO: vi.fn(() => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
    reset: mockReset,
  })),
}));

vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: vi.fn(),
}));

vi.mock("@/lib/error", () => ({
  isErrorWithErrors: vi.fn(() => false),
}));

describe("useSsoActivation", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.stubGlobal("sessionStorage", {
      getItem: vi.fn(() => null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });
  });

  it("should do nothing when code or state is missing", () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "" : key === "state" ? "" : null,
    );

    renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    expect(mockMutateAsync).not.toHaveBeenCalled();
  });

  it("should call signinBySSO and redirect to console on success", async () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "state-token" : null,
    );
    mockMutateAsync.mockResolvedValue({
      enable_mfa: false,
      access_token: "token",
    });

    renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    await waitFor(() =>
      expect(mockMutateAsync).toHaveBeenCalledWith({
        code: "auth-code",
        state: "state-token",
      }),
    );
    await waitFor(() => expect(mockSetAuthenticated).toHaveBeenCalled());
    expect(mockPush).toHaveBeenCalledWith("/services/language");
  });

  it("should redirect to MFA check when MFA is enabled", async () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "mfa-state" : null,
    );
    mockMutateAsync.mockResolvedValue({
      enable_mfa: true,
      mfaId: "mfa-123",
      mfaType: 1,
    });

    renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    await waitFor(() =>
      expect(mockPush).toHaveBeenCalledWith("/mfa-check?mfa_id=mfa-123&mfa_type=1"),
    );
  });

  it("should redirect to login on error", async () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "error-state" : null,
    );
    mockMutateAsync.mockRejectedValue(new Error("SSO failed"));

    renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith("/login"));
  });

  it("should return isPending state", () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "" : key === "state" ? "" : null,
    );

    const { result } = renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    expect(result.current).toHaveProperty("isPending");
  });

  it("redirects to the sso-activate path when a redirect url is returned", async () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "redir-state" : null,
    );
    mockMutateAsync.mockResolvedValue({
      sso_user_redirect_url:
        "https://idp.test/callback?username=jane&code=xyz",
    });

    renderHook(() => useSsoActivation(), { wrapper: createWrapper() });

    await waitFor(() =>
      expect(mockPush).toHaveBeenCalledWith(
        "/sso-activate?username=jane&code=xyz",
      ),
    );
    expect(mockSetAuthenticated).not.toHaveBeenCalled();
  });

  it("ignores a redirect url that lacks username or code", async () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "redir-state-2" : null,
    );
    mockMutateAsync.mockResolvedValue({
      sso_user_redirect_url: "https://idp.test/callback?foo=bar",
      enable_mfa: false,
    });

    renderHook(() => useSsoActivation(), { wrapper: createWrapper() });

    await waitFor(() =>
      expect(mockPush).toHaveBeenCalledWith("/services/language"),
    );
  });

  it("does nothing when the guard for this state is already set", () => {
    vi.stubGlobal("sessionStorage", {
      getItem: vi.fn(() => "1"),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "used-state" : null,
    );

    renderHook(() => useSsoActivation(), { wrapper: createWrapper() });

    expect(mockMutateAsync).not.toHaveBeenCalled();
  });

  it("shows a targeted toast when the account email is not found", async () => {
    const { showErrorToast } = await import("@/hooks/use-toast");
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "nf-state" : null,
    );
    mockMutateAsync.mockRejectedValue({
      error: { description: "jane@example.com user_not_found" },
    });

    renderHook(() => useSsoActivation(), { wrapper: createWrapper() });

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "There is no account with this email (jane@example.com).",
      }),
    );
    expect(mockPush).toHaveBeenCalledWith("/login");
  });

  it("shows a generic toast when state data is not found", async () => {
    const { showErrorToast } = await import("@/hooks/use-toast");
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "sd-state" : null,
    );
    mockMutateAsync.mockRejectedValue({ code: "state_data_not_found" });

    renderHook(() => useSsoActivation(), { wrapper: createWrapper() });

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "Something went wrong.",
      }),
    );
  });

  it("forwards structured error lists to the toast", async () => {
    const { showErrorToast } = await import("@/hooks/use-toast");
    const { isErrorWithErrors } = await import("@/lib/error");
    (isErrorWithErrors as unknown as ReturnType<typeof vi.fn>).mockReturnValue(
      true,
    );
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "list-state" : null,
    );
    mockMutateAsync.mockRejectedValue({ errors: ["bad thing"] });

    renderHook(() => useSsoActivation(), { wrapper: createWrapper() });

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: ["bad thing"] }),
    );
  });
});
