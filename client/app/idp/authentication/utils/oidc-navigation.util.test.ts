import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { redirectToLogin, buildNavigationUrl } from "./oidc-navigation.util";

describe("oidc-navigation.util", () => {
  let hrefSetter: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    hrefSetter = vi.fn();

    Object.defineProperty(window, "location", {
      value: {
        search: "",
        hash: "",
        href: "http://localhost:3000/oidc/consent",
      },
      writable: true,
      configurable: true,
    });

    Object.defineProperty(window.location, "href", {
      set: hrefSetter as (v: string) => void,
      get: () => "http://localhost:3000/oidc/consent",
      configurable: true,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ─── redirectToLogin ──────────────────────────────────────────────────────
  describe("redirectToLogin", () => {
    it("should redirect to /oidc/login with current query params", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?x-blocks-key=test-key&clientId=client-123",
          hash: "",
          href: "http://localhost:3000/oidc/consent?x-blocks-key=test-key&clientId=client-123",
        },
        writable: true,
        configurable: true,
      });

      Object.defineProperty(window.location, "href", {
        set: hrefSetter as (v: string) => void,
        get: () => "http://localhost:3000/oidc/consent?x-blocks-key=test-key&clientId=client-123",
        configurable: true,
      });

      redirectToLogin();

      expect(hrefSetter).toHaveBeenCalledWith(expect.stringContaining("/oidc/login?"));
      const redirectUrl = hrefSetter.mock.calls[0][0] as string;
      expect(redirectUrl).toContain("x-blocks-key=test-key");
      expect(redirectUrl).toContain("clientId=client-123");
    });

    it("should extract brandColor from hash fragment", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?x-blocks-key=test-key",
          hash: "#FF0000&logoUrl=https://cdn.test.com/logo.png",
          href: "http://localhost:3000/oidc/consent?x-blocks-key=test-key#FF0000&logoUrl=https://cdn.test.com/logo.png",
        },
        writable: true,
        configurable: true,
      });

      Object.defineProperty(window.location, "href", {
        set: hrefSetter as (v: string) => void,
        get: () =>
          "http://localhost:3000/oidc/consent?x-blocks-key=test-key#FF0000&logoUrl=https://cdn.test.com/logo.png",
        configurable: true,
      });

      redirectToLogin();

      expect(hrefSetter).toHaveBeenCalled();
      const redirectUrl = hrefSetter.mock.calls[0][0] as string;
      expect(redirectUrl).toContain("brandColor=");
      expect(redirectUrl).toContain("logoUrl=");
    });
  });

  // ─── buildNavigationUrl ───────────────────────────────────────────────────
  describe("buildNavigationUrl", () => {
    it("should build URL for target path with current query params", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?x-blocks-key=test-key&clientId=client-123",
          hash: "",
          href: "http://localhost:3000/oidc/login?x-blocks-key=test-key&clientId=client-123",
        },
        writable: true,
        configurable: true,
      });

      const url = buildNavigationUrl("/oidc/consent");

      expect(url).toContain("/oidc/consent?");
      expect(url).toContain("x-blocks-key=test-key");
      expect(url).toContain("clientId=client-123");
    });

    it("should extract brandColor from hash and include in URL", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?x-blocks-key=test-key",
          hash: "#124091&clientId=client-from-hash",
          href: "http://localhost:3000/oidc/login?x-blocks-key=test-key#124091&clientId=client-from-hash",
        },
        writable: true,
        configurable: true,
      });

      const url = buildNavigationUrl("/oidc/recover");

      expect(url).toContain("/oidc/recover?");
      expect(url).toContain("brandColor=");
      expect(url).toContain("clientId=client-from-hash");
    });

    it("should handle URL with no params", () => {
      const url = buildNavigationUrl("/oidc/login");
      expect(url).toContain("/oidc/login?");
    });
  });
});
