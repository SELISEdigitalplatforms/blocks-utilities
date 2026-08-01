import { describe, expect, it } from "vitest";
import {
  createProviderField,
  createNameField,
  createClientIdField,
  createClientSecretField,
  createRedirectUrlField,
  createAudienceField,
  createCommonOAuthFields,
} from "./sso-provider-config-field-factory.util";

describe("sso provider config field factory", () => {
  it("maps a password type through unchanged", () => {
    const field = createProviderField("1", "Secret", "clientSecret", "password");
    expect(field).toMatchObject({
      id: "1",
      label: "Secret",
      name: "clientSecret",
      type: "password",
    });
  });

  it("defaults unknown types to input", () => {
    const field = createProviderField(
      "1",
      "Name",
      "provider",
      "textarea" as never,
    );
    expect(field.type).toBe("input");
  });

  it("applies overrides", () => {
    const field = createProviderField("1", "Name", "provider", "input", {
      isDisabled: true,
    });
    expect(field).toMatchObject({ isDisabled: true });
  });

  it("name field is disabled by default", () => {
    expect(createNameField()).toMatchObject({
      name: "provider",
      isDisabled: true,
    });
  });

  it("builds the individual oauth fields", () => {
    expect(createClientIdField().name).toBe("clientId");
    expect(createClientSecretField().type).toBe("password");
    expect(createRedirectUrlField().name).toBe("redirectUrl");
    expect(createAudienceField().name).toBe("audience");
  });

  it("assembles the common oauth field set in order", () => {
    const fields = createCommonOAuthFields();
    expect(fields.map((f) => f.name)).toEqual([
      "provider",
      "clientId",
      "clientSecret",
      "redirectUrl",
      "audience",
    ]);
  });
});
