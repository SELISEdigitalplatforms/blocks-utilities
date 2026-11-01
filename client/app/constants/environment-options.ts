import { tStatic } from "@seliseblocks/genesis-os";

export const getEnvironmentOptions = () => [
  { index: 0, label: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.DEVELOPMENT"), value: "dev", subtext: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.FOR_DAY_TO_DAY_DEVELOPMENT") },
  { index: 1, label: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.TESTING"), value: "test", subtext: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.FOR_QA_TESTING_AND_VALIDATION") },
  { index: 2, label: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.STAGING"), value: "stg", subtext: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.FOR_FINAL_PRE_PROD_VALIDATION") },
  { index: 3, label: "IAT", value: "iat", subtext: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.FOR_TESTING_SERVICE_INTEGRATIONS") },
  { index: 4, label: "UAT", value: "uat", subtext: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.FOR_END_USER_OR_STAKEHOLDER") },
  { index: 5, label: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.PROD_SHADOW"), value: "prod-shadow", subtext: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.FOR_MIRRORING_PRODUCTION_DATA") },
  { index: 6, label: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.PRE_PROD"), value: "pre-prod", subtext: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.FOR_LOAD_TESTING_SECURITY_CHECKS") },
  { index: 7, label: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.PRODUCTION"), value: "prod", subtext: tStatic("utilities.shared", "ENVIRONMENT_OPTIONS.THE_LIVE_ENVIRONMENT_SERVING") },
];
