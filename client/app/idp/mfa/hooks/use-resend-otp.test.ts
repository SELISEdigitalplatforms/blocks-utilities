import { renderHook, act } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { MOCK_MFA_ID } from "../../test-utils/__mocks__";

const mockMutateAsync = vi.fn();
vi.mock("./use-mfa-config", () => ({
  useResendMfaOTP: vi.fn(() => ({
    mutateAsync: mockMutateAsync,
  })),
}));

const mockReset = vi.fn();
vi.mock("@/hooks/use-count-down", () => ({
  useCountDown: vi.fn(() => ({
    remainingTime: 300,
    reset: mockReset,
  })),
}));

import { useResendOtp } from "./use-resend-otp";

describe("useResendOtp", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it("should return remainingTime and resend function", () => {
    const { result } = renderHook(() => useResendOtp({ mfaId: MOCK_MFA_ID }), {
      wrapper: createWrapper(),
    });

    expect(result.current.remainingTime).toBe(300);
    expect(typeof result.current.resend).toBe("function");
    expect(typeof result.current.reset).toBe("function");
  });

  it("should call mutateAsync and reset on resend", async () => {
    mockMutateAsync.mockResolvedValue(undefined);

    const { result } = renderHook(() => useResendOtp({ mfaId: MOCK_MFA_ID }), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      await result.current.resend();
    });

    expect(mockMutateAsync).toHaveBeenCalledWith({ mfaId: MOCK_MFA_ID });
    expect(mockReset).toHaveBeenCalled();
  });

  it("should not reset on resend failure", async () => {
    mockMutateAsync.mockRejectedValue(new Error("Failed"));

    const { result } = renderHook(() => useResendOtp({ mfaId: MOCK_MFA_ID }), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      await result.current.resend();
    });

    expect(mockMutateAsync).toHaveBeenCalledWith({ mfaId: MOCK_MFA_ID });
    expect(mockReset).not.toHaveBeenCalled();
  });
});
