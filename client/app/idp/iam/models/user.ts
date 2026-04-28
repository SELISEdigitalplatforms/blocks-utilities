import { IPermission } from "./permission";
import { IRole } from "./role";

export interface User {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  language: string;
  salutation: string;
  firstName: string;
  lastName: string;
  email: string;
  userName: string;
  phoneNumber: string;
  roles: string[];
  permissions: string[];
  active: boolean;
  isVarified: boolean;
  profileImageUrl: string;
  mfaEnabled: boolean;
  lastLoggedInTime: string;
  logInCount: number;
  firstLoggedInTime: string;
  userMfaType: number;
  isMfaVerified: boolean;
  userCreationType: number;
  memberships: IMembership[]
}

export interface IMembership {
  organizationId: string,
  roles: string[],
  permissions: string[]
}
export interface IGetUsersPayload {
  page: number;
  pageSize: number;
  sort?: {
    property: string;
    isDescending: boolean;
  };
  filter?: {
    email: string;
    name: string;
    organizationId?: string;
  };
  projectKey: string;
}
export interface IGetUsersResponse {
  errors: unknown;
  data: User[];
  totalCount: number;
}

export interface IGetUserByIdPayload {
  id: string;
  projectKey: string;
}
export interface IGetUserByIdResponse {
  data: User;
  errors: unknown;
  roles: IRole[];
  permissions: IPermission[];
}

export interface ICreateUserPayload {
  email: string;
  firstName: string;
  lastName: string;
  userPassType: number;
  userCreationType: number;
  platform: string;
  projectKey: string;
  organizationId?: string;
}
export interface ICreateUserResponse {
  errors: unknown;
  isSuccess: boolean;
  itemId: string | null;
}

export interface IUpdateUserPayload {
  itemId: string;
  projectKey: string;
  salutation?: string;
  firstName?: string;
  lastName?: string;
  phoneNumber?: string;
  tags?: string[];
  profileImageUrl?: string;
  profileImageId?: string;
  userMfaType?: number;
  mfaEnabled?: boolean;
  roles?: string[];
  permissions?: string[];
  memberships?: IMembership[];
}

export interface IUpdateUserResponse {
  errors: unknown;
  isSuccess: boolean;
  itemId: string | null;
}

export interface ISaveRolesAndPermissionsPayload {
  userId: string;
  roles?: string[];
  permissions?: string[];
  projectKey: string;
}
export interface ISaveRolesAndPermissionsResponse {
  errors: unknown | null;
  isSuccess: boolean;
  itemId: string;
}
export interface IGetSessionPayload {
  page: number;
  pageSize: number;
  filter: { UserId: string };
  projectKey: string;
}
export interface IGetHistoriesPayload {
  page: number;
  pageSize: number;
  filter: { UserId: string };
  projectKey: string;
}

export interface IGeneratePATPayload {
  note?: string;
  codeTtlInMinute: number;
  clientId: string;
}

export interface IGetUserRolesPayload {
  userId: string;
  projectKey: string;
}
export interface IGetUserRolesResponse {
  totalCount: number;
  data: IRole[];
  errors: unknown | null;
}

export interface IGetUserPermissionsPayload {
  userId: string;
  projectKey: string;
}
export interface IGetUserPermissionsResponse {
  errors: unknown | null;
  totalCount: number;
  data: IPermission[];
}

export interface UserDetailsDevicesData {
  site: string;
  device: string;
  noRefreshTokens: string | number;
  lastAccessOn: string;
}

export interface UserDetailsHistoryData {
  event: string;
  time: string;
  site: string | number;
  accessFrom: UserAccessFromData;
}

export interface UserAccessFromData {
  ip: string;
  location: string;
}

// Interface for the data we pass to the InviteUser modal
export interface EditUserData {
  itemId: string;
  firstName: string;
  lastName: string | null;
  email: string;
  phoneNumber: string | null;
  salutation: string;
}

interface DeviceInformation {
  Browser: string;
  OS: string;
  Device: string;
  Brand: string;
  Model: string;
}

export interface IHistories {
  _id: string;
  CreatedDate: string;
  LastUpdatedDate: string;
  CreatedBy: string;
  LastUpdatedBy: string;
  OrganizationIds: string[];
  Tags: string[];
  Event: string;
  ActionBy: string;
  IpAddresses: string;
  DeviceInformation: DeviceInformation;
}

export interface IHistoriesResponse {
  totalCount: number;
  data: IHistories[];
  errors: unknown;
}

export interface IDeviceSession {
  RefreshToken: string;
  TenantId: string;
  IssuedUtc: Date;
  ExpiresUtc: Date;
  UserId: string;
  IpAddresses: string;
  DeviceInformation: DeviceInformation;
  CreateDate: Date;
  UpdateDate: Date;
  IsActive: boolean;
  _id: string;
}

export interface IDeviceSessionResponse {
  totalCount: number;
  data: IDeviceSession[];
  errors: unknown;
}

export interface IPATResponse {
  note: string;
  itemId: string;
  createdDate: Date;
  expiryDate: Date;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  code: string;
  userId: string;
  clientId: string;
}

export const status = [
  {
    value: "Active",
    label: "Active",
  },
  {
    value: "Inactive",
    label: "Inactive",
  },
  {
    value: "Verified",
    label: "Verified",
  },
];

export interface IAccountActivationPayload {
  code: string;
  firstname: string;
  lastname: string;
  password: string;
  captchaCode?: string;
  mailPurpose?: string;
  preventPostEvent: boolean;
  projectKey: string;
}

export interface IAccountActivationResponse {
  errors: unknown | null;
  isSuccess: boolean;
}

export interface IAccountResendActivationPayload {
  userId: string;
  // mailPurpose: string;
  projectKey: string;
}
export interface IAccountResendActivationResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IAccountRecoverPayload {
  email: string;
  captchaCode?: string;
  mailPurpose?: string;
  projectKey: string;
}
export interface IAccountRecoverResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IAccountResetPasswordPayload {
  code: string;
  password: string;
  captchaCode?: string;
  logoutFromAllDevices: boolean;
  projectKey: string;
}
export interface IAccountResetPasswordResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IActivationCodeValidationPayload {
  activationCode: string;
  projectKey: string;
}

export interface IActivationCodeExpirationResponse {
  errors: unknown | null;
  isSuccess: boolean;
  userId: string;
}

export interface ISaveSignUpSettingPayload {
  isEmailPasswordSignUpEnabled: boolean;
  isSSoSignUpEnabled: boolean;
  projectKey: string;
  itemId: string;
}

export interface ISaveSignUpSettingResponse {
  errors: unknown;
  isSuccess: boolean;
  itemId: string;
}

export interface IGetSignUpSettingPayload {
  projectKey: string;
  // itemId: string;
}

export interface IGetSignUpSettingResponse {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  isEmailPasswordSignUpEnabled: boolean;
  isSSoSignUpEnabled: boolean;
}
