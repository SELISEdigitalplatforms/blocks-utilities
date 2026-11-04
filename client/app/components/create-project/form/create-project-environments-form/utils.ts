import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_SHARED_MODULE = "utilities.shared";

export const createProjectEnvironmentFormDefaultValue = {
  environments: [] as { value: string }[],
};

export const createProjectEnvironmentFormSchema = () =>
  z.object({
  environments: z
    .array(
      z.object({
        value: z.string(),
      }),
    )
    .min(1, tStatic(UTILITIES_SHARED_MODULE, "CREATE_PROJECT_ENVIRONMENTS_FORM.AT_LEAST_ONE_ENVIRONMENT")),
});

export const getEnvironmentOptions = () => [
  {
    index: 0,
    label: tStatic(UTILITIES_SHARED_MODULE, "UTILS.DEVELOPMENT"),
    value: "dev",
    subtext: tStatic(UTILITIES_SHARED_MODULE, "UTILS.FOR_DAY_TO_DAY_DEVELOPMENT"),
  },
  {
    index: 1,
    label: tStatic(UTILITIES_SHARED_MODULE, "UTILS.TESTING"),
    value: "test",
    subtext: tStatic(UTILITIES_SHARED_MODULE, "UTILS.FOR_QA_TESTING_AND_VALIDATION"),
  },
  {
    index: 2,
    label: tStatic(UTILITIES_SHARED_MODULE, "UTILS.STAGING"),
    value: "stg",
    subtext: tStatic(UTILITIES_SHARED_MODULE, "UTILS.FOR_FINAL_PRE_PROD_VALIDATION"),
  },
  {
    index: 3,
    label: "IAT",
    value: "iat",
    subtext: tStatic(UTILITIES_SHARED_MODULE, "UTILS.FOR_TESTING_SERVICE_INTEGRATIONS"),
  },
  {
    index: 4,
    label: "UAT",
    value: "uat",
    subtext: tStatic(UTILITIES_SHARED_MODULE, "UTILS.FOR_END_USER_OR_STAKEHOLDER"),
  },
  {
    index: 5,
    label: tStatic(UTILITIES_SHARED_MODULE, "UTILS.PROD_SHADOW"),
    value: "prod-shadow",
    subtext: tStatic(UTILITIES_SHARED_MODULE, "UTILS.FOR_MIRRORING_PRODUCTION_DATA"),
  },
  {
    index: 6,
    label: tStatic(UTILITIES_SHARED_MODULE, "UTILS.PRE_PROD"),
    value: "pre-prod",
    subtext: tStatic(UTILITIES_SHARED_MODULE, "UTILS.FOR_LOAD_TESTING_SECURITY_CHECKS"),
  },
  {
    index: 7,
    label: tStatic(UTILITIES_SHARED_MODULE, "UTILS.PRODUCTION"),
    value: "prod",
    subtext: tStatic(UTILITIES_SHARED_MODULE, "UTILS.THE_LIVE_ENVIRONMENT_SERVING"),
  },
];
