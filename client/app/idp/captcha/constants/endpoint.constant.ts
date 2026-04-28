import { API_BASES } from "@/constants/endpoint.constant";

// ─── Captcha endpoints (captcha.service) ────────────────────────────────────

const CAPTCHA_SUBPATH = "/Captcha";

export const CAPTCHA_ENDPOINTS = {
  GETS: `${API_BASES.CLOUD_CONFIGURATION}${CAPTCHA_SUBPATH}/Gets`,
  SAVE: `${API_BASES.CLOUD_CONFIGURATION}${CAPTCHA_SUBPATH}/Save`,
  UPDATE_STATUS: `${API_BASES.CLOUD_CONFIGURATION}${CAPTCHA_SUBPATH}/UpdateStatus`,
} as const;
