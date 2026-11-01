import { describe, expect, it } from "vitest";
import { getMFA_Provider_Data } from "./mfa-config";

describe("mfa-config", () => {
  describe("MFA_Provider_Data", () => {
    it("should contain 2 providers", () => {
      expect(getMFA_Provider_Data()).toHaveLength(2);
    });

    it("should have email provider as the first entry", () => {
      const email = getMFA_Provider_Data()[0];
      expect(email.provider).toBe("email");
      expect(email.label).toBe("Email");
      expect(email.type).toBe(2);
      expect(email.status).toBe(false);
    });

    it("should have authenticator_app provider as the second entry", () => {
      const authApp = getMFA_Provider_Data()[1];
      expect(authApp.provider).toBe("authenticator_app");
      expect(authApp.label).toBe("Authenticator app");
      expect(authApp.type).toBe(1);
      expect(authApp.status).toBe(false);
    });

    it("should have Icon component for each provider", () => {
      getMFA_Provider_Data().forEach((provider) => {
        expect(provider.Icon).toBeDefined();
      });
    });

    it("should have description for each provider", () => {
      getMFA_Provider_Data().forEach((provider) => {
        expect(provider.description).toBeTruthy();
        expect(typeof provider.description).toBe("string");
      });
    });
  });
});
