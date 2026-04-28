import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockUserServiceFactory,
  mockAccountActivationPayload,
  mockResendActivationPayload,
  mockAccountRecoverPayload,
  mockAccountResetPasswordPayload,
  mockActivationCodeValidationPayload,
  mockActivationCodeExpirationResponse,
} from "../../test-utils/__mocks__";
import { userService } from "@blocks-idp/iam/services/user.service";
import {
  useAccountActivation,
  useAccountResendActivation,
  useAccountDeactivate,
  useAccountRecover,
  useAccountResetPassword,
  useAccountActivationCodeExpiration,
} from "./use-account";

vi.mock("@blocks-idp/iam/services/user.service", () => mockUserServiceFactory());

describe("use-account hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useAccountActivation", () => {
    it("should activate account successfully", async () => {
      vi.mocked(userService.account.accountActivation).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAccountActivation(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockAccountActivationPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.account.accountActivation).toHaveBeenCalledWith(
        mockAccountActivationPayload,
      );
    });
  });

  describe("useAccountResendActivation", () => {
    it("should resend activation successfully", async () => {
      vi.mocked(userService.account.accountResendActivation).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAccountResendActivation(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockResendActivationPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.account.accountResendActivation).toHaveBeenCalledWith(
        mockResendActivationPayload,
      );
    });
  });

  describe("useAccountDeactivate", () => {
    it("should deactivate account successfully", async () => {
      vi.mocked(userService.accountDeactivate).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAccountDeactivate(), {
        wrapper: createWrapper(),
      });

      const payload = { userId: "user-123", projectKey: "test-key" };
      result.current.mutate(payload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.accountDeactivate).toHaveBeenCalledWith(payload);
    });
  });

  describe("useAccountRecover", () => {
    it("should recover account successfully", async () => {
      vi.mocked(userService.account.accountRecover).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAccountRecover(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockAccountRecoverPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.account.accountRecover).toHaveBeenCalledWith(mockAccountRecoverPayload);
    });
  });

  describe("useAccountResetPassword", () => {
    it("should reset password successfully", async () => {
      vi.mocked(userService.account.accountResetPassword).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAccountResetPassword(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockAccountResetPasswordPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.account.accountResetPassword).toHaveBeenCalledWith(
        mockAccountResetPasswordPayload,
      );
    });
  });

  describe("useAccountActivationCodeExpiration", () => {
    it("should check activation code expiration successfully", async () => {
      vi.mocked(userService.account.checkActivationCodeExpiration).mockResolvedValue(
        mockActivationCodeExpirationResponse,
      );

      const { result } = renderHook(() => useAccountActivationCodeExpiration(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockActivationCodeValidationPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.account.checkActivationCodeExpiration).toHaveBeenCalledWith(
        mockActivationCodeValidationPayload,
      );
    });
  });
});
