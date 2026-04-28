import { API_BASES } from "@/constants/endpoint.constant";

// ─── Subpaths ─────────────────────────────────────────────────────────────────

const IAM_SUBPATH = "/Iam";
const AUTH_SUBPATH = "/Authentication";
const IAM_CONFIG_SUBPATH = "/IAM";

// ─── User endpoints (user.service) ──────────────────────────────────────────

export const USER_ENDPOINTS = {
  GET_USERS: `${API_BASES.IDP}${IAM_SUBPATH}/GetUsers`,
  GET_USER: `${API_BASES.IDP}${IAM_SUBPATH}/GetUser`,
  CREATE: `${API_BASES.IDP}${IAM_SUBPATH}/Create`,
  UPDATE: `${API_BASES.IDP}${IAM_SUBPATH}/Update`,
  GET_SIGNUP_SETTING: `${API_BASES.IDP}${IAM_SUBPATH}/GetSignUpSetting`,
  SAVE_SIGNUP_SETTING: `${API_BASES.IDP}${IAM_SUBPATH}/SaveSignUpSetting`,
  SAVE_ROLES_AND_PERMISSIONS: `${API_BASES.IDP}${IAM_SUBPATH}/SaveRolesAndPermissions`,
  GET_SESSIONS: `${API_BASES.IDP}${IAM_SUBPATH}/GetSessions`,
  GET_HISTORIES: `${API_BASES.IDP}${IAM_SUBPATH}/GetHistories`,
  GET_USER_CODES: `${API_BASES.IDP}${AUTH_SUBPATH}/GetUserCodes`,
  GENERATE_USER_CODE: `${API_BASES.IDP}${AUTH_SUBPATH}/GenerateUserCode`,
  GET_USER_ROLES: `${API_BASES.IDP}${IAM_SUBPATH}/GetUserRoles`,
  GET_USER_PERMISSIONS: `${API_BASES.IDP}${IAM_SUBPATH}/GetUserPermissions`,
  DEACTIVATE: `${API_BASES.IDP}${IAM_SUBPATH}/Deactivate`,
} as const;

// ─── Account endpoints (account.service) ────────────────────────────────────

export const ACCOUNT_ENDPOINTS = {
  ACTIVATE: `${API_BASES.IDP}${IAM_SUBPATH}/Activate`,
  RESEND_ACTIVATION: `${API_BASES.IDP}${IAM_SUBPATH}/ResendActivation`,
  RECOVER: `${API_BASES.IDP}${IAM_SUBPATH}/Recover`,
  RESET_PASSWORD: `${API_BASES.IDP}${IAM_SUBPATH}/ResetPassword`,
  VALIDATE_ACTIVATION_CODE: `${API_BASES.IDP}${IAM_SUBPATH}/ValidateActivationCode`,
} as const;

// ─── Role endpoints (role.service) ──────────────────────────────────────────

export const ROLE_ENDPOINTS = {
  GET_ROLES: `${API_BASES.IDP}${IAM_SUBPATH}/GetRoles`,
  GET_ROLE: `${API_BASES.IDP}${IAM_SUBPATH}/GetRole`,
  CREATE_ROLE: `${API_BASES.IDP}${IAM_SUBPATH}/CreateRole`,
  UPDATE_ROLE: `${API_BASES.IDP}${IAM_SUBPATH}/UpdateRole`,
  SET_ROLES: `${API_BASES.IDP}${IAM_SUBPATH}/SetRoles`,
} as const;

// ─── Permission endpoints (permission.service) ─────────────────────────────

export const PERMISSION_ENDPOINTS = {
  GET_PERMISSIONS: `${API_BASES.IDP}${IAM_SUBPATH}/GetPermissions`,
  GET_PERMISSION: `${API_BASES.IDP}${IAM_SUBPATH}/GetPermission`,
  GET_PERMISSIONS_GROUP_BY_SEVERITY: `${API_BASES.IDP}${IAM_SUBPATH}/GetPermissionsGroupBySeverity`,
  CREATE_PERMISSION: `${API_BASES.IDP}${IAM_SUBPATH}/CreatePermission`,
  UPDATE_PERMISSION: `${API_BASES.IDP}${IAM_SUBPATH}/UpdatePermission`,
  GET_RESOURCE_GROUPS: `${API_BASES.IDP}${IAM_SUBPATH}/GetResourceGroups`,
} as const;

// ─── Organization endpoints (organization.service) ─────────────────────────

export const ORGANIZATION_ENDPOINTS = {
  GET_ORGANIZATIONS: `${API_BASES.IDP}${IAM_SUBPATH}/GetOrganizations`,
  GET_ORGANIZATION: `${API_BASES.IDP}${IAM_SUBPATH}/GetOrganization`,
  SAVE_ORGANIZATION: `${API_BASES.IDP}${IAM_SUBPATH}/SaveOrganization`,
  GET_ORGANIZATION_CONFIG: `${API_BASES.IDP}${IAM_SUBPATH}/GetOrganizationConfig`,
  SAVE_ORGANIZATION_CONFIG: `${API_BASES.IDP}${IAM_SUBPATH}/SaveOrganizationConfig`,
} as const;

// ─── IAM configuration endpoints (configuration.service) ───────────────────

export const IAM_CONFIGURATION_ENDPOINTS = {
  GET: `${API_BASES.CLOUD_CONFIGURATION}${IAM_CONFIG_SUBPATH}/Get`,
  SAVE: `${API_BASES.CLOUD_CONFIGURATION}${IAM_CONFIG_SUBPATH}/Save`,
} as const;
