import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

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

export const createSsoProviderConfigBaseSchema = () => z.object({
  provider: z.string().trim(),
  audience: z.string().url({ message: tStatic("utilities.idp.authentication", "SSO_PROVIDER_CONFIG_SCHEMA.AUDIENCE_URL_MUST_BE_A") }).trim(),
  redirectUrl: z.string().url({ message: tStatic("utilities.idp.authentication", "SSO_PROVIDER_CONFIG_SCHEMA.REDIRECT_URL_MUST_BE_A") }).trim(),
});

export const createSsoOAuthProviderSchema = () => createSsoProviderConfigBaseSchema().extend({
  clientId: z.string().trim().nonempty(tStatic("utilities.idp.authentication", "SSO_PROVIDER_CONFIG_SCHEMA.CLIENT_ID_IS_REQUIRED")),
  clientSecret: z.string().trim().nonempty(tStatic("utilities.idp.authentication", "SSO_PROVIDER_CONFIG_SCHEMA.CLIENT_SECRET_IS_REQUIRED")),
  initialRoles: z.array(z.string()).optional(),
  initialPermissions: z.array(z.string()).optional(),
  userRoles: z.array(ssoRoleSchema),
  userPermissions: z.array(ssoPermissionSchema),
});

export type SSORole = z.infer<typeof ssoRoleSchema>;
export type SSOPermission = z.infer<typeof ssoPermissionSchema>;
export type SSOProviderConfigBase = z.infer<ReturnType<typeof createSsoProviderConfigBaseSchema>>;
export type SSOOAuthProviderConfig = z.infer<ReturnType<typeof createSsoOAuthProviderSchema>>;
