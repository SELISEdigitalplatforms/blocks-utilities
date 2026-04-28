export interface IInvitePeoplePayload {
  invitations: Record<string, string[]>;
  groupId: string;
}

export interface IInvitePeopleResponse {
  isSuccess: boolean;
  errors: null | { exceed_limit: string };
}

export interface IResendInvitation {
  email: string;
  groupId: string;
}

export interface IRemoveAccess {
  userIds: string[];
  projectKey: string;
}

export interface IRemoveEnvironmentAccess {
  email: string;
  projectKeys: string[];
  groupId: string;
}

export interface IConfirmInvitation {
  code: string;
}
export interface SharedEnvironment {
  itemId: string;
  tenantId: string;
  isInvitationSent: boolean;
  isInvitationConfirmed: boolean;
  isCreator: boolean;
  enviroment: string; // Note: API has typo "enviroment" instead of "environment"
}

export interface PeopleDetails {
  salutation: string;
  firstName: string;
  lastName: string;
  email: string;
  profileImageUrl: string | null;
  userId: string;
  allowResendActivation: boolean;
}

export interface PeopleGroupedByEnvironments {
  peopleDetails: PeopleDetails;
  sharedEnviroments: SharedEnvironment[];
}

// Legacy interface for backward compatibility
export interface People {
  itemId: string;
  salutation: string;
  firstName: string;
  lastName: string;
  email: string;
  profileImageUrl: string;
  userId: string;
  tenantId: string;
  role: string;
  isInvitationSent: boolean;
  isInvitationConfirmed: boolean;
  isCreator: boolean;
}
