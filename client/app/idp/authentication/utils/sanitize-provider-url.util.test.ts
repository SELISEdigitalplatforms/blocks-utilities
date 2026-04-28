import { describe, expect, it } from "vitest";
import { sanitizeProviderUrl } from "./sanitize-provider-url.util";

describe("sanitize-provider-url.util", () => {
  describe("sanitizeProviderUrl", () => {
    it("should strip C# format-string placeholders", () => {
      const url =
        "https://accounts.google.com/o/oauth2/auth?client_id={0}&redirect_uri={1}&client_id=real-client-id&redirect_uri=https://app.test.com/callback";

      const result = sanitizeProviderUrl(url);

      expect(result).toContain("client_id=real-client-id");
      expect(result).toContain("redirect_uri=https");
      expect(result).not.toContain("{0}");
      expect(result).not.toContain("{1}");
    });

    it("should preserve URL with no placeholders", () => {
      const url = "https://accounts.google.com/o/oauth2/auth?client_id=real-id&scope=openid";

      const result = sanitizeProviderUrl(url);
      expect(result).toBe(url);
    });

    it("should return original URL if parsing fails", () => {
      const invalidUrl = "not-a-valid-url";
      const result = sanitizeProviderUrl(invalidUrl);
      expect(result).toBe(invalidUrl);
    });

    it("should handle URL with only placeholder values", () => {
      const url = "https://example.com?param={0}";
      const result = sanitizeProviderUrl(url);
      // Placeholder-only params are stripped entirely
      expect(result).toBe("https://example.com/");
    });

    it("should preserve the last non-placeholder value for duplicate keys", () => {
      const url = "https://example.com?key={0}&key=value1&key=value2";

      const result = sanitizeProviderUrl(url);
      // Should keep the last non-placeholder value
      expect(result).toContain("key=value");
      expect(result).not.toContain("{0}");
    });
  });
});
