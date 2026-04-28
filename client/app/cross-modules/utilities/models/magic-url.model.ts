export interface MagicUrl {
  itemId: string;
  type?: string;
  name?: string;
  uri: string;
  requestMethod?: string;
  usageLimit: number;
  usageCount: number;
  expiryDate?: string;
  shortUri: string;
  clientCredential?: string;
  status?: string;
  createdAt: string;
  createdBy?: string;
  linkBasedActionConfigId?: string;
  requestByUserId?: string;
  expiredReason?: string | null;

  // Optional/Legacy fields
  shortCode?: string;
  persistent?: boolean;
  updatedAt?: string;
  expiryLifeSpan?: number;
  isExpired?: boolean;
}

export interface IGetMagicUrlsPayload {
  page: number;
  pageSize: number;
  projectKey: string;
  searchText?: string;
  status?: string;
  expiryDateRangeStartDate?: string;
  expiryDateRangeEndDate?: string;
  requestMethod?: string;
  type?: string;
}

export interface IGetMagicUrlsResponse {
  totalCount: number;
  data: MagicUrl[];
  errors: unknown;
}

export interface IGetMagicUrlByIdPayload {
  ItemId: string;
  projectKey: string;
}

export interface IGetMagicUrlByIdResponse {
  data: MagicUrl;
  errors: unknown;
}

export interface ICreateMagicUrlPayload {
  type: number;
  name?: string;
  uri: string;
  requestMethod?: string;
  requestPayload?: string;
  requestHeaders?: string;
  redirectUrl?: string;
  usageLimit?: number;
  expiryLifeSpan?: number;
  requestByUserId?: string;
  userCanLogin?: boolean;
  clientCredential?: string;
  linkBasedActionConfigId?: string;
  persistent?: boolean;
  projectKey: string;
  requestEncodedQueryString?: string;
}
