import { TEST_PROJECT_KEY, mockSuccessResponse, mockErrorResponse } from "@/test-utils/__mocks__";
import type {
  ICaptchaConfig,
  IGetCaptchaConfigsPayload,
  ISaveCaptchaConfigsPayload,
  IEnableCaptchaConfigsStatusPayload,
} from "../../captcha/models/captcha";

export { mockSuccessResponse, mockErrorResponse };

// ─── Mock IDs ─────────────────────────────────────────────────────────────────

export const MOCK_CAPTCHA_ITEM_ID = "captcha-a1b2-c3d4";

// ─── Captcha Mocks ───────────────────────────────────────────────────────────

export const mockCaptchaConfig: ICaptchaConfig = {
  itemId: MOCK_CAPTCHA_ITEM_ID,
  createdDate: "2026-01-15T10:00:00Z",
  lastUpdatedDate: "2026-01-15T10:00:00Z",
  createdBy: "admin",
  lastUpdatedBy: "admin",
  organizationIds: [],
  tags: [],
  captchaKey: "6Le-mock-captcha-key",
  captchaSecret: "6Le-mock-captcha-secret",
  provider: "recaptcha",
  captchaGenerator: "EasyCaptchaGenerator",
  isEnable: true,
};

export const mockCaptchaConfigsResponse = {
  configurations: [mockCaptchaConfig],
};

export const mockEmptyCaptchaConfigsResponse = {
  configurations: [],
};

export const mockGetCaptchaConfigsPayload: IGetCaptchaConfigsPayload = {
  projectKey: TEST_PROJECT_KEY,
};

export const mockSaveCaptchaPayload: ISaveCaptchaConfigsPayload = {
  captchaKey: "6Le-new-captcha-key",
  captchaSecret: "6Le-new-captcha-secret",
  provider: "recaptcha",
  captchaGenerator: "EasyCaptchaGenerator",
  isEnable: true,
  projectKey: TEST_PROJECT_KEY,
};

export const mockUpdateCaptchaStatusPayload: IEnableCaptchaConfigsStatusPayload = {
  itemId: MOCK_CAPTCHA_ITEM_ID,
  isEnable: false,
  projectKey: TEST_PROJECT_KEY,
};
