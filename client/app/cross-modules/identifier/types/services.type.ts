import { RegisteredService } from "../models/service.model";

// Service Registration
export interface IRegisterServicePayload {
  serviceName: string;
  description?: string;
  metadata?: string;
  projectKey: string;
  tags: string[];
}

export interface IRegisterServiceResponse {
  itemId: string;
  isSuccess: boolean;
  errors: unknown;
}

// Get All Services
export interface IGetAllServicesPayload {
  page: number;
  pageSize: number;
  sort?: {
    property: string;
    isDescending: boolean;
  };
  filter?: {
    serviceId: string;
    serviceName: string;
    serviceType: number | string;
  };
  projectKey: string;
}

export interface IGetAllServicesResponse {
  data: RegisteredService[];
  totalCount: number;
  errors?: unknown;
}
