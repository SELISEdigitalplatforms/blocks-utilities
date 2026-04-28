import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockMfaServiceFactory,
  mockMfaConfigResponse,
  mockSaveMfaConfigPayload,
  mockConfigureUserMfaPayload,
  mockSetupTotpResponse,
  mockGenerateOtpPayload,
  mockGenerateOtpResponse,
  mockVerifyOtpPayload,
  mockVerifyOtpResponse,
  mockResendOtpPayload,
  mockDisableMfaPayload,
  MOCK_MFA_USER_ID,
} from "../../test-utils/__mocks__";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__";
import { mfaService } from "../services/mfa.service";
import {
  useGetMFAConfig,
  useSaveMFAConfig,
  useConfigureUserMFA,
  useGetTotp,
  useGenerateUserMfaOTP,
  useVerifyMfaOTP,
  useResendMfaOTP,
  useDisableMfa,
} from "./use-mfa-config";

vi.mock("@blocks-idp/mfa/services/mfa.service", () => mockMfaServiceFactory());

describe("use-mfa-config hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetMFAConfig", () => {
    it("should fetch MFA configuration successfully", async () => {
      vi.mocked(mfaService.getConfigurations).mockResolvedValue(mockMfaConfigResponse);

      const { result } = renderHook(() => useGetMFAConfig({ projectKey: TEST_PROJECT_KEY }), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockMfaConfigResponse);
      expect(mfaService.getConfigurations).toHaveBeenCalledWith({
        projectKey: TEST_PROJECT_KEY,
      });
    });
  });

  describe("useSaveMFAConfig", () => {
    it("should save MFA configuration successfully", async () => {
      vi.mocked(mfaService.saveMFAConfiguration).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSaveMFAConfig(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveMfaConfigPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mfaService.saveMFAConfiguration).toHaveBeenCalledWith(mockSaveMfaConfigPayload);
    });
  });

  describe("useConfigureUserMFA", () => {
    it("should configure user MFA successfully", async () => {
      vi.mocked(mfaService.configureUserMFA).mockResolvedValue(undefined as never);

      const { result } = renderHook(
        () => useConfigureUserMFA({ id: MOCK_MFA_USER_ID, projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      result.current.mutate(mockConfigureUserMfaPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mfaService.configureUserMFA).toHaveBeenCalledWith(mockConfigureUserMfaPayload);
    });
  });

  describe("useGetTotp", () => {
    it("should fetch TOTP setup data successfully", async () => {
      vi.mocked(mfaService.setupUserTotp).mockResolvedValue(mockSetupTotpResponse);

      const { result } = renderHook(
        () => useGetTotp({ id: MOCK_MFA_USER_ID, projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockSetupTotpResponse);
      expect(mfaService.setupUserTotp).toHaveBeenCalledWith({
        id: MOCK_MFA_USER_ID,
        projectKey: TEST_PROJECT_KEY,
      });
    });
  });

  describe("useGenerateUserMfaOTP", () => {
    it("should generate OTP successfully", async () => {
      vi.mocked(mfaService.generateUserMfaOTP).mockResolvedValue(mockGenerateOtpResponse);

      const { result } = renderHook(() => useGenerateUserMfaOTP(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockGenerateOtpPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mfaService.generateUserMfaOTP).toHaveBeenCalledWith(mockGenerateOtpPayload);
    });
  });

  describe("useVerifyMfaOTP", () => {
    it("should verify OTP successfully", async () => {
      vi.mocked(mfaService.verifyOtp).mockResolvedValue(mockVerifyOtpResponse);

      const { result } = renderHook(
        () => useVerifyMfaOTP({ id: MOCK_MFA_USER_ID, projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      result.current.mutate(mockVerifyOtpPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mfaService.verifyOtp).toHaveBeenCalledWith(mockVerifyOtpPayload);
    });
  });

  describe("useResendMfaOTP", () => {
    it("should resend OTP successfully", async () => {
      vi.mocked(mfaService.resendOtp).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useResendMfaOTP(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockResendOtpPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mfaService.resendOtp).toHaveBeenCalledWith(mockResendOtpPayload);
    });
  });

  describe("useDisableMfa", () => {
    it("should disable MFA successfully", async () => {
      vi.mocked(mfaService.disableMFA).mockResolvedValue(undefined as never);

      const { result } = renderHook(
        () => useDisableMfa({ id: MOCK_MFA_USER_ID, projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      result.current.mutate(mockDisableMfaPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mfaService.disableMFA).toHaveBeenCalledWith(mockDisableMfaPayload);
    });
  });
});
