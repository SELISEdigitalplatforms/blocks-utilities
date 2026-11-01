import { isValidSubdomain } from "@/lib/domain";
import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const editDomainFormSchema = () =>
  z.object({
  domains: z.array(
    z.object({
      itemId: z.string().min(1, tStatic("utilities.shared", "EDIT_DOMAIN_FORM.REPOSITORY_ID_REQUIRED")),
      repoUrl: z.string().optional(),
      customDeploymentUrl: z
        .string()
        .trim()
        .min(1, tStatic("utilities.shared", "EDIT_DOMAIN_FORM.CUSTOM_DEPLOYMENT_URL_REQUIRED"))
        .refine(isValidSubdomain, {
          message:
            tStatic("utilities.shared", "EDIT_DOMAIN_FORM.INVALID_SUBDOMAIN"),
        }),
    }),
  ),
});
