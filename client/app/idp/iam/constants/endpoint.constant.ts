import { getRuntimeEnv } from "@/lib/runtime-env";

// ─── IAM Base URL ───────────────────────────────────────────────────────────────

const IAM_BASE_URL = getRuntimeEnv("BLOCKS_IAM_BASE_URL") || "https://dev-iam.blocksdevelopers.com";

// ─── Logic Base URL (for specific endpoints) ────────────────────────────────────

const LOGIC_BASE_URL = getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || "https://dev-logic.blocksdevelopers.com";

// ─── Subpaths ─────────────────────────────────────────────────────────────────

const IAM_SUBPATH = `${IAM_BASE_URL}/api/iam`;
const AUTH_SUBPATH = `${IAM_BASE_URL}/api/Authentication`;
const IAM_CONFIG_SUBPATH = `${IAM_BASE_URL}/api/IAM`;
const LOGIC_IAM_SUBPATH = `${LOGIC_BASE_URL}/api/iam`;
const LOGIC_AUTH_SUBPATH = `${LOGIC_BASE_URL}/api/Authentication`;

// ─── User endpoints (user.service) ──────────────────────────────────────────

export const USER_ENDPOINTS = {
  GET_USERS: `${IAM_SUBPATH}/users`,
  GET_USER: `${IAM_SUBPATH}/user`,
  USER_INFO: `${IAM_BASE_URL}/api/UserInfo`,
  ME: `${IAM_SUBPATH}/me`,
  CREATE: `${IAM_SUBPATH}/Create`,
  UPDATE: `${IAM_SUBPATH}/Update`,
  GET_SIGNUP_SETTING: `${IAM_SUBPATH}/GetSignUpSetting`,
  SAVE_SIGNUP_SETTING: `${IAM_SUBPATH}/SaveSignUpSetting`,
  SAVE_ROLES_AND_PERMISSIONS: `${IAM_SUBPATH}/SaveRolesAndPermissions`,
  GET_SESSIONS: `${LOGIC_IAM_SUBPATH}/GetSessions`,
  GET_HISTORIES: `${LOGIC_IAM_SUBPATH}/GetHistories`,
  GET_USER_CODES: `${LOGIC_AUTH_SUBPATH}/GetUserCodes`,
  GENERATE_USER_CODE: `${AUTH_SUBPATH}/GenerateUserCode`,
  GET_USER_ROLES: `${IAM_SUBPATH}/GetUserRoles`,
  GET_USER_PERMISSIONS: `${IAM_SUBPATH}/GetUserPermissions`,
  DEACTIVATE: `${IAM_SUBPATH}/Deactivate`,
} as const;

// ─── Account endpoints (account.service) ────────────────────────────────────

export const ACCOUNT_ENDPOINTS = {
  ACTIVATE: `${IAM_SUBPATH}/Activate`,
  RESEND_ACTIVATION: `${IAM_SUBPATH}/ResendActivation`,
  RECOVER: `${IAM_SUBPATH}/Recover`,
  RESET_PASSWORD: `${IAM_SUBPATH}/ResetPassword`,
  VALIDATE_ACTIVATION_CODE: `${IAM_SUBPATH}/ValidateActivationCode`,
} as const;

// ─── Role endpoints (role.service) ──────────────────────────────────────────

export const ROLE_ENDPOINTS = {
  GET_ROLES: `${IAM_SUBPATH}/GetRoles`,
  GET_ROLE: `${IAM_SUBPATH}/GetRole`,
  CREATE_ROLE: `${IAM_SUBPATH}/CreateRole`,
  UPDATE_ROLE: `${IAM_SUBPATH}/UpdateRole`,
  SET_ROLES: `${IAM_SUBPATH}/SetRoles`,
} as const;

// ─── Permission endpoints (permission.service) ─────────────────────────────

export const PERMISSION_ENDPOINTS = {
  GET_PERMISSIONS: `${IAM_SUBPATH}/GetPermissions`,
  GET_PERMISSION: `${IAM_SUBPATH}/GetPermission`,
  GET_PERMISSIONS_GROUP_BY_SEVERITY: `${IAM_SUBPATH}/GetPermissionsGroupBySeverity`,
  CREATE_PERMISSION: `${IAM_SUBPATH}/CreatePermission`,
  UPDATE_PERMISSION: `${IAM_SUBPATH}/UpdatePermission`,
  GET_RESOURCE_GROUPS: `${IAM_SUBPATH}/GetResourceGroups`,
} as const;

// ─── Organization endpoints (organization.service) ─────────────────────────

export const ORGANIZATION_ENDPOINTS = {
  GET_ORGANIZATIONS: `${IAM_SUBPATH}/GetOrganizations`,
  GET_ORGANIZATION: `${IAM_SUBPATH}/GetOrganization`,
  SAVE_ORGANIZATION: `${IAM_SUBPATH}/SaveOrganization`,
  GET_ORGANIZATION_CONFIG: `${IAM_SUBPATH}/GetOrganizationConfig`,
  SAVE_ORGANIZATION_CONFIG: `${IAM_SUBPATH}/SaveOrganizationConfig`,
} as const;

// ─── IAM configuration endpoints (configuration.service) ───────────────────

export const IAM_CONFIGURATION_ENDPOINTS = {
  GET: `${IAM_CONFIG_SUBPATH}/Get`,
  SAVE: `${IAM_CONFIG_SUBPATH}/Save`,
} as const;
