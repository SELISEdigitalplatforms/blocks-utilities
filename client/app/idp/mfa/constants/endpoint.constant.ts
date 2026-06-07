import { API_BASES } from "@/constants/endpoint.constant";

// ─── MFA configuration endpoints (mfa.service — logic base) ─────────────────

const MFA_CONFIG_SUBPATH = "/MFA";

export const MFA_CONFIG_ENDPOINTS = {
  GET: `${API_BASES.LOGIC}${MFA_CONFIG_SUBPATH}/Get`,
  SAVE: `${API_BASES.LOGIC}${MFA_CONFIG_SUBPATH}/Save`,
} as const;

// ─── MFA endpoints (mfa.service — logic base) ──────────────────────────────

const MFA_SUBPATH = "/Mfa";
const MANAGEMENT_SUBPATH = "/Management";

export const MFA_ENDPOINTS = {
  GENERATE_OTP: `${API_BASES.LOGIC}${MFA_SUBPATH}/GenerateOTP`,
  CONFIGURE_USER_MFA: `${API_BASES.LOGIC}${MANAGEMENT_SUBPATH}/ConfigureUserMfa`,
  SETUP_TOTP: `${API_BASES.LOGIC}${MFA_SUBPATH}/SetUpTotp`,
  VERIFY_OTP: `${API_BASES.LOGIC}${MFA_SUBPATH}/VerifyOTP`,
  RESEND_OTP: `${API_BASES.LOGIC}${MFA_SUBPATH}/ResendOtp`,
  DISABLE_MFA: `${API_BASES.LOGIC}${MFA_SUBPATH}/DisableUserMfa`,
} as const;
