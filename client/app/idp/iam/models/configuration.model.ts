export interface IIAMConfiguration {
  accountActivationUrl: string;
  accountVerificationUrl: string;
  recoverAccountUrl: string;
  activationUrlLifetimeInMinutes: number;
  recoverAccountUrlLifetimeInMinutes: number;
  logoutOnPasswordChange: boolean;
}

export interface IIAMConfigurationSavePayload extends IIAMConfiguration {
  projectKey: string;
}
export interface IIAMConfigurationGetResponse {
  data: IIAMConfiguration;
  errors: unknown;
}
