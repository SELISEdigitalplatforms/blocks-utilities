export interface IRole {
  itemId: string;
  name: string;
  description: string;
  count?: number;
  slug: string;
  projectKey?: string;
}

export interface GetRolesPayload {
  filter?: {
    search?: string;
    slugs?: string[];
  };
  sort?: {
    property: string;
    isDescending: boolean;
  };
  page?: number;
  pageSize?: number;
  projectKey: string;
}
export interface GetRolesResponse {
  data: IRole[];
  errors: unknown;
  totalCount: number;
}

export interface IGetRolePayload {
  id: string;
  projectKey: string;
}
export interface IGetRoleResponse {
  data: IRole;
  errors: unknown;
}

export interface CreateRolePayload {
  name: string;
  description: string;
  slug: string;
  projectKey: string;
}
export interface UpdateRolePayload extends Partial<Omit<CreateRolePayload, "slug">> {
  itemId: string;
}

export interface CreateGroup {
  name: string;
  description: string;
  slug: string;
  projectKey: string;
}

export interface EditRole {
  itemId: string;
  name: string;
  description: string;
  projectKey: string;
}

export interface GetRolePermission {
  itemId: string;
  name: string;
  description: string;
  resource: string;
  resourceGroup: string;
  group?: string;
}

export interface SetRoles {
  addPermissions: string[];
  removePermissions: string[];
  slug: string;
  projectKey: string;
}

export interface GroupsData {
  itemId: string;
  name: string;
  description: string;
  count: number;
  projectKey: string;
}

export enum ResourceType {
  "Endpoint" = 1,
  "FE action" = 2,
  "Data protection" = 3,
}

export interface IGetRoles {
  page: number;
  pageSize: number;
  search: string;
  type?: string | null;
  isBuiltIn: string;
  roles: string[];
}
export interface IGetRolesPayload {
  page: number;
  pageSize: number;
  filter: {
    search?: string;
    type?: number;
    isBuiltIn: string;
    tags?: string;
    isArchived?: boolean;
  };
  roles?: string[];
  projectKey: string;
}
export interface IUpdateRole {
  itemId: string;
  name: string;
  description: string;
}
