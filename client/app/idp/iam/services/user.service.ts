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
  IUpdateUserPayload,
  IUpdateUserResponse,
  IGetSignUpSettingPayload,
  IGetSignUpSettingResponse,
  ISaveSignUpSettingPayload,
  ISaveSignUpSettingResponse,
  User,
} from "@blocks-idp/iam/models/user";
import { UserAccountService } from "./account.service";
import { USER_ENDPOINTS } from "../constants/endpoint.constant";

export class UserService {
  constructor(public account: UserAccountService) {}

  getUsers(payload: IGetUsersPayload): Promise<IGetUsersResponse> {
    return http.post(USER_ENDPOINTS.GET_USERS, payload);
  }

  getUser(): Promise<{ data: User }> {
    return http.get(USER_ENDPOINTS.GET_USER);
  }

  getUserById(payload: IGetUserByIdPayload): Promise<IGetUserByIdResponse> {
    return http.get(`${USER_ENDPOINTS.GET_USER}?id=${payload.id}&ProjectKey=${payload.projectKey}`);
  }

  addUser(createPayload: ICreateUserPayload): Promise<ICreateUserResponse> {
    return http.post(USER_ENDPOINTS.CREATE, createPayload);
  }

  updateUser(payload: IUpdateUserPayload): Promise<IUpdateUserResponse> {
    return http.post(USER_ENDPOINTS.UPDATE, payload);
  }

  getSignUpSetting(payload: IGetSignUpSettingPayload): Promise<IGetSignUpSettingResponse> {
    return http.get(`${USER_ENDPOINTS.GET_SIGNUP_SETTING}?ProjectKey=${payload.projectKey}`);
  }

  saveSignUpSetting(payload: ISaveSignUpSettingPayload): Promise<ISaveSignUpSettingResponse> {
    return http.post(USER_ENDPOINTS.SAVE_SIGNUP_SETTING, payload);
  }

  saveRolesAndPermissions(
    payload: ISaveRolesAndPermissionsPayload,
  ): Promise<ISaveRolesAndPermissionsResponse> {
    return http.post(USER_ENDPOINTS.SAVE_ROLES_AND_PERMISSIONS, payload);
  }

  async getSessions(payload: IGetSessionPayload): Promise<IDeviceSessionResponse> {
    const res = await http.get<{ data: string[]; errors: unknown; totalCount: number }>(
      `${USER_ENDPOINTS.GET_SESSIONS}?page=${payload.page}&pageSize=${payload.pageSize}&projectkey=${payload.projectKey}&filter.userId=${payload.filter.UserId}`,
    );
    return {
      data: res.data.map((item) => JSON.parse(parseMongoDBString(item))),
      totalCount: res.totalCount,
      errors: res.errors,
    };
  }

  async getHistories(payload: IGetHistoriesPayload): Promise<IHistoriesResponse> {
    const res = await http.get<{ data: string[]; errors: unknown; totalCount: number }>(
      `${USER_ENDPOINTS.GET_HISTORIES}?page=${payload.page}&pageSize=${payload.pageSize}&projectkey=${payload.projectKey}&filter.userId=${payload.filter.UserId}`,
    );
    return {
      data: res.data.map((item) => JSON.parse(parseMongoDBString(item))),
      totalCount: res.totalCount,
      errors: res.errors,
    };
  }

  async getPats(): Promise<IPATResponse> {
    return http.get(USER_ENDPOINTS.GET_USER_CODES);
  }

  async generatePats(payload: IGeneratePATPayload): Promise<IPATResponse> {
    return http.post(USER_ENDPOINTS.GENERATE_USER_CODE, payload);
  }

  getUserRoles(payload: IGetUserRolesPayload): Promise<IGetUserRolesResponse> {
    return http.get(
      `${USER_ENDPOINTS.GET_USER_ROLES}?Id=${payload.userId}&ProjectKey=${payload.projectKey}`,
    );
  }

  getUserPermissions(payload: IGetUserPermissionsPayload): Promise<IGetUserPermissionsResponse> {
    return http.get(
      `${USER_ENDPOINTS.GET_USER_PERMISSIONS}?Id=${payload.userId}&ProjectKey=${payload.projectKey}`,
    );
  }

  accountDeactivate(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    return http.post(USER_ENDPOINTS.DEACTIVATE, payload);
  }
}

export const userService = new UserService(new UserAccountService());
