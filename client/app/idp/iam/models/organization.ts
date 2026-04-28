export interface IOrganization {
  itemId: string;
  name: string;
  isEnable: boolean;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  lastUpdatedBy: string;
  language: string | null;
  organizationIds: string[];
  tags: string[];
}

export interface IOrganizationFilter {
  projectKey: string;
  page: number;
  pageSize: number;
  search?: string;
  sort?: {
    property: string;
    isDescending: boolean;
  };
}

export interface IGetOrganizationsParams {
  projectKey: string;
  page: number;
  pageSize: number;
  searchText?: string;
}

export interface IGetOrganizationsResponse {
  organizations: IOrganization[];
  errors: unknown;
  isSuccess: boolean;
  totalCount: number;
}

export interface IGetOrganizationByIdParams {
  projectKey: string;
  itemId: string;
}

export interface IGetOrganizationByIdResponse {
  organization: IOrganization;
  errors: unknown;
  isSuccess: boolean;
}

export interface ICreateOrUpdateOrganizationPayload {
  projectKey: string;
  name: string;
  itemId: string;
  isEnable: boolean;
}

export interface ICreateOrUpdateOrganizationResponse {
  errors: unknown;
  isSuccess: boolean;
}
