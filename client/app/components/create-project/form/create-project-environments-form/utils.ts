import { z } from "zod";

export const createProjectEnvironmentFormDefaultValue = {
  environments: [] as { value: string }[],
};

export const createProjectEnvironmentFormSchema = z.object({
  environments: z
    .array(
      z.object({
        value: z.string(),
      }),
    )
    .min(1, "At least one environment is required"),
});

export const environmentOptions = [
  {
    index: 0,
    label: "Development",
    value: "dev",
    subtext: "For day-to-day development and testing unstable changes.",
  },
  {
    index: 1,
    label: "Testing",
    value: "test",
    subtext: "For QA testing and validation of new features.",
  },
  {
    index: 2,
    label: "Staging",
    value: "stg",
    subtext: "For final pre-prod validation in a near-production replica.",
  },
  {
    index: 3,
    label: "IAT",
    value: "iat",
    subtext: "For testing service integrations across modules/systems.",
  },
  {
    index: 4,
    label: "UAT",
    value: "uat",
    subtext: "For end-user or stakeholder validation of new features.",
  },
  {
    index: 5,
    label: "Prod Shadow",
    value: "prod-shadow",
    subtext: "For mirroring production data/traffic to validate changes invisibly.",
  },
  {
    index: 6,
    label: "Pre-Prod",
    value: "pre-prod",
    subtext: "For load testing, security checks, or final sanity tests before production.",
  },
  {
    index: 7,
    label: "Production",
    value: "prod",
    subtext: "The live environment serving actual users.",
  },
];
