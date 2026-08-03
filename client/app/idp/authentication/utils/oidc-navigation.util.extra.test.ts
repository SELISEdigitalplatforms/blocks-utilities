import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { redirectToLogin, buildNavigationUrl } from "./oidc-navigation.util";

const originalLocation = window.location;

const setLocation = (parts: { search?: string; hash?: string; href?: string }) => {
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: {
      search: parts.search ?? "",
      hash: parts.hash ?? "",
      href: parts.href ?? "http://localhost:3000/oidc/login",
      assign: () => {},
    },
  });
};

describe("oidc-navigation.util extra branches", () => {
  beforeEach(() => setLocation({}));
  afterEach(() => {
    Object.defineProperty(window, "location", {
      configurable: true,
      value: originalLocation,
    });
  });

  describe("redirectToLogin", () => {
    it("merges a non-hex hash fragment as query params", () => {
      setLocation({ hash: "#state=abc&nonce=def" });
      redirectToLogin();
      expect(window.location.href).toContain("state=abc");
      expect(window.location.href).toContain("nonce=def");
    });

    it("pulls extra params after a hex color in the hash", () => {
      setLocation({ hash: "#124091&clientId=c1&userName=u1" });
      redirectToLogin();
      expect(window.location.href).toContain("brandColor=%2523124091");
      expect(window.location.href).toContain("clientId=c1");
      expect(window.location.href).toContain("userName=u1");
    });

    it("encodes a hash-prefixed brand color already present in the query", () => {
      setLocation({ search: "?brandColor=%23ABCDEF" });
      redirectToLogin();
      // %23ABCDEF decodes to #ABCDEF, is re-encoded to %23ABCDEF, then URLSearchParams
      // encodes the % once more in toString(), yielding %2523ABCDEF.
      expect(window.location.href).toContain("brandColor=%2523ABCDEF");
    });
  });

  describe("buildNavigationUrl", () => {
    it("merges a non-hex hash fragment as query params", () => {
      setLocation({ hash: "#scope=openid&state=s1" });
      const url = buildNavigationUrl("/oidc/consent");
      expect(url).toContain("scope=openid");
      expect(url).toContain("state=s1");
    });

    it("recovers the brand color from the full url when not in query or hash", () => {
      setLocation({
        search: "",
        hash: "",
        href: "http://localhost:3000/oidc/login?brandColor=%23112233",
      });
      const url = buildNavigationUrl("/oidc/recover");
      expect(url).toContain("brandColor=%2523112233");
    });

    it("includes hash params that follow a hex color", () => {
      setLocation({ hash: "#654321&clientId=abc" });
      const url = buildNavigationUrl("/oidc/consent");
      expect(url).toContain("brandColor=%2523654321");
      expect(url).toContain("clientId=abc");
    });
  });
});
