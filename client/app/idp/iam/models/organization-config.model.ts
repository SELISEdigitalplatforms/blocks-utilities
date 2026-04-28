import { z } from "zod";

export interface IOrganizationConfigResponse {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  allowCreationFromCloud: boolean;
  allowCreationFromConstruct: boolean;
  isMultiOrgEnabled: boolean;
  roles?: string[];
}

export interface IOrganizationConfigPayload {
  itemId: string;
  allowCreationFromCloud: boolean;
  allowCreationFromConstruct: boolean;
  isMultiOrgEnabled: boolean;
  roles?: string[];
  projectKey: string;
}

export interface IOrganizationConfigSaveResponse {
  errors: unknown;
  isSuccess: boolean;
}

export const organizationConfigFormSchema = z.object({
  isMultiOrgEnabled: z.boolean(),
  allowCreationFromCloud: z.boolean(),
  allowCreationFromConstruct: z.boolean(),
});

export type IOrganizationConfigForm = z.infer<typeof organizationConfigFormSchema>;

export const organizationConfigFormDefaultValues: IOrganizationConfigForm = {
  isMultiOrgEnabled: false,
  allowCreationFromCloud: true,
  allowCreationFromConstruct: false,
};
