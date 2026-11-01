import { isValidSubdomain } from "@/lib/domain";
import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_SHARED_MODULE = "utilities.shared";

export const editDomainFormSchema = () =>
  z.object({
  domains: z.array(
    z.object({
      itemId: z.string().min(1, tStatic(UTILITIES_SHARED_MODULE, "EDIT_DOMAIN_FORM.REPOSITORY_ID_REQUIRED")),
      repoUrl: z.string().optional(),
      customDeploymentUrl: z
        .string()
        .trim()
        .min(1, tStatic(UTILITIES_SHARED_MODULE, "EDIT_DOMAIN_FORM.CUSTOM_DEPLOYMENT_URL_REQUIRED"))
        .refine(isValidSubdomain, {
          message:
            tStatic(UTILITIES_SHARED_MODULE, "EDIT_DOMAIN_FORM.INVALID_SUBDOMAIN"),
        }),
    }),
  ),
});
