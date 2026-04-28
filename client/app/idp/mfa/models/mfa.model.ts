export interface IMFAConfiguration {
  enableMfa: boolean;
  mfaTemplate: { templateName: string; templateId: string };
  projectKey: string | null;
  userMfaType: number[];
}
export interface IGetConfigurationPayload {
  projectKey: string;
}

export interface IMFAConfigurationSavePayload {
  enableMfa: boolean;
  userMfaType: number[];
  mfaTemplate?: {
    templateName: string;
    templateId: string;
  };
  projectKey: string;
}
export interface IMFAConfigurationSaveResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IGetConfigurationResponse extends IMFAConfiguration {}

export interface IConfigureUserMFAPayload {
  userId: string;
  mfaEnabled: boolean;
  userMfaType: number;
  projectKey: string;
}
export interface IConfigureUserMFAResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface ISetupUserTotpPayload {
  projectKey: string;
  id: string;
}
export interface ISetupUserTotpResponse {
  errors: unknown | null;
  isSuccess: boolean;
  qrImageUrl: string;
  qrCode: string;
}
export interface IGenerateUserMFA_OtpPayload {
  userId: string;
  projectKey: string;
  mfaType: number;
  sendPhoneNumberAsEmailDomain?: string;
}
export interface IGenerateUserMFA_OtpResponse {
  errors: unknown | null;
  isSuccess: boolean;
  mfaId: string;
}
export interface IVerifyMfaOtpPayload {
  mfaId: string;
  verificationCode: string;
  authType: number;
  projectKey: string;
  isFromTokenCall?: boolean;
}
export interface IVerifyMfaOtpResponse {
  errors: unknown;
  isSuccess: boolean;
  isValid: boolean;
  userId: string;
}
export interface IResendMfaOtpPayload {
  mfaId: string;
  sendPhoneNumberAsEmailDomain?: string;
}
export interface IVerifyMfaOtpResponse {
  errors: unknown;
  isSuccess: boolean;
  isValid: boolean;
  userId: string;
}
export interface IDisableMFAPayload {
  userId: string;
  projectKey: string;
}
export interface IDisableMFAResponse {
  errors: unknown;
  isSuccess: boolean;
}
