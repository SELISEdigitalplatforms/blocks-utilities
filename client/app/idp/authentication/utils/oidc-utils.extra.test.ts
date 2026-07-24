import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { extractOIDCParams } from "./oidc-utils";

const originalLocation = window.location;

const setLocation = (parts: { search?: string; hash?: string; href?: string }) => {
  Object.defineProperty(window, "location", {
    configurable: true,
    value: {
      search: parts.search ?? "",
      hash: parts.hash ?? "",
      href: parts.href ?? "http://localhost:3000/oidc/login",
    },
  });
};

describe("oidc-utils extra branches", () => {
  beforeEach(() => setLocation({}));
  afterEach(() => {
    Object.defineProperty(window, "location", {
      configurable: true,
      value: originalLocation,
    });
  });

  it("reads every param from a hash of the form #<color>&key=value", () => {
    const hash =
      "#00ff00&x-blocks-key=key1&clientId=cli1&userName=user1&state=st1&nonce=no1&scope=openid&redirect_uri=https://cb.test&logoUrl=https://cdn.test/l.png";
    setLocation({ hash, href: `http://localhost:3000/oidc/login${hash}` });

    const params = extractOIDCParams();
    expect(params.themeColor).toBe("#00ff00");
    expect(params.projectKey).toBe("key1");
    expect(params.clientId).toBe("cli1");
    expect(params.userName).toBe("user1");
    expect(params.state).toBe("st1");
    expect(params.nonce).toBe("no1");
    expect(params.scope).toBe("openid");
    expect(params.redirectUri).toBe("https://cb.test");
    expect(params.logoUrl).toBe("https://cdn.test/l.png");
  });

  it("handles a bare hex-color hash with no trailing params", () => {
    setLocation({ hash: "#abcdef", href: "http://localhost:3000/oidc/login#abcdef" });
    const params = extractOIDCParams();
    expect(params.themeColor).toBe("#abcdef");
  });

  it("passes through a already-hashed #RRGGBB brand color from the query", () => {
    setLocation({
      search: "?brandColor=%23AABBCC",
      href: "http://localhost:3000/oidc/login?brandColor=%23AABBCC",
    });
    const params = extractOIDCParams();
    expect(params.themeColor).toBe("#AABBCC");
  });

  it("strips trailing query junk from the brand color", () => {
    setLocation({
      search: "?brandColor=FF0000%26foo%3Dbar",
      href: "http://localhost:3000/oidc/login?brandColor=FF0000%26foo%3Dbar",
    });
    const params = extractOIDCParams();
    expect(params.themeColor).toBe("#FF0000");
  });

  it("falls back to the default color for an invalid brand color", () => {
    setLocation({
      search: "?brandColor=notacolor",
      href: "http://localhost:3000/oidc/login?brandColor=notacolor",
    });
    const params = extractOIDCParams();
    expect(params.themeColor).toBe("#124091");
  });

  it("fully decodes a multiply-encoded logoUrl from the query", () => {
    setLocation({
      search: "?logoUrl=https%253A%252F%252Fcdn.test%252Flogo.png",
      href: "http://localhost:3000/oidc/login?logoUrl=https%253A%252F%252Fcdn.test%252Flogo.png",
    });
    const params = extractOIDCParams();
    expect(params.logoUrl).toBe("https://cdn.test/logo.png");
  });

  it("recovers a logoUrl found only in the full url", () => {
    setLocation({
      search: "",
      href: "http://localhost:3000/oidc/login?x-blocks-key=k#logoUrl=https://cdn.test/only.png",
    });
    const params = extractOIDCParams();
    expect(params.logoUrl).toBe("https://cdn.test/only.png");
  });
});
