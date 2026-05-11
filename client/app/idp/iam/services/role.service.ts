import { HttpClient } from "@/lib/http-client";
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
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class RoleService {
  getRoles(payload: GetRolesPayload): Promise<GetRolesResponse> {
    return logicHttp.post(ROLE_ENDPOINTS.GET_ROLES, payload);
  }

  getRoleById(payload: IGetRolePayload): Promise<IGetRoleResponse> {
    return logicHttp.get(`${ROLE_ENDPOINTS.GET_ROLE}?projectKey=${payload.projectKey}&id=${payload.id}`);
  }

  addRole(payload: CreateRolePayload): Promise<IRole> {
    return logicHttp.post(ROLE_ENDPOINTS.CREATE_ROLE, payload);
  }

  updateRole(payload: UpdateRolePayload) {
    return logicHttp.post<{
      errors: unknown;
      isSuccess: boolean;
      itemId: string;
    }>(ROLE_ENDPOINTS.UPDATE_ROLE, payload);
  }

  setRoles(addSetRolesPayload: SetRoles): Promise<SetRoles> {
    return logicHttp.post<SetRoles>(ROLE_ENDPOINTS.SET_ROLES, { ...addSetRolesPayload });
  }
}

export const roleService = new RoleService();
