export interface ISaveMagicUrlConfigPayload {
  contextName: string;
  shortUrlBase: string;
  projectKey: string;
}

export interface IMagicUrlConfig {
  itemId: string;
  contextName: string;
  shortUrlBase: string;
  projectKey: string;
  createdAt: string;
  updatedAt: string;
}

export interface ISaveMagicUrlConfigResponse {
  errors?: Record<string, string>;
  isSuccess: boolean;
  configId: string;
  wasCreated: boolean;
  config: IMagicUrlConfig;
  errorMessage?: string;
}
