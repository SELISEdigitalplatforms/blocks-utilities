import { isValidSubdomain } from "@/lib/domain";
import { z } from "zod";

export const editDomainFormSchema = z.object({
  domains: z.array(
    z.object({
      itemId: z.string().min(1, "Repository ID is required"),
      repoUrl: z.string().optional(),
      customDeploymentUrl: z
        .string()
        .trim()
        .min(1, "Custom Deployment URL is required")
        .refine(isValidSubdomain, {
          message:
            "Invalid subdomain. Must start with http:// or https:// and follow subdomain rules.",
        }),
    }),
  ),
});
