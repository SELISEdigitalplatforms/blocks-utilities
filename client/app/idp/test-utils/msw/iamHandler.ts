import { http, HttpResponse, type JsonBodyType } from "msw";
import {
  mockUsersResponse,
  mockUser,
  mockRolesResponse,
  mockGetRoleResponse,
  mockPermissionsResponse,
  mockGetPermissionByIdResponse,
  mockResourceGroupResponse,
  mockOrganizationsResponse,
  mockGetOrganizationByIdResponse,
  mockOrganizationConfigResponse,
  mockGetIamConfigResponse,
  mockSignUpSettingResponse,
  mockActivationCodeExpirationResponse,
} from "../__mocks__/iam.data.mock";
import { mockSuccessResponse, mockSuccessResponseWithItemId } from "@/test-utils/__mocks__";
import {
  USER_ENDPOINTS,
  ACCOUNT_ENDPOINTS,
  ROLE_ENDPOINTS,
  PERMISSION_ENDPOINTS,
  ORGANIZATION_ENDPOINTS,
  IAM_CONFIGURATION_ENDPOINTS,
} from "../../iam/constants/endpoint.constant";

// ─── Endpoint Patterns ────────────────────────────────────────────────────────

// User
const GET_USERS_PATTERN = new RegExp(USER_ENDPOINTS.GET_USERS);
const GET_USER_PATTERN = new RegExp(`${USER_ENDPOINTS.GET_USER}$`);
const GET_USER_BY_ID_PATTERN = new RegExp(`${USER_ENDPOINTS.GET_USER}\\?`);
const CREATE_USER_PATTERN = new RegExp(USER_ENDPOINTS.CREATE);
const UPDATE_USER_PATTERN = new RegExp(USER_ENDPOINTS.UPDATE);
const GET_SIGNUP_SETTING_PATTERN = new RegExp(`${USER_ENDPOINTS.GET_SIGNUP_SETTING}\\?`);
const SAVE_SIGNUP_SETTING_PATTERN = new RegExp(USER_ENDPOINTS.SAVE_SIGNUP_SETTING);
const SAVE_ROLES_AND_PERMISSIONS_PATTERN = new RegExp(USER_ENDPOINTS.SAVE_ROLES_AND_PERMISSIONS);
const GET_SESSIONS_PATTERN = new RegExp(`${USER_ENDPOINTS.GET_SESSIONS}\\?`);
const GET_HISTORIES_PATTERN = new RegExp(`${USER_ENDPOINTS.GET_HISTORIES}\\?`);
const GET_USER_CODES_PATTERN = new RegExp(USER_ENDPOINTS.GET_USER_CODES);
const GENERATE_USER_CODE_PATTERN = new RegExp(USER_ENDPOINTS.GENERATE_USER_CODE);
const GET_USER_ROLES_PATTERN = new RegExp(`${USER_ENDPOINTS.GET_USER_ROLES}\\?`);
const GET_USER_PERMISSIONS_PATTERN = new RegExp(`${USER_ENDPOINTS.GET_USER_PERMISSIONS}\\?`);
const DEACTIVATE_PATTERN = new RegExp(USER_ENDPOINTS.DEACTIVATE);

// Account
const ACTIVATE_PATTERN = new RegExp(ACCOUNT_ENDPOINTS.ACTIVATE);
const RESEND_ACTIVATION_PATTERN = new RegExp(ACCOUNT_ENDPOINTS.RESEND_ACTIVATION);
const RECOVER_PATTERN = new RegExp(ACCOUNT_ENDPOINTS.RECOVER);
const RESET_PASSWORD_PATTERN = new RegExp(ACCOUNT_ENDPOINTS.RESET_PASSWORD);
const VALIDATE_ACTIVATION_CODE_PATTERN = new RegExp(ACCOUNT_ENDPOINTS.VALIDATE_ACTIVATION_CODE);

// Role
const GET_ROLES_PATTERN = new RegExp(ROLE_ENDPOINTS.GET_ROLES);
const GET_ROLE_PATTERN = new RegExp(`${ROLE_ENDPOINTS.GET_ROLE}\\?`);
const CREATE_ROLE_PATTERN = new RegExp(ROLE_ENDPOINTS.CREATE_ROLE);
const UPDATE_ROLE_PATTERN = new RegExp(ROLE_ENDPOINTS.UPDATE_ROLE);
const SET_ROLES_PATTERN = new RegExp(ROLE_ENDPOINTS.SET_ROLES);

// Permission
const GET_PERMISSIONS_PATTERN = new RegExp(PERMISSION_ENDPOINTS.GET_PERMISSIONS);
const GET_PERMISSION_PATTERN = new RegExp(`${PERMISSION_ENDPOINTS.GET_PERMISSION}\\?`);
const CREATE_PERMISSION_PATTERN = new RegExp(PERMISSION_ENDPOINTS.CREATE_PERMISSION);
const UPDATE_PERMISSION_PATTERN = new RegExp(PERMISSION_ENDPOINTS.UPDATE_PERMISSION);
const GET_RESOURCE_GROUPS_PATTERN = new RegExp(`${PERMISSION_ENDPOINTS.GET_RESOURCE_GROUPS}\\?`);

