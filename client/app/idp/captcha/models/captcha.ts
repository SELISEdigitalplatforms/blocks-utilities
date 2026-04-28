export const CAPTCHA_PROVIDERS = {
  recaptcha: { value: "recaptcha", label: "Google reCAPTCHA" },
  hcaptcha: { value: "hcaptcha", label: "hCAPTCHA" },
} as const;
export const CAPTCHA_GENERATOR_TYPE = {
  EasyCaptchaGenerator: { value: "EasyCaptchaGenerator", label: "Easy" },
  HardCaptchaGenerator: { value: "HardCaptchaGenerator", label: "Hard" },
} as const;

export type CAPTCHA_PROVIDERS_KEY = keyof typeof CAPTCHA_PROVIDERS;

export interface ICaptchaConfig {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  captchaKey: string;
  captchaSecret: string;
  provider: CAPTCHA_PROVIDERS_KEY;
  captchaGenerator: keyof typeof CAPTCHA_GENERATOR_TYPE;
  isEnable: boolean;
}

export interface IGetCaptchaConfigsPayload {
  projectKey: string;
}
export interface IGetCaptchaConfigsResponse {
  configurations: ICaptchaConfig[];
}

export interface ISaveCaptchaConfigsPayload {
  captchaKey: string;
  captchaSecret: string;
  provider: string;
  captchaGenerator: string;
  isEnable: boolean;
  projectKey: string;
}
export interface ISaveCaptchaConfigsResponse {
  errors: null | unknown;
  isSuccess: boolean;
  itemId: string;
}

export interface IEnableCaptchaConfigsStatusPayload {
  itemId: string;
  isEnable: boolean;
  projectKey: string;
}

export interface IEnableCaptchaConfigsStatusResponse extends ISaveCaptchaConfigsResponse {}
