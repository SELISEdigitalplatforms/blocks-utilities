import { z } from "zod";

export const ssoRoleSchema = z.object({
  itemId: z.string(),
  name: z.string(),
  slug: z.string(),
  description: z.string(),
});

export const ssoPermissionSchema = z.object({
  itemId: z.string(),
  name: z.string(),
  description: z.string(),
});

export const ssoProviderConfigBaseSchema = z.object({
  provider: z.string().trim(),
  audience: z.string().url({ message: "Audience URL must be a valid URL." }).trim(),
  redirectUrl: z.string().url({ message: "Redirect URL must be a valid URL." }).trim(),
});

export const ssoOAuthProviderSchema = ssoProviderConfigBaseSchema.extend({
  clientId: z.string().trim().nonempty("Client id is required"),
  clientSecret: z.string().trim().nonempty("Client secret is required"),
  initialRoles: z.array(z.string()).optional(),
  initialPermissions: z.array(z.string()).optional(),
  userRoles: z.array(ssoRoleSchema),
  userPermissions: z.array(ssoPermissionSchema),
});

export type SSORole = z.infer<typeof ssoRoleSchema>;
export type SSOPermission = z.infer<typeof ssoPermissionSchema>;
export type SSOProviderConfigBase = z.infer<typeof ssoProviderConfigBaseSchema>;
export type SSOOAuthProviderConfig = z.infer<typeof ssoOAuthProviderSchema>;
