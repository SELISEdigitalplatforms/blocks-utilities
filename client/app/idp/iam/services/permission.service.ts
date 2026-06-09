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
import { deriveIdpBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const iamHttp = new HttpClient(
  deriveIdpBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class PermissionService {
  getPermissions(
    payload: IGetPermissionsPayload,
  ): Promise<IAPIResponse<IPermission[]> & { totalCount: number }> {
    return iamHttp.post(PERMISSION_ENDPOINTS.GET_PERMISSIONS, payload, undefined, { absoluteUrl: true });
  }

  getPermissionsSeverity(
    payload: IGetPermissionsSeverityRequestPayload,
  ): Promise<IGetPermissionsSeverityResponse> {
    const url = `${PERMISSION_ENDPOINTS.GET_PERMISSIONS_GROUP_BY_SEVERITY}?ProjectKey=${payload.projectKey}`;
    return iamHttp.get(url, undefined, { absoluteUrl: true });
  }

  getPermissionById(payload: IGetPermissionByIdPayload): Promise<IGetPermissionByIdResponse> {
    return iamHttp.get(
      `${PERMISSION_ENDPOINTS.GET_PERMISSION}?Id=${payload.id}&ProjectKey=${payload.projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  addPermission = (
    addPermissionPayload: CreatePermissionPayload,
  ): Promise<CreatePermissionResponse> => {
    return iamHttp.post(PERMISSION_ENDPOINTS.CREATE_PERMISSION, addPermissionPayload, undefined, { absoluteUrl: true });
  };

  updatePermission = (payload: UpdatePermissionPayload): Promise<UpdatePermissionResponse> => {
    return iamHttp.post(PERMISSION_ENDPOINTS.UPDATE_PERMISSION, payload, undefined, { absoluteUrl: true });
  };

  getResourceGroup(payload: IGetResourceGroupPayload): Promise<IGetResourceGroupResponse> {
    return iamHttp.get(`${PERMISSION_ENDPOINTS.GET_RESOURCE_GROUPS}?ProjectKey=${payload.projectKey}`, undefined, { absoluteUrl: true });
  }
}

export const permissionService = new PermissionService();
