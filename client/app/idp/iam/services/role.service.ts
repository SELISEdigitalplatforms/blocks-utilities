import { serviceInstances } from "@/lib/http-client";
import {
  CreateRolePayload,
  GetRolesPayload,
  GetRolesResponse,
  IGetRolePayload,
  IGetRoleResponse,
  IRole,
  SetRoles,
  UpdateRolePayload,
} from "@blocks-idp/iam/models/role";
import { ROLE_ENDPOINTS } from "../constants/endpoint.constant";

export class RoleService {
  getRoles(payload: GetRolesPayload): Promise<GetRolesResponse> {
    return serviceInstances.idpService.post(ROLE_ENDPOINTS.GET_ROLES, payload, undefined, { absoluteUrl: true });
  }

  getRoleById(payload: IGetRolePayload): Promise<IGetRoleResponse> {
    return serviceInstances.idpService.get(`${ROLE_ENDPOINTS.GET_ROLE}?projectKey=${payload.projectKey}&id=${payload.id}`, undefined, { absoluteUrl: true });
  }

  addRole(payload: CreateRolePayload): Promise<IRole> {
    return serviceInstances.idpService.post(ROLE_ENDPOINTS.CREATE_ROLE, payload, undefined, { absoluteUrl: true });
  }

  updateRole(payload: UpdateRolePayload) {
    return serviceInstances.idpService.post<{
      errors: unknown;
      isSuccess: boolean;
      itemId: string;
    }>(ROLE_ENDPOINTS.UPDATE_ROLE, payload, undefined, { absoluteUrl: true });
  }

  setRoles(addSetRolesPayload: SetRoles): Promise<SetRoles> {
    return serviceInstances.idpService.post<SetRoles>(ROLE_ENDPOINTS.SET_ROLES, { ...addSetRolesPayload }, undefined, { absoluteUrl: true });
  }
}

export const roleService = new RoleService();
