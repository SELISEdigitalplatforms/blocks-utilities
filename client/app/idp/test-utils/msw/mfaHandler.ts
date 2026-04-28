import { http, HttpResponse, type JsonBodyType } from "msw";
import {
  mockMfaConfigResponse,
  mockGenerateOtpResponse,
  mockSetupTotpResponse,
  mockVerifyOtpResponse,
} from "../__mocks__/mfa.data.mock";
import { mockSuccessResponse } from "@/test-utils/__mocks__";
import { MFA_CONFIG_ENDPOINTS, MFA_ENDPOINTS } from "../../mfa/constants/endpoint.constant";

// ─── Endpoint Patterns ────────────────────────────────────────────────────────

// MFA Config
const GET_MFA_CONFIG_PATTERN = new RegExp(`${MFA_CONFIG_ENDPOINTS.GET}\\?`);
const SAVE_MFA_CONFIG_PATTERN = new RegExp(MFA_CONFIG_ENDPOINTS.SAVE);

// MFA Operations
const GENERATE_OTP_PATTERN = new RegExp(MFA_ENDPOINTS.GENERATE_OTP);
const CONFIGURE_USER_MFA_PATTERN = new RegExp(MFA_ENDPOINTS.CONFIGURE_USER_MFA);
const SETUP_TOTP_PATTERN = new RegExp(`${MFA_ENDPOINTS.SETUP_TOTP}\\?`);
const VERIFY_OTP_PATTERN = new RegExp(MFA_ENDPOINTS.VERIFY_OTP);
const RESEND_OTP_PATTERN = new RegExp(MFA_ENDPOINTS.RESEND_OTP);
const DISABLE_MFA_PATTERN = new RegExp(MFA_ENDPOINTS.DISABLE_MFA);

// ─── Default Handlers (happy-path) ───────────────────────────────────────────

export const mfaHandlers = [
  // MFA Config
  http.get(GET_MFA_CONFIG_PATTERN, () => HttpResponse.json(mockMfaConfigResponse)),
  http.post(SAVE_MFA_CONFIG_PATTERN, () => HttpResponse.json(mockSuccessResponse)),

  // MFA Operations
  http.post(GENERATE_OTP_PATTERN, () => HttpResponse.json(mockGenerateOtpResponse)),
  http.post(CONFIGURE_USER_MFA_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
  http.get(SETUP_TOTP_PATTERN, () => HttpResponse.json(mockSetupTotpResponse)),
  http.post(VERIFY_OTP_PATTERN, () => HttpResponse.json(mockVerifyOtpResponse)),
  http.post(RESEND_OTP_PATTERN, () => HttpResponse.json(mockVerifyOtpResponse)),
  http.post(DISABLE_MFA_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
];

// ─── Per-Test Override Factories ──────────────────────────────────────────────

// MFA Config
export const getMfaConfigHandler = (response: JsonBodyType = mockMfaConfigResponse) =>
  http.get(GET_MFA_CONFIG_PATTERN, () => HttpResponse.json(response));

export const getMfaConfigErrorHandler = (status = 500) =>
  http.get(GET_MFA_CONFIG_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const saveMfaConfigHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(SAVE_MFA_CONFIG_PATTERN, () => HttpResponse.json(response));

export const saveMfaConfigErrorHandler = (status = 500) =>
  http.post(SAVE_MFA_CONFIG_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

// MFA Operations
export const generateOtpHandler = (response: JsonBodyType = mockGenerateOtpResponse) =>
  http.post(GENERATE_OTP_PATTERN, () => HttpResponse.json(response));

export const generateOtpErrorHandler = (status = 500) =>
  http.post(GENERATE_OTP_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const configureUserMfaHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(CONFIGURE_USER_MFA_PATTERN, () => HttpResponse.json(response));

export const setupTotpHandler = (response: JsonBodyType = mockSetupTotpResponse) =>
  http.get(SETUP_TOTP_PATTERN, () => HttpResponse.json(response));

export const setupTotpErrorHandler = (status = 500) =>
  http.get(SETUP_TOTP_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const verifyOtpHandler = (response: JsonBodyType = mockVerifyOtpResponse) =>
  http.post(VERIFY_OTP_PATTERN, () => HttpResponse.json(response));

export const verifyOtpErrorHandler = (status = 500) =>
  http.post(VERIFY_OTP_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const resendOtpHandler = (response: JsonBodyType = mockVerifyOtpResponse) =>
  http.post(RESEND_OTP_PATTERN, () => HttpResponse.json(response));

export const disableMfaHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(DISABLE_MFA_PATTERN, () => HttpResponse.json(response));

export const disableMfaErrorHandler = (status = 500) =>
  http.post(DISABLE_MFA_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );
