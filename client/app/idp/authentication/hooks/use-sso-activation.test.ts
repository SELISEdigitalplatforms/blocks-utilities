import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { useSsoActivation } from "./use-sso-activation";

const mockPush = vi.fn();
const mockReplace = vi.fn();
const mockGet = vi.fn();
vi.mock("react-router-dom", () => ({
  useNavigate: vi.fn(() => mockPush),
  useSearchParams: vi.fn(() => [{ get: mockGet }]),
}));

const mockSetAuthenticated = vi.fn();
vi.mock("@/store/useAuthStore", () => ({
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
    expect(mockPush).toHaveBeenCalledWith("/console");
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

    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith("/login"));
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
});
