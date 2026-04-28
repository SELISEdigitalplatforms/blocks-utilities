import { PeopleGroupedByEnvironments } from "@/models/people";

export interface IPeopleAcceptInvitationPayload {
  code: string;
}

export interface IPeopleAcceptInvitationResponse {
  isSuccess: boolean;
  activationKey?: string;
  errors?: Record<string, string>;
}

export interface ITransferOwnershipPayload {
  tenantGroupId: string;
  transferToUserEmail: string;
}

export interface GetPeopleResponse {
  peoples: PeopleGroupedByEnvironments[];
  totalCount: number;
  errors: null | unknown;
  isSuccess: boolean;
  isOwner: boolean;
}
