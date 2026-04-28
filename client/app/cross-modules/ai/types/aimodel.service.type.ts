export interface ICreateModelPayload {
  provider: string;
  service_platform: string;
  version: string;
  model_name: string;
  display_name: string;
  api_key: string;
  base_url: string;
  openai_organization_id: string;
  openai_project_id: string;
  api_version: string;
  deployment_name: string;
  project_key: string;
  DefaultTemp?: number;
  MaxTokens: number;
  custom_parameters: Record<string, unknown> | null;
  custom_headers: Record<string, unknown> | null;
}

export interface IModelResponse {
  is_success: false;
  item_id: string;
  detail: string;
  error: {};
}

export interface IValid {
  valid: boolean;
  message: string;
  model_id: string;
  provider: string;
  model_name: string;
}

export interface IValidateModelResponse {
  valid: IValid;
  message: string;
}

export interface IModelListPayload {
  provider?: string | null;
  model_type?: string | null;
  is_active?: boolean | null;
  search: string | null;
  status?: string | null;
  page: number;
  page_size: number;
}

export enum ModelStatus {
  VALID = "valid",
  INVALID = "invalid",
}

export interface IModelListResponse {
  models: IModelInfo[];
  project_key: string;
  total: number;
  page: number;
  page_size: number;
  has_next: boolean;
}

export interface IModelInfo {
  _id: string;

  CreatedDate?: string;
  LastUpdatedDate?: string;
  CreatedBy?: string;
  Language?: string;
  LastUpdatedBy?: string;
  OrganizationIds?: string[];
  Tags?: string[];
  ProjectKey: string;

  Provider?: string;
  ModelType?: string;
  ServicePlatform?: string;
  Description?: string;
  Capabilities?: Record<string, unknown>;

  DisplayName: string;
  ModelName?: string;

  ApiKey: string;
  BaseUrl: string;

  OpenAiOrganizationId?: string;
  OpenAiProjectId?: string;
  ApiVersion?: string;
  DeploymentName?: string;

  CustomParameters?: Record<string, string>;
  CustomHeaders?: Record<string, string>;

  Status: string;
  IsActive: boolean;
  has_streaming?: boolean;
}

export interface IUpdateModelPayload {
  display_name: string;
  version?: string;
  is_active?: boolean;
  api_key?: string;
  base_url: string;
  openai_organization_id?: string;
  openai_project_id?: string;
  api_version?: string;
  deployment_name?: string;
  project_key: string;
  DefaultTemp?: number;
  MaxTokens?: number;
  custom_parameters?: Record<string, unknown> | null;
  custom_headers?: Record<string, unknown> | null;
}

export interface ISeedProvidersPayload {}

export interface IProvider {
  Provider: string;
  Url: string;
  DocLink: string;
  Description: string;
  Order: number | string;
  IsActive?: boolean;
}

export interface ISeedModelInfo {
  Model: string;
  ModelGoodName: string;
  MaxTokens: number;
  DefaultTemp: number;
  ContextLength: number;
  InputCostPerMillion: number;
  OutputCostPerMillion: number;
  IsActive: boolean;
  DefaultBaseUrl?: string;
}
