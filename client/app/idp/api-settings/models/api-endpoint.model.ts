export interface IApiEndpoint {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string | null;
  lastUpdatedBy: string;
  language: string | null;
  organizationIds: string[];
  tags: string[];
  service: string;
  method: string;
  description: string;
  isCaptchaRequired: boolean;
  captchaProvider: string;
  isMFARequired: boolean;
  mfaType: string;
  controller: string;
  baseUrl: string;
  version: string;
}

export interface IApiEndpointFilter {
  service?: string;
  method?: string;
  controller?: string;
}

export interface IGetApiEndpointsPayload {
  projectKey: string;
  page?: number;
  pageSize?: number;
  filter?: IApiEndpointFilter;
}

export interface IGetApiEndpointsResponse {
  page: number;
  pageSize: number;
  totalPages: number;
  totalCount: number;
  data: IApiEndpoint[];
  errors: string[] | null;
}

export interface IUpdateApiEndpointPayload {
  projectKey: string;
  itemId: string;
  service: string;
  method: string;
  controller: string;
  description: string;
  isCaptchaRequired: boolean;
  captchaProvider: string;
  isMFARequired: boolean;
  mfaType: string;
}

export interface IUpdateApiEndpointResponse {
  isSuccess: boolean;
  errors?: string[];
}

export interface IBulkUpdateApiEndpointsPayload {
  projectKey: string;
  itemIds: string[];
  isCaptchaRequired?: boolean;
  isMFARequired?: boolean;
  disableAll?: boolean;
}

export interface IBulkUpdateApiEndpointsResponse {
  isSuccess: boolean;
  errors?: string[];
}

export interface IRemoveApiEndpointsPayload {
  projectKey: string;
  itemIds: string[];
}

export interface IRemoveApiEndpointsResponse {
  isSuccess: boolean;
  errors?: string[];
}

export interface IServiceGroup {
  service: string;
  description: string;
  icon: string;
  endpoints: IApiEndpoint[];
}
