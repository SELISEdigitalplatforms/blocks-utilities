import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  deriveUtilityBaseUrl,
  deriveIdpBaseUrl,
  deriveUdsBaseUrl,
  deriveAgentBaseUrl,
  deriveOsBaseUrl,
  deriveLocalizationBaseUrl,
  deriveLogicBaseUrl,
  deriveObservabilityBaseUrl,
  deriveDeploymentBaseUrl,
} from "./blocks-url.util";

const setOrigin = (origin: string) => {
  Object.defineProperty(window, "location", {
    value: { origin } as Location,
    writable: true,
    configurable: true,
  });
};

describe("blocks-url.util deriveBaseUrl", () => {
  const originalLocation = window.location;

  afterEach(() => {
    Object.defineProperty(window, "location", {
      value: originalLocation,
      writable: true,
      configurable: true,
    });
  });

  it("uses the developers domain without prefix on localhost", () => {
    setOrigin("http://localhost:3000");
    expect(deriveUtilityBaseUrl()).toBe(
      "https://utilities.blocksdevelopers.com",
    );
  });

  it("preserves the dev prefix on a developers host", () => {
    setOrigin("https://dev-utilities.blocksdevelopers.com");
    expect(deriveIdpBaseUrl()).toBe("https://dev-iam.blocksdevelopers.com");
  });

  it("preserves the stg prefix", () => {
    setOrigin("https://stg-utilities.blocksdevelopers.com");
    expect(deriveUdsBaseUrl()).toBe("https://stg-data.blocksdevelopers.com");
  });

  it("maps a production seliseblocks host to the prod domain", () => {
    setOrigin("https://utilities.seliseblocks.com");
    expect(deriveOsBaseUrl()).toBe("https://os.seliseblocks.com");
  });

  it("keeps the prefix on a prefixed production host", () => {
    setOrigin("https://dev-utilities.seliseblocks.com");
    expect(deriveLogicBaseUrl()).toBe("https://dev-logic.seliseblocks.com");
  });

  it("falls back to stg developers domain when origin does not match", () => {
    setOrigin("not-a-url");
    expect(deriveAgentBaseUrl()).toBe("https://stg-agent.blocksdevelopers.com");
  });

  it("covers the remaining subdomain helpers", () => {
    setOrigin("http://localhost");
    expect(deriveLocalizationBaseUrl()).toBe(
      "https://localization.blocksdevelopers.com",
    );
    expect(deriveObservabilityBaseUrl()).toBe(
      "https://monitor.blocksdevelopers.com",
    );
    expect(deriveDeploymentBaseUrl()).toBe(
      "https://release.blocksdevelopers.com",
    );
  });
});
