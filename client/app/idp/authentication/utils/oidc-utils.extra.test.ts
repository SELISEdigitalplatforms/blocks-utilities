import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  extractOIDCParams,
  buildOIDCNavigationUrl,
  getCurrentOIDCParams,
} from "./oidc-utils";

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

  it("recovers a brand color found only in the full url query", () => {
    setLocation({
      search: "",
      href: "http://localhost:3000/oidc/login?brandColor=00ff00",
    });
    const params = extractOIDCParams();
    expect(params.themeColor).toBe("#00ff00");
  });

  it("leaves a malformed percent-encoded logoUrl untouched", () => {
    setLocation({
      search: "?logoUrl=%zz",
      href: "http://localhost:3000/oidc/login?logoUrl=%zz",
    });
    const params = extractOIDCParams();
    expect(params.logoUrl).toBe("%zz");
  });

  it("builds a navigation url preserving all present params", () => {
    setLocation({
      search:
        "?x-blocks-key=k1&userName=u1&clientId=c1&logoUrl=https://cdn.test/l.png&brandColor=00ff00&state=s1&nonce=n1&scope=openid&redirect_uri=https://cb.test",
      href: "http://localhost:3000/oidc/login?x-blocks-key=k1&userName=u1&clientId=c1&logoUrl=https://cdn.test/l.png&brandColor=00ff00&state=s1&nonce=n1&scope=openid&redirect_uri=https://cb.test",
    });
    const url = buildOIDCNavigationUrl("/oidc/signup");
    expect(url.startsWith("/oidc/signup?")).toBe(true);
    const qs = new URLSearchParams(url.split("?")[1]);
    expect(qs.get("x-blocks-key")).toBe("k1");
    expect(qs.get("userName")).toBe("u1");
    expect(qs.get("clientId")).toBe("c1");
    expect(qs.get("logoUrl")).toBe("https://cdn.test/l.png");
    expect(qs.get("brandColor")).toBe("#00ff00");
    expect(qs.get("state")).toBe("s1");
    expect(qs.get("nonce")).toBe("n1");
    expect(qs.get("scope")).toBe("openid");
    expect(qs.get("redirect_uri")).toBe("https://cb.test");
  });

  it("returns just the path when no params are present", () => {
    setLocation({ search: "", href: "http://localhost:3000/oidc/login" });
    // Default theme color is always set, so a brandColor is always emitted.
    const url = buildOIDCNavigationUrl("/oidc/signup");
    expect(url).toContain("/oidc/signup");
  });

  it("exposes current params as URLSearchParams", () => {
    setLocation({
      search:
        "?x-blocks-key=k1&userName=u1&clientId=c1&logoUrl=https://cdn.test/l.png&brandColor=00ff00&state=s1&nonce=n1&scope=openid&redirect_uri=https://cb.test",
      href: "http://localhost:3000/oidc/login?x-blocks-key=k1&userName=u1&clientId=c1&logoUrl=https://cdn.test/l.png&brandColor=00ff00&state=s1&nonce=n1&scope=openid&redirect_uri=https://cb.test",
    });
    const qs = getCurrentOIDCParams();
    expect(qs.get("x-blocks-key")).toBe("k1");
    expect(qs.get("state")).toBe("s1");
    expect(qs.get("nonce")).toBe("n1");
    expect(qs.get("scope")).toBe("openid");
    expect(qs.get("redirect_uri")).toBe("https://cb.test");
    expect(qs.get("brandColor")).toBe("#00ff00");
  });

  it("should read a colour-only fragment as parameters rather than giving up", () => {
    // A fragment that opens with a bare six-digit colour and no "&" separator
    // still has to be run through the parameter reader.
    setLocation({ search: "", hash: "#123456" });

    const params = extractOIDCParams();

    expect(params.themeColor).toBe("#123456");
  });

  it("should take the colour from the fragment when the query carries none", () => {
    setLocation({ search: "?clientId=client-1", hash: "#abcdef" });

    const params = extractOIDCParams();

    expect(params.themeColor).toBe("#abcdef");
    expect(params.clientId).toBe("client-1");
  });

  it("should let a query colour win over the fragment colour", () => {
    setLocation({
      search: "?brandColor=%23ff0000",
      hash: "#abcdef",
      href: "http://localhost:3000/oidc/login?brandColor=%23ff0000#abcdef",
    });

    const params = extractOIDCParams();

    expect(params.themeColor).toBe("#ff0000");
  });

  it("should carry a fragment colour into a navigation URL", () => {
    setLocation({ search: "?clientId=client-1", hash: "#abcdef" });

    const url = buildOIDCNavigationUrl("/signin");

    expect(url).toContain("brandColor=%23abcdef");
    expect(url).toContain("clientId=client-1");
  });

  it("should carry a fragment colour into the current params", () => {
    setLocation({ search: "?userName=ada", hash: "#abcdef" });

    const params = getCurrentOIDCParams();

    expect(params.get("brandColor")).toBe("#abcdef");
    expect(params.get("userName")).toBe("ada");
  });

});
