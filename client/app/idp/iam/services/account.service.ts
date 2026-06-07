import { HttpClient } from "@/lib/http-client";
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
import { deriveIdpBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const iamHttp = new HttpClient(
  deriveIdpBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class UserAccountService {
  accountActivation(payload: IAccountActivationPayload): Promise<IAccountActivationResponse> {
    return iamHttp.post(ACCOUNT_ENDPOINTS.ACTIVATE, payload, undefined, { absoluteUrl: true });
  }

  accountResendActivation(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    return iamHttp.post(ACCOUNT_ENDPOINTS.RESEND_ACTIVATION, payload, undefined, { absoluteUrl: true });
  }

  accountRecover(payload: IAccountRecoverPayload): Promise<IAccountRecoverResponse> {
    return iamHttp.post(ACCOUNT_ENDPOINTS.RECOVER, payload, undefined, { absoluteUrl: true });
  }

  accountResetPassword(
    payload: IAccountResetPasswordPayload,
  ): Promise<IAccountResetPasswordResponse> {
    return iamHttp.post(ACCOUNT_ENDPOINTS.RESET_PASSWORD, payload, undefined, { absoluteUrl: true });
  }

  checkActivationCodeExpiration(
    payload: IActivationCodeValidationPayload,
  ): Promise<IActivationCodeExpirationResponse> {
    return iamHttp.post(ACCOUNT_ENDPOINTS.VALIDATE_ACTIVATION_CODE, payload, undefined, { absoluteUrl: true });
  }
}
