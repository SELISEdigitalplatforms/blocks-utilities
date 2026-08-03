import { describe, expect, it } from "vitest";
import {
  ssoRoleSchema,
  ssoPermissionSchema,
  ssoProviderConfigBaseSchema,
  ssoOAuthProviderSchema,
} from "./sso-provider-config.schema";

describe("sso provider config schemas", () => {
  it("validates a role shape", () => {
    expect(
      ssoRoleSchema.safeParse({
        itemId: "1",
        name: "Admin",
        slug: "admin",
        description: "d",
      }).success,
    ).toBe(true);
  });

  it("validates a permission shape", () => {
    expect(
      ssoPermissionSchema.safeParse({ itemId: "1", name: "read", description: "d" })
        .success,
    ).toBe(true);
  });

  it("requires valid audience and redirect urls in the base schema", () => {
    expect(
      ssoProviderConfigBaseSchema.safeParse({
        provider: "google",
        audience: "https://api.example.com",
        redirectUrl: "https://app.example.com/cb",
      }).success,
    ).toBe(true);
    expect(
      ssoProviderConfigBaseSchema.safeParse({
        provider: "google",
        audience: "bad",
        redirectUrl: "https://app.example.com/cb",
      }).success,
    ).toBe(false);
  });

  it("accepts a full oauth provider config", () => {
    const result = ssoOAuthProviderSchema.safeParse({
      provider: "google",
      audience: "https://api.example.com",
      redirectUrl: "https://app.example.com/cb",
      clientId: "cid",
      clientSecret: "secret",
      userRoles: [],
      userPermissions: [],
    });
    expect(result.success).toBe(true);
  });

  it("rejects an oauth config missing the client id", () => {
    const result = ssoOAuthProviderSchema.safeParse({
      provider: "google",
      audience: "https://api.example.com",
      redirectUrl: "https://app.example.com/cb",
      clientId: "",
      clientSecret: "secret",
      userRoles: [],
      userPermissions: [],
    });
    expect(result.success).toBe(false);
  });
});
