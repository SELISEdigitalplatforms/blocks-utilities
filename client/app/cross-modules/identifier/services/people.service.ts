import { http } from "@/lib/http-client";
import {
  IPeopleAcceptInvitationPayload,
  IPeopleAcceptInvitationResponse,
  ITransferOwnershipPayload,
  GetPeopleResponse,
} from "@blocks-identifier/models/people.model";
import {
  IConfirmInvitation,
  IInvitePeoplePayload,
  IInvitePeopleResponse,
  IRemoveAccess,
  IRemoveEnvironmentAccess,
  IResendInvitation,
} from "@/models/people";
import { PEOPLE_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";

export class PeopleService {
  peopleAcceptInvitation(
    payload: IPeopleAcceptInvitationPayload,
  ): Promise<IPeopleAcceptInvitationResponse> {
    return http.post(PEOPLE_ENDPOINTS.CONFIRM_INVITATION, payload);
  }

  getPeople(payload: {
    page: number;
    pageSize: number;
    filter: string;
    projectGroupId: string;
  }): Promise<GetPeopleResponse> {
    return http.post<GetPeopleResponse>(PEOPLE_ENDPOINTS.GETS, payload);
  }

  invitePeople(invitePeoplePayload: IInvitePeoplePayload): Promise<IInvitePeopleResponse> {
    return http.post(PEOPLE_ENDPOINTS.INVITE, invitePeoplePayload);
  }

  resendInvitation(resendInvitation: IResendInvitation): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> {
    return http.post(PEOPLE_ENDPOINTS.RESEND_INVITATION, resendInvitation);
  }

  removeAccess(removeAccess: IRemoveAccess): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> {
    return http.post(PEOPLE_ENDPOINTS.REMOVE_ACCESS, removeAccess);
  }

  removeEnvironmentAccess(payload: IRemoveEnvironmentAccess): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> {
    return http.post(PEOPLE_ENDPOINTS.REMOVE_ACCESS, payload);
  }

  confirmInvitation(removeAccess: IConfirmInvitation): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
    activationKey: string;
  }> {
    return http.post(PEOPLE_ENDPOINTS.CONFIRM_INVITATION, removeAccess);
  }

  transferOwnership(payload: ITransferOwnershipPayload): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> {
    return http.post(PEOPLE_ENDPOINTS.TRANSFER_OWNERSHIP, payload);
  }

  // getUserById(id: string, projectKey: string): Promise<{ data: IdentityUserData }> {
  //   return http.get(`/idp/v1/Iam/GetUser?id=${id}&ProjectKey=${projectKey}`);
  // }

  // saveRolesAndPermissions(
  //   paylaod: ISaveRolesAndPermissionsPayload,
  // ): Promise<ISaveRolesAndPermissionsResponse> {
  //   return http.post(`/idp/v1/Iam/SaveRolesAndPermissions`, paylaod);
  // }

  // async getSessions(payload: IGetSessionPayload): Promise<IDeviceSessionResponse> {
  //   const res = await http.get<{ data: string[]; errors: unknown; totalCount: number }>(
  //     `/idp/v1/Iam/GetSessions?page=${payload.page}&pageSize=${payload.pageSize}&projectkey=${payload.projectKey}&filter.userId=${payload.filter.UserId}`,
  //   );
  //   return {
  //     data: res.data.map((item) => JSON.parse(parseMongoDBJson(item))),
  //     totalCount: res.totalCount,
  //     errors: res.errors,
  //   };
  // }

  // async getHistories(payload: IGetHistoriesPayload): Promise<IHistoriesResponse> {
  //   const res = await http.get<{ data: string[]; errors: unknown; totalCount: number }>(
  //     `/idp/v1/Iam/GetHistories?page=${payload.page}&pageSize=${payload.pageSize}&projectkey=${payload.projectKey}&filter.userId=${payload.filter.UserId}`,
  //   );
  //   return {
  //     data: res.data.map((item) => JSON.parse(parseMongoDBJson(item))),
  //     totalCount: res.totalCount,
  //     errors: res.errors,
  //   };
  // }

  // getUserRoles(payload: IGetUserRolesPayload): Promise<IGetUserRolesResponse> {
  //   return http.get(
  //     `/idp/v1/Iam/GetUserRoles?Id=${payload.userId}&ProjectKey=${payload.projectKey}`,
  //   );
  // }
  // getUserPermissions(payload: IGetUserPermissionsPayload): Promise<IGetUserPermissionsResponse> {
  //   return http.get(
  //     `/idp/v1/Iam/GetUserPermissions?Id=${payload.userId}&ProjectKey=${payload.projectKey}`,
  //   );
  // }
}

export const peopleService = new PeopleService();
