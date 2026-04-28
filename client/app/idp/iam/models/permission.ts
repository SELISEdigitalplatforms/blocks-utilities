export enum PermissionSeverityLevel {
  Critical = 1,
  High,
  Medium,
  Low,
}

type PermissionSeverityOption = {
  label: string;
  value: PermissionSeverityLevel;
  variant: "error" | "destructive" | "info" | "success";
  className?: string;
  barClassName?: string;
  id: string;
  bg: string;
};

export const PERMISSION_SEVERITY_OPTIONS: PermissionSeverityOption[] = [
  {
    id: "Critical",
    label: "Critical",
    value: PermissionSeverityLevel.Critical,
    variant: "error",
    className: "text-red-800",
    barClassName: "bg-red-800",
    bg: "bg-red-50",
  },
  {
    id: "High",
    label: "High",
    value: PermissionSeverityLevel.High,
    variant: "destructive",
    className: "text-rose-800",
    barClassName: "bg-rose-500",
    bg: "bg-rose-50",
  },
  {
    id: "Medium",
    label: "Medium",
    value: PermissionSeverityLevel.Medium,
    variant: "info",
    className: "text-yellow-500",
    barClassName: "bg-yellow-400",
    bg: "bg-yellow-50",
  },
  {
    id: "Low",
    label: "Low",
    value: PermissionSeverityLevel.Low,
    variant: "success",
    className: "text-blue-500",
    barClassName: "bg-blue-400",
    bg: "bg-blue-50",
  },
];

export interface IPermission {
  itemId: string;
  name: string;
  type: number;
  description: string;
  resource: string;
  resourceGroup: string;
  projectKey: string;
  tags: string[];
  roles: string[];
  dependentPermissions: string[];
  isArchived: boolean;
  isBuiltIn: boolean;
  language: string | null;
  organizationIds: string[];
  permissionSeverity: PermissionSeverityLevel;
}

export interface IPermissionFilter {
  projectKey: string;
  source?: string[];
  type?: number | null;
  page: number;
  pageSize: number;
  search: string;
  isBuiltIn: string;
  roles: string[];
  resourceGroup?: string;
  sort?: {
    property: string;
    isDescending: boolean;
  };
  permissionSeverity?: string;
}

export interface IGetPermissionsPayload {
  page: number;
  pageSize: number;
  sort?: {
    property: string;
    isDescending: boolean;
  };
  filter: {
    search: string;
    isBuiltIn: string;
    type?: number;
    tags?: string[];
    isArchived?: boolean;
    resourceGroup?: string;
    resources?: string[];
    permissionSeverity?: number;
  };
  roles: string[];
  projectKey: string;
}
export interface IGetPermissionByIdPayload {
  id: string;
  projectKey: string;
}
export interface IGetPermissionByIdResponse {
  data: IPermission;
  errors: unknown;
}

export interface CreatePermissionPayload {
  name: string;
  type: number;
  description: string;
  resource: string;
  resourceGroup: string;
  tags: string[];
  dependentPermissions: string[];
  isBuiltIn: boolean;
  projectKey: string;
}
export interface CreatePermissionResponse {
  errors: unknown;
  isSuccess: boolean;
  itemId: string;
}
export interface UpdatePermissionPayload extends Partial<CreatePermissionPayload> {
  itemId: string;
  isArchived?: boolean;
}
export interface UpdatePermissionResponse {
  errors: unknown;
  isSuccess: boolean;
  itemId: string;
}

export interface GetRolePermission {
  itemId: string;
  name: string;
  description: string;
  resource: string;
  resourceGroup: string;
  group?: string;
}

export interface GetPermission {
  itemId: string;
  name: string;
  description: string;
  resource: string;
  resourceGroup: string;
}

export enum ResourceType {
  "Endpoint" = 1,
  "FE action" = 2,
  "Data protection" = 3,
}

export const RESOURCE_TYPE = [
  {
    value: "1",
    label: "Endpoint",
  },
  {
    value: "2",
    label: "FE action",
  },
  {
    value: "3",
    label: "Data protection",
  },
];

export interface IGetResourceGroupPayload {
  projectKey: string;
}

export type IGetResourceGroupResponse = {
  resourceGroup: string;
  count: number;
}[];

export interface IGetPermissionsSeverityRequestPayload {
  projectKey: string;
}

export type IGetPermissionsSeverityResponse = {
  severityLevel: string;
  count: number;
}[];
