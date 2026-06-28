import { serviceInstances } from "@/lib/http-client";
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
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.ACTIVATE, payload, undefined, { absoluteUrl: true });
  }

  accountResendActivation(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.RESEND_ACTIVATION, payload, undefined, { absoluteUrl: true });
  }

  accountRecover(payload: IAccountRecoverPayload): Promise<IAccountRecoverResponse> {
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.RECOVER, payload, undefined, { absoluteUrl: true });
  }

  accountResetPassword(
    payload: IAccountResetPasswordPayload,
  ): Promise<IAccountResetPasswordResponse> {
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.RESET_PASSWORD, payload, undefined, { absoluteUrl: true });
  }

  checkActivationCodeExpiration(
    payload: IActivationCodeValidationPayload,
  ): Promise<IActivationCodeExpirationResponse> {
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.VALIDATE_ACTIVATION_CODE, payload, undefined, { absoluteUrl: true });
  }
}
