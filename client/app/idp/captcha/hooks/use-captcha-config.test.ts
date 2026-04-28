import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockCaptchaServiceFactory,
  mockGetCaptchaConfigsPayload,
  mockCaptchaConfigsResponse,
  mockSaveCaptchaPayload,
  mockUpdateCaptchaStatusPayload,
} from "../../test-utils/__mocks__";
import { captchaService } from "../services/captcha.service";
import {
  useGetCaptchaConfigs,
  useSaveCaptcha,
  useToggleCaptchaConfigStatus,
} from "./use-captcha-config";

vi.mock("@blocks-idp/captcha/services/captcha.service", () => mockCaptchaServiceFactory());

describe("use-captcha-config hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetCaptchaConfigs", () => {
    it("should fetch captcha configs successfully", async () => {
      vi.mocked(captchaService.getCaptchaConfigs).mockResolvedValue(mockCaptchaConfigsResponse);

      const { result } = renderHook(() => useGetCaptchaConfigs(mockGetCaptchaConfigsPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockCaptchaConfigsResponse);
      expect(captchaService.getCaptchaConfigs).toHaveBeenCalledWith(mockGetCaptchaConfigsPayload);
    });
  });

  describe("useSaveCaptcha", () => {
    it("should save captcha config successfully", async () => {
      vi.mocked(captchaService.saveCaptcha).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSaveCaptcha(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveCaptchaPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(captchaService.saveCaptcha).toHaveBeenCalledWith(mockSaveCaptchaPayload);
    });
  });

  describe("useToggleCaptchaConfigStatus", () => {
    it("should update captcha config status successfully", async () => {
      vi.mocked(captchaService.updateCaptchaConfigStatus).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useToggleCaptchaConfigStatus(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockUpdateCaptchaStatusPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(captchaService.updateCaptchaConfigStatus).toHaveBeenCalledWith(
        mockUpdateCaptchaStatusPayload,
      );
    });
  });
});
