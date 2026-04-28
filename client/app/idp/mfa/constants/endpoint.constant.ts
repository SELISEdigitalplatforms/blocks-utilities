import { API_BASES } from "@/constants/endpoint.constant";

// ─── MFA configuration endpoints (mfa.service — cloud config) ──────────────

const MFA_CONFIG_SUBPATH = "/MFA";

export const MFA_CONFIG_ENDPOINTS = {
  GET: `${API_BASES.CLOUD_CONFIGURATION}${MFA_CONFIG_SUBPATH}/Get`,
  SAVE: `${API_BASES.CLOUD_CONFIGURATION}${MFA_CONFIG_SUBPATH}/Save`,
} as const;

// ─── MFA endpoints (mfa.service — IDP & MFA bases) ─────────────────────────

const MFA_SUBPATH = "/Mfa";
const MANAGEMENT_SUBPATH = "/Management";

export const MFA_ENDPOINTS = {
  GENERATE_OTP: `${API_BASES.IDP}${MFA_SUBPATH}/GenerateOTP`,
  CONFIGURE_USER_MFA: `${API_BASES.MFA}${MANAGEMENT_SUBPATH}/ConfigureUserMfa`,
  SETUP_TOTP: `${API_BASES.IDP}${MFA_SUBPATH}/SetUpTotp`,
  VERIFY_OTP: `${API_BASES.IDP}${MFA_SUBPATH}/VerifyOTP`,
  RESEND_OTP: `${API_BASES.IDP}${MFA_SUBPATH}/ResendOtp`,
  DISABLE_MFA: `${API_BASES.IDP}${MFA_SUBPATH}/DisableUserMfa`,
} as const;
