import { http } from "@/lib/http-client";
import {
  IGenerateUserMFA_OtpPayload,
  IGenerateUserMFA_OtpResponse,
  IGetConfigurationPayload,
  IGetConfigurationResponse,
  IConfigureUserMFAPayload,
  IConfigureUserMFAResponse,
  IMFAConfigurationSavePayload,
  IMFAConfigurationSaveResponse,
  ISetupUserTotpPayload,
  ISetupUserTotpResponse,
  IVerifyMfaOtpPayload,
  IVerifyMfaOtpResponse,
  IResendMfaOtpPayload,
  IDisableMFAResponse,
  IDisableMFAPayload,
} from "../models/mfa.model";
import { MFA_CONFIG_ENDPOINTS, MFA_ENDPOINTS } from "../constants/endpoint.constant";

export class MFAService {
  getConfigurations(payload: IGetConfigurationPayload): Promise<IGetConfigurationResponse> {
    return http.get(`${MFA_CONFIG_ENDPOINTS.GET}?ProjectKey=${payload.projectKey}`, undefined, { absoluteUrl: true });
  }

  saveMFAConfiguration(
    payload: IMFAConfigurationSavePayload,
  ): Promise<IMFAConfigurationSaveResponse> {
    return http.post(MFA_CONFIG_ENDPOINTS.SAVE, payload, undefined, { absoluteUrl: true });
  }

  generateUserMfaOTP(payload: IGenerateUserMFA_OtpPayload): Promise<IGenerateUserMFA_OtpResponse> {
    return http.post(MFA_ENDPOINTS.GENERATE_OTP, payload, undefined, { absoluteUrl: true });
  }

  configureUserMFA(payload: IConfigureUserMFAPayload): Promise<IConfigureUserMFAResponse> {
    return http.post(MFA_ENDPOINTS.CONFIGURE_USER_MFA, payload, undefined, { absoluteUrl: true });
  }
  setupUserTotp(payload: ISetupUserTotpPayload): Promise<ISetupUserTotpResponse> {
    return http.get(
      `${MFA_ENDPOINTS.SETUP_TOTP}?UserId=${payload.id}&ProjectKey=${payload.projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  verifyOtp(payload: IVerifyMfaOtpPayload): Promise<IVerifyMfaOtpResponse> {
    return http.post(MFA_ENDPOINTS.VERIFY_OTP, payload, undefined, { absoluteUrl: true });
  }

  resendOtp(payload: IResendMfaOtpPayload): Promise<IVerifyMfaOtpResponse> {
    return http.post(MFA_ENDPOINTS.RESEND_OTP, payload.mfaId, undefined, { absoluteUrl: true });
  }
  disableMFA(payload: IDisableMFAPayload): Promise<IDisableMFAResponse> {
    return http.post(MFA_ENDPOINTS.DISABLE_MFA, payload, undefined, { absoluteUrl: true });
  }
}

export const mfaService = new MFAService();
