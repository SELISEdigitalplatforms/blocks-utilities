import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockAuthServiceFactory,
  mockOAuthServiceFactory,
  mockSigninPayload,
  mockSigninResponse,
  mockSignupPayload,
  mockSignupResponse,
  mockVerifyMfaPayload,
  mockVerifyMfaResponse,
  mockSigninBySSOPayload,
  mockSigninBySSOResponse,
} from "../../test-utils/__mocks__";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import {
  useSigninByEmail,
  useSigninBySSO,
  useVerifyMfa,
  useLogout,
  useSignupByEmail,
} from "./use-auth";

vi.mock("@blocks-idp/authentication/services/auth.service", () => mockAuthServiceFactory());
vi.mock("@blocks-idp/authentication/services/oauth.service", () => mockOAuthServiceFactory());

describe("use-auth hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useSigninByEmail", () => {
    it("should call authService.signinByEmail", async () => {
      vi.mocked(authService.signinByEmail).mockResolvedValue(mockSigninResponse as never);
      const { result } = renderHook(() => useSigninByEmail(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSigninPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(authService.signinByEmail).toHaveBeenCalledWith(mockSigninPayload);
    });
  });

  describe("useSigninBySSO", () => {
    it("should call oauthService.signinBySSO", async () => {
      vi.mocked(oauthService.signinBySSO).mockResolvedValue(mockSigninBySSOResponse);
      const { result } = renderHook(() => useSigninBySSO(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSigninBySSOPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(oauthService.signinBySSO).toHaveBeenCalledWith(mockSigninBySSOPayload);
    });
  });

  describe("useVerifyMfa", () => {
    it("should call authService.verifyMfa", async () => {
      vi.mocked(authService.verifyMfa).mockResolvedValue(mockVerifyMfaResponse);
      const { result } = renderHook(() => useVerifyMfa(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockVerifyMfaPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(authService.verifyMfa).toHaveBeenCalledWith(mockVerifyMfaPayload);
    });
  });

  describe("useLogout", () => {
    it("should call authService.logout", async () => {
      vi.mocked(authService.logout).mockResolvedValue(undefined);
      const { result } = renderHook(() => useLogout(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(undefined);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(authService.logout).toHaveBeenCalled();
    });
  });

  describe("useSignupByEmail", () => {
    it("should call authService.signupByEmail", async () => {
      vi.mocked(authService.signupByEmail).mockResolvedValue(mockSignupResponse);
      const { result } = renderHook(() => useSignupByEmail(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSignupPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(authService.signupByEmail).toHaveBeenCalledWith(mockSignupPayload);
    });
  });
});
