import { HttpClient } from "@/lib/http-client";
import { IAPIResponse } from "@/models/api-response";
import {
  CreatePermissionPayload,
  CreatePermissionResponse,
  IGetPermissionByIdPayload,
  IGetPermissionByIdResponse,
  IGetPermissionsPayload,
  IGetPermissionsSeverityRequestPayload,
  IGetPermissionsSeverityResponse,
  IGetResourceGroupPayload,
  IGetResourceGroupResponse,
  IPermission,
  UpdatePermissionPayload,
  UpdatePermissionResponse,
} from "@blocks-idp/iam/models/permission";
import { PERMISSION_ENDPOINTS } from "../constants/endpoint.constant";
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class PermissionService {
  getPermissions(
    payload: IGetPermissionsPayload,
  ): Promise<IAPIResponse<IPermission[]> & { totalCount: number }> {
    return logicHttp.post(PERMISSION_ENDPOINTS.GET_PERMISSIONS, payload);
  }

  getPermissionsSeverity(
    payload: IGetPermissionsSeverityRequestPayload,
  ): Promise<IGetPermissionsSeverityResponse> {
    const url = `${PERMISSION_ENDPOINTS.GET_PERMISSIONS_GROUP_BY_SEVERITY}?ProjectKey=${payload.projectKey}`;
    return logicHttp.get(url);
  }

  getPermissionById(payload: IGetPermissionByIdPayload): Promise<IGetPermissionByIdResponse> {
    return logicHttp.get(
      `${PERMISSION_ENDPOINTS.GET_PERMISSION}?Id=${payload.id}&ProjectKey=${payload.projectKey}`,
    );
  }

  addPermission = (
    addPermissionPayload: CreatePermissionPayload,
  ): Promise<CreatePermissionResponse> => {
    return logicHttp.post(PERMISSION_ENDPOINTS.CREATE_PERMISSION, addPermissionPayload);
  };

  updatePermission = (payload: UpdatePermissionPayload): Promise<UpdatePermissionResponse> => {
    return logicHttp.post(PERMISSION_ENDPOINTS.UPDATE_PERMISSION, payload);
  };

  getResourceGroup(payload: IGetResourceGroupPayload): Promise<IGetResourceGroupResponse> {
    return logicHttp.get(`${PERMISSION_ENDPOINTS.GET_RESOURCE_GROUPS}?ProjectKey=${payload.projectKey}`);
  }
}

export const permissionService = new PermissionService();
