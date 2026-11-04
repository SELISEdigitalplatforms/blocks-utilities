import { describe, expect, it } from "vitest";
import { makeOidcSchema, createOIDCFormDefaultValue } from "./utils";

const valid = {
  redirectUrlOidc: "https://app.example.com/callback",
  audienceUrlOidc: "https://api.example.com",
  scope: "openid",
  clientBrandColor: "#fff",
  clientDisplayName: "My Client",
};

describe("createOidcSchema", () => {
  it("has openid as the default scope", () => {
    expect(createOIDCFormDefaultValue.scope).toBe("openid");
  });

  it("accepts https urls", () => {
    expect(makeOidcSchema().safeParse(valid).success).toBe(true);
  });

  it("accepts http for localhost", () => {
    const result = makeOidcSchema().safeParse({
      ...valid,
      redirectUrlOidc: "http://localhost:3000/cb",
      audienceUrlOidc: "http://127.0.0.1:3000",
    });
    expect(result.success).toBe(true);
  });

  it("rejects http for non-localhost", () => {
    const result = makeOidcSchema().safeParse({
      ...valid,
      redirectUrlOidc: "http://app.example.com/cb",
    });
    expect(result.success).toBe(false);
  });

  it("rejects an invalid url", () => {
    const result = makeOidcSchema().safeParse({
      ...valid,
      audienceUrlOidc: "nope",
    });
    expect(result.success).toBe(false);
  });

  it("requires a client display name", () => {
    const result = makeOidcSchema().safeParse({ ...valid, clientDisplayName: "" });
    expect(result.success).toBe(false);
  });
});
