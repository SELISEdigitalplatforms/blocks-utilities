import { http } from "@/lib/http-client";
import { parseMongoDBString } from "@/lib/utils";
import {
  IAccountResendActivationPayload,
  IAccountResendActivationResponse,
  ICreateUserPayload,
  ICreateUserResponse,
  IDeviceSessionResponse,
  IGeneratePATPayload,
  IGetHistoriesPayload,
  IGetSessionPayload,
  IGetSignUpSettingPayload,
  IGetSignUpSettingResponse,
  IGetUserByIdPayload,
  IGetUserByIdResponse,
  IGetUserPermissionsPayload,
  IGetUserPermissionsResponse,
  IGetUserRolesPayload,
  IGetUserRolesResponse,
  IGetUsersPayload,
  IGetUsersResponse,
  IHistoriesResponse,
  IPATResponse,
  ISaveRolesAndPermissionsPayload,
  ISaveRolesAndPermissionsResponse,
  ISaveSignUpSettingPayload,
  ISaveSignUpSettingResponse,
  IUpdateUserPayload,
  IUpdateUserResponse,
  User,
} from "@blocks-idp/iam/models/user";
import { USER_ENDPOINTS } from "../constants/endpoint.constant";
import { UserAccountService } from "./account.service";

export class UserService {
  constructor(public account: UserAccountService) {}

  getUsers(payload: IGetUsersPayload): Promise<IGetUsersResponse> {
    return http.post(USER_ENDPOINTS.GET_USERS, payload, undefined, {
      absoluteUrl: true,
    });
  }

  getUser(): Promise<{ data: User }> {
    return http.get(`${USER_ENDPOINTS.GET_USER}`, undefined, {
      absoluteUrl: true,
    });
  }

  getUserInfo(): Promise<User> {
    return http.get(`${USER_ENDPOINTS.USER_INFO}`, undefined, {
      absoluteUrl: true,
    });
  }

  getUserById(payload: IGetUserByIdPayload): Promise<IGetUserByIdResponse> {
    return http.get(
      `${USER_ENDPOINTS.GET_USERS}?id=${payload.id}&ProjectKey=${payload.projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  addUser(createPayload: ICreateUserPayload): Promise<ICreateUserResponse> {
    return http.post(USER_ENDPOINTS.CREATE, createPayload, undefined, {
      absoluteUrl: true,
    });
  }

  updateUser(payload: IUpdateUserPayload): Promise<IUpdateUserResponse> {
    return http.post(
      `${USER_ENDPOINTS.UPDATE}/${payload.itemId}`,
      payload,
      undefined,
      { absoluteUrl: true },
    );
  }

  getSignUpSetting(
    payload: IGetSignUpSettingPayload,
  ): Promise<IGetSignUpSettingResponse> {
    return http.get(
      `${USER_ENDPOINTS.GET_SIGNUP_SETTING}?ProjectKey=${payload.projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  saveSignUpSetting(
    payload: ISaveSignUpSettingPayload,
  ): Promise<ISaveSignUpSettingResponse> {
    return http.post(USER_ENDPOINTS.SAVE_SIGNUP_SETTING, payload, undefined, {
      absoluteUrl: true,
    });
  }

  saveRolesAndPermissions(
    payload: ISaveRolesAndPermissionsPayload,
  ): Promise<ISaveRolesAndPermissionsResponse> {
    return http.post(
      USER_ENDPOINTS.SAVE_ROLES_AND_PERMISSIONS,
      payload,
      undefined,
      { absoluteUrl: true },
    );
  }

  async getSessions(
    payload: IGetSessionPayload,
  ): Promise<IDeviceSessionResponse> {
    const res = await http.get<{
      data: string[];
      errors: unknown;
      totalCount: number;
    }>(
      `${USER_ENDPOINTS.GET_SESSIONS}?page=${payload.page}&pageSize=${payload.pageSize}&projectkey=${payload.projectKey}&filter.userId=${payload.filter.UserId}`,
      undefined,
      { absoluteUrl: true },
    );
    return {
      data: res.data.map((item) => JSON.parse(parseMongoDBString(item))),
      totalCount: res.totalCount,
      errors: res.errors,
    };
  }

  async getHistories(
    payload: IGetHistoriesPayload,
  ): Promise<IHistoriesResponse> {
    const res = await http.get<{
      data: string[];
      errors: unknown;
      totalCount: number;
    }>(
      `${USER_ENDPOINTS.GET_HISTORIES}?page=${payload.page}&pageSize=${payload.pageSize}&projectkey=${payload.projectKey}&filter.userId=${payload.filter.UserId}`,
      undefined,
      { absoluteUrl: true },
    );
    return {
      data: res.data.map((item) => JSON.parse(parseMongoDBString(item))),
      totalCount: res.totalCount,
      errors: res.errors,
    };
  }

  async getPats(): Promise<IPATResponse> {
    return http.get(USER_ENDPOINTS.GET_USER_CODES, undefined, {
      absoluteUrl: true,
    });
  }

  async generatePats(payload: IGeneratePATPayload): Promise<IPATResponse> {
    return http.post(USER_ENDPOINTS.GENERATE_USER_CODE, payload, undefined, {
      absoluteUrl: true,
    });
  }

  getUserRoles(payload: IGetUserRolesPayload): Promise<IGetUserRolesResponse> {
    return http.get(
      `${USER_ENDPOINTS.GET_USER_ROLES}?Id=${payload.userId}&ProjectKey=${payload.projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  getUserPermissions(
    payload: IGetUserPermissionsPayload,
  ): Promise<IGetUserPermissionsResponse> {
    return http.get(
      `${USER_ENDPOINTS.GET_USER_PERMISSIONS}?Id=${payload.userId}&ProjectKey=${payload.projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  accountDeactivate(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    return http.post(USER_ENDPOINTS.DEACTIVATE, payload, undefined, {
      absoluteUrl: true,
    });
  }

  me(): Promise<{ data: User }> {
    return http.get(`${USER_ENDPOINTS.ME}`, undefined, { absoluteUrl: true });
  }
}

export const userService = new UserService(new UserAccountService());
