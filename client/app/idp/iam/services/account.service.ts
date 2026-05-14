import { http } from "@/lib/http-client";
import {
  IAccountActivationPayload,
  IAccountActivationResponse,
  IAccountRecoverPayload,
  IAccountRecoverResponse,
  IAccountResendActivationPayload,
  IAccountResendActivationResponse,
  IAccountResetPasswordPayload,
  IAccountResetPasswordResponse,
  IActivationCodeExpirationResponse,
  IActivationCodeValidationPayload,
} from "@blocks-idp/iam/models/user";
import { ACCOUNT_ENDPOINTS } from "../constants/endpoint.constant";

export class UserAccountService {
  accountActivation(payload: IAccountActivationPayload): Promise<IAccountActivationResponse> {
    return http.post(ACCOUNT_ENDPOINTS.ACTIVATE, payload);
  }

  accountResendActivation(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    return http.post(ACCOUNT_ENDPOINTS.RESEND_ACTIVATION, payload);
  }

  accountRecover(payload: IAccountRecoverPayload): Promise<IAccountRecoverResponse> {
    return http.post(ACCOUNT_ENDPOINTS.RECOVER, payload);
  }

  accountResetPassword(
    payload: IAccountResetPasswordPayload,
  ): Promise<IAccountResetPasswordResponse> {
    return http.post(ACCOUNT_ENDPOINTS.RESET_PASSWORD, payload);
  }

  checkActivationCodeExpiration(
    payload: IActivationCodeValidationPayload,
  ): Promise<IActivationCodeExpirationResponse> {
    return http.post(ACCOUNT_ENDPOINTS.VALIDATE_ACTIVATION_CODE, payload);
  }
}
