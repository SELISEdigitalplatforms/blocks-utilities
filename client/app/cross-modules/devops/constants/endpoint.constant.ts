export const CLOUD_BUILD_ENDPOINTS = {
  // Authentication & Authorization
  ACCESS_TOKEN: "/api/auth/accessToken",
  IS_AUTHORIZED: "/api/auth/isAuthorized",
  REMOVE_AUTHORIZATION: "/api/auth/removeAuthorization",
  REMOVE_ACCESS_TOKEN: "/api/auth/removeAccessToken",

  // GitHub Repositories
  GITHUB_REPOS: "/api/github/repos",
  GITHUB_USER: "/api/github/user",
  GITHUB_BRANCHES: "/api/github/branches",
  GITHUB_BRANCH_EXISTS: "/api/github/branchExists",

  // Build & Deployment
  BUILD_BUILD: "/api/build/clone",
  RUN_BUILD: "/api/build/run",
  MANUAL: "/api/build/manual",
  BUILD: "/api/build",

  // Repository Management
  REPOS: "/api/repos",
  REPOS_LIST: "/api/repos/list",
  REPO_DETAILS: "/api/repos/details",

  // Build Settings
  SETTINGS: "/api/settings",
};

export const MIGRATION_ENDPOINTS = {
  GET_STATUS: "/api/identifier/migration/status",
  START_MIGRATION: "/api/identifier/migration/start",
};
