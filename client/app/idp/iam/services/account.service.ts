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
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class UserAccountService {
  accountActivation(payload: IAccountActivationPayload): Promise<IAccountActivationResponse> {
    return logicHttp.post(ACCOUNT_ENDPOINTS.ACTIVATE, payload);
  }

  accountResendActivation(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    return logicHttp.post(ACCOUNT_ENDPOINTS.RESEND_ACTIVATION, payload);
  }

  accountRecover(payload: IAccountRecoverPayload): Promise<IAccountRecoverResponse> {
    return logicHttp.post(ACCOUNT_ENDPOINTS.RECOVER, payload);
  }

  accountResetPassword(
    payload: IAccountResetPasswordPayload,
  ): Promise<IAccountResetPasswordResponse> {
    return logicHttp.post(ACCOUNT_ENDPOINTS.RESET_PASSWORD, payload);
  }

  checkActivationCodeExpiration(
    payload: IActivationCodeValidationPayload,
  ): Promise<IActivationCodeExpirationResponse> {
    return logicHttp.post(ACCOUNT_ENDPOINTS.VALIDATE_ACTIVATION_CODE, payload);
  }
}
