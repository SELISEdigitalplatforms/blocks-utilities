import { TEST_PROJECT_KEY, mockSuccessResponse, mockErrorResponse } from "@/test-utils/__mocks__";
import type {
  IMFAConfiguration,
  IGetConfigurationPayload,
  IMFAConfigurationSavePayload,
  IGenerateUserMFA_OtpPayload,
  IConfigureUserMFAPayload,
  ISetupUserTotpPayload,
  IVerifyMfaOtpPayload,
  IResendMfaOtpPayload,
  IDisableMFAPayload,
} from "../../mfa/models/mfa.model";

export { mockSuccessResponse, mockErrorResponse };

// ─── Mock IDs ─────────────────────────────────────────────────────────────────

export const MOCK_MFA_USER_ID = "mfa-user-a1b2-c3d4";
export const MOCK_MFA_ID = "mfa-id-e5f6-g7h8";

// ─── MFA Configuration Mocks ────────────────────────────────────────────────

export const mockMfaConfiguration: IMFAConfiguration = {
  enableMfa: true,
  mfaTemplate: { templateName: "Default MFA", templateId: "tpl-001" },
  projectKey: TEST_PROJECT_KEY,
  userMfaType: [1, 2],
};

export const mockGetMfaConfigPayload: IGetConfigurationPayload = {
  projectKey: TEST_PROJECT_KEY,
};

export const mockMfaConfigResponse = {
  ...mockMfaConfiguration,
};

export const mockSaveMfaConfigPayload: IMFAConfigurationSavePayload = {
  enableMfa: true,
  userMfaType: [1],
  mfaTemplate: { templateName: "Default MFA", templateId: "tpl-001" },
  projectKey: TEST_PROJECT_KEY,
};

// ─── MFA Operation Mocks ────────────────────────────────────────────────────

export const mockGenerateOtpPayload: IGenerateUserMFA_OtpPayload = {
  userId: MOCK_MFA_USER_ID,
  projectKey: TEST_PROJECT_KEY,
  mfaType: 1,
};

export const mockGenerateOtpResponse = {
  errors: null,
  isSuccess: true,
  mfaId: MOCK_MFA_ID,
};

export const mockConfigureUserMfaPayload: IConfigureUserMFAPayload = {
  userId: MOCK_MFA_USER_ID,
  mfaEnabled: true,
  userMfaType: 1,
  projectKey: TEST_PROJECT_KEY,
};

export const mockSetupTotpPayload: ISetupUserTotpPayload = {
  projectKey: TEST_PROJECT_KEY,
  id: MOCK_MFA_USER_ID,
};

export const mockSetupTotpResponse = {
  errors: null,
  isSuccess: true,
  qrImageUrl: "https://chart.googleapis.com/chart?cht=qr&chs=200x200&chl=test",
  qrCode: "JBSWY3DPEHPK3PXP",
};

export const mockVerifyOtpPayload: IVerifyMfaOtpPayload = {
  mfaId: MOCK_MFA_ID,
  verificationCode: "123456",
  authType: 1,
  projectKey: TEST_PROJECT_KEY,
};

export const mockVerifyOtpResponse = {
  errors: null,
  isSuccess: true,
  isValid: true,
  userId: MOCK_MFA_USER_ID,
};

export const mockResendOtpPayload: IResendMfaOtpPayload = {
  mfaId: MOCK_MFA_ID,
};

export const mockDisableMfaPayload: IDisableMFAPayload = {
  userId: MOCK_MFA_USER_ID,
  projectKey: TEST_PROJECT_KEY,
};