// Organization
const GET_ORGANIZATIONS_PATTERN = new RegExp(`${ORGANIZATION_ENDPOINTS.GET_ORGANIZATIONS}\\?`);
const GET_ORGANIZATION_PATTERN = new RegExp(`${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION}\\?`);
const SAVE_ORGANIZATION_PATTERN = new RegExp(ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION);
const GET_ORGANIZATION_CONFIG_PATTERN = new RegExp(
  `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION_CONFIG}\\?`,
);
const SAVE_ORGANIZATION_CONFIG_PATTERN = new RegExp(
  ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION_CONFIG,
);

// IAM Configuration
const GET_IAM_CONFIG_PATTERN = new RegExp(`${IAM_CONFIGURATION_ENDPOINTS.GET}\\?`);
const SAVE_IAM_CONFIG_PATTERN = new RegExp(IAM_CONFIGURATION_ENDPOINTS.SAVE);

// ─── Default Handlers (happy-path) ───────────────────────────────────────────

export const iamHandlers = [
  // User
  http.post(GET_USERS_PATTERN, () => HttpResponse.json(mockUsersResponse)),
  http.get(GET_USER_PATTERN, () => HttpResponse.json({ data: mockUser })),
  http.get(GET_USER_BY_ID_PATTERN, () =>
    HttpResponse.json({ data: mockUser, errors: null, roles: [], permissions: [] }),
  ),
  http.post(CREATE_USER_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.post(UPDATE_USER_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.get(GET_SIGNUP_SETTING_PATTERN, () => HttpResponse.json(mockSignUpSettingResponse)),
  http.post(SAVE_SIGNUP_SETTING_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.post(SAVE_ROLES_AND_PERMISSIONS_PATTERN, () =>
    HttpResponse.json(mockSuccessResponseWithItemId),
  ),
  http.get(GET_SESSIONS_PATTERN, () =>
    HttpResponse.json({ data: [], totalCount: 0, errors: null }),
  ),
  http.get(GET_HISTORIES_PATTERN, () =>
    HttpResponse.json({ data: [], totalCount: 0, errors: null }),
  ),
  http.get(GET_USER_CODES_PATTERN, () => HttpResponse.json({ data: [], errors: null })),
  http.post(GENERATE_USER_CODE_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
  http.get(GET_USER_ROLES_PATTERN, () =>
    HttpResponse.json({ data: [], totalCount: 0, errors: null }),
  ),
  http.get(GET_USER_PERMISSIONS_PATTERN, () =>
    HttpResponse.json({ data: [], totalCount: 0, errors: null }),
  ),
  http.post(DEACTIVATE_PATTERN, () => HttpResponse.json(mockSuccessResponse)),

  // Account
  http.post(ACTIVATE_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
  http.post(RESEND_ACTIVATION_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
  http.post(RECOVER_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
  http.post(RESET_PASSWORD_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
  http.post(VALIDATE_ACTIVATION_CODE_PATTERN, () =>
    HttpResponse.json(mockActivationCodeExpirationResponse),
  ),

  // Role
  http.post(GET_ROLES_PATTERN, () => HttpResponse.json(mockRolesResponse)),
  http.get(GET_ROLE_PATTERN, () => HttpResponse.json(mockGetRoleResponse)),
  http.post(CREATE_ROLE_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.post(UPDATE_ROLE_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.post(SET_ROLES_PATTERN, () => HttpResponse.json(mockSuccessResponse)),

  // Permission
  http.post(GET_PERMISSIONS_PATTERN, () => HttpResponse.json(mockPermissionsResponse)),
  http.get(GET_PERMISSION_PATTERN, () => HttpResponse.json(mockGetPermissionByIdResponse)),
  http.post(CREATE_PERMISSION_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.post(UPDATE_PERMISSION_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.get(GET_RESOURCE_GROUPS_PATTERN, () => HttpResponse.json(mockResourceGroupResponse)),

  // Organization
  http.get(GET_ORGANIZATIONS_PATTERN, () => HttpResponse.json(mockOrganizationsResponse)),
  http.get(GET_ORGANIZATION_PATTERN, () => HttpResponse.json(mockGetOrganizationByIdResponse)),
  http.post(SAVE_ORGANIZATION_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
  http.get(GET_ORGANIZATION_CONFIG_PATTERN, () =>
    HttpResponse.json(mockOrganizationConfigResponse),
  ),
  http.post(SAVE_ORGANIZATION_CONFIG_PATTERN, () => HttpResponse.json(mockSuccessResponse)),

  // IAM Configuration
  http.get(GET_IAM_CONFIG_PATTERN, () => HttpResponse.json(mockGetIamConfigResponse)),
  http.post(SAVE_IAM_CONFIG_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
];

// ─── Per-Test Override Factories ──────────────────────────────────────────────

// User
export const getUsersHandler = (response: JsonBodyType = mockUsersResponse) =>
  http.post(GET_USERS_PATTERN, () => HttpResponse.json(response));

export const getUsersErrorHandler = (status = 500) =>
  http.post(GET_USERS_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const getUserHandler = (response: JsonBodyType = { data: mockUser }) =>
  http.get(GET_USER_PATTERN, () => HttpResponse.json(response));

export const getUserByIdHandler = (
  response: JsonBodyType = { data: mockUser, errors: null, roles: [], permissions: [] },
) => http.get(GET_USER_BY_ID_PATTERN, () => HttpResponse.json(response));

export const createUserHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(CREATE_USER_PATTERN, () => HttpResponse.json(response));

export const createUserErrorHandler = (status = 500) =>
  http.post(CREATE_USER_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const updateUserHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(UPDATE_USER_PATTERN, () => HttpResponse.json(response));

export const getSignUpSettingHandler = (response: JsonBodyType = mockSignUpSettingResponse) =>
  http.get(GET_SIGNUP_SETTING_PATTERN, () => HttpResponse.json(response));

export const saveSignUpSettingHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(SAVE_SIGNUP_SETTING_PATTERN, () => HttpResponse.json(response));

export const deactivateHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(DEACTIVATE_PATTERN, () => HttpResponse.json(response));

// Account
export const accountActivationHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(ACTIVATE_PATTERN, () => HttpResponse.json(response));

export const accountActivationErrorHandler = (status = 500) =>
  http.post(ACTIVATE_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const resendActivationHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(RESEND_ACTIVATION_PATTERN, () => HttpResponse.json(response));

export const recoverHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(RECOVER_PATTERN, () => HttpResponse.json(response));

export const resetPasswordHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(RESET_PASSWORD_PATTERN, () => HttpResponse.json(response));

export const validateActivationCodeHandler = (
  response: JsonBodyType = mockActivationCodeExpirationResponse,
) => http.post(VALIDATE_ACTIVATION_CODE_PATTERN, () => HttpResponse.json(response));

// Role
export const getRolesHandler = (response: JsonBodyType = mockRolesResponse) =>
  http.post(GET_ROLES_PATTERN, () => HttpResponse.json(response));

export const getRolesErrorHandler = (status = 500) =>
  http.post(GET_ROLES_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const getRoleHandler = (response: JsonBodyType = mockGetRoleResponse) =>
  http.get(GET_ROLE_PATTERN, () => HttpResponse.json(response));

export const createRoleHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(CREATE_ROLE_PATTERN, () => HttpResponse.json(response));

export const updateRoleHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(UPDATE_ROLE_PATTERN, () => HttpResponse.json(response));

export const setRolesHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(SET_ROLES_PATTERN, () => HttpResponse.json(response));

// Permission
export const getPermissionsHandler = (response: JsonBodyType = mockPermissionsResponse) =>
  http.post(GET_PERMISSIONS_PATTERN, () => HttpResponse.json(response));

export const getPermissionsErrorHandler = (status = 500) =>
  http.post(GET_PERMISSIONS_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const getPermissionHandler = (response: JsonBodyType = mockGetPermissionByIdResponse) =>
  http.get(GET_PERMISSION_PATTERN, () => HttpResponse.json(response));

export const createPermissionHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(CREATE_PERMISSION_PATTERN, () => HttpResponse.json(response));

export const updatePermissionHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(UPDATE_PERMISSION_PATTERN, () => HttpResponse.json(response));

export const getResourceGroupsHandler = (response: JsonBodyType = mockResourceGroupResponse) =>
  http.get(GET_RESOURCE_GROUPS_PATTERN, () => HttpResponse.json(response));

// Organization
export const getOrganizationsHandler = (response: JsonBodyType = mockOrganizationsResponse) =>
  http.get(GET_ORGANIZATIONS_PATTERN, () => HttpResponse.json(response));

export const getOrganizationsErrorHandler = (status = 500) =>
  http.get(GET_ORGANIZATIONS_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const getOrganizationHandler = (response: JsonBodyType = mockGetOrganizationByIdResponse) =>
  http.get(GET_ORGANIZATION_PATTERN, () => HttpResponse.json(response));

export const saveOrganizationHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(SAVE_ORGANIZATION_PATTERN, () => HttpResponse.json(response));

export const getOrganizationConfigHandler = (
  response: JsonBodyType = mockOrganizationConfigResponse,
) => http.get(GET_ORGANIZATION_CONFIG_PATTERN, () => HttpResponse.json(response));

export const saveOrganizationConfigHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(SAVE_ORGANIZATION_CONFIG_PATTERN, () => HttpResponse.json(response));

// IAM Configuration
export const getIamConfigHandler = (response: JsonBodyType = mockGetIamConfigResponse) =>
  http.get(GET_IAM_CONFIG_PATTERN, () => HttpResponse.json(response));

export const getIamConfigErrorHandler = (status = 500) =>
  http.get(GET_IAM_CONFIG_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const saveIamConfigHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(SAVE_IAM_CONFIG_PATTERN, () => HttpResponse.json(response));
