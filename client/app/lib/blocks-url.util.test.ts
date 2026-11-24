import { afterEach, describe, expect, it } from "vitest";
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
    expect(deriveUtilityBaseUrl()).toBe("https://utilities.blocksdevelopers.com");
  });

  it("preserves the dev prefix on a developers host", () => {
    setOrigin("https://dev-utilities.blocksdevelopers.com");
    expect(deriveIdpBaseUrl()).toBe("https://dev-iam.blocksdevelopers.com");
  });

  it("preserves the stg prefix", () => {
    setOrigin("https://stg-utilities.blocksdevelopers.com");
    expect(deriveDeploymentBaseUrl()).toBe("https://stg-release.blocksdevelopers.com");
  });

  it("maps a production seliseblocks host to the prod domain", () => {
    setOrigin("https://utilities.seliseblocks.com");
    expect(deriveOsBaseUrl()).toBe("https://os.seliseblocks.com");
  });

  it("keeps the prefix on a prefixed production host", () => {
    setOrigin("https://dev-utilities.seliseblocks.com");
    expect(deriveAgentBaseUrl()).toBe("https://dev-agent.seliseblocks.com");
  });

  it("falls back to stg developers domain when origin does not match", () => {
    setOrigin("not-a-url");
    expect(deriveAgentBaseUrl()).toBe("https://stg-agent.blocksdevelopers.com");
  });

  it("covers the remaining subdomain helpers", () => {
    setOrigin("http://localhost");
    expect(deriveDeploymentBaseUrl()).toBe("https://release.blocksdevelopers.com");
  });
});

// Data, localization, logic and monitor are served by the os host: the configured key while set, else
// BLOCKS_OS_BASE_URL, else the os host derived from the page; never a retired host.
describe("blocks-url.util merged areas", () => {
  type EnvWindow = {
    __BLOCKS_ENV__?: Record<string, string>;
    process?: { env: Record<string, string> };
  };
  const originalLocation = window.location;
  const originalEnv = (window as unknown as EnvWindow).__BLOCKS_ENV__;
  const originalProcess = (window as unknown as EnvWindow).process;
  // genesis-os also reads window.process.env, which it points at __BLOCKS_ENV__ when it loads; set both, as in the app.
  const setEnv = (env: Record<string, string>) => {
    (window as unknown as EnvWindow).__BLOCKS_ENV__ = env;
    (window as unknown as EnvWindow).process = { env };
  };
  const merged: Array<[string, () => string, string]> = [
    ["data", deriveUdsBaseUrl, "BLOCKS_DATA_BASE_URL"],
    ["localization", deriveLocalizationBaseUrl, "BLOCKS_LOCALIZATION_BASE_URL"],
    ["logic", deriveLogicBaseUrl, "BLOCKS_LOGIC_BASE_URL"],
    ["monitor", deriveObservabilityBaseUrl, "BLOCKS_MONITOR_BASE_URL"],
  ];

  afterEach(() => {
    (window as unknown as EnvWindow).__BLOCKS_ENV__ = originalEnv;
    (window as unknown as EnvWindow).process = originalProcess;
    Object.defineProperty(window, "location", {
      value: originalLocation,
      writable: true,
      configurable: true,
    });
  });

  it.each(merged)("%s uses its configured key while set", (_, derive, key) => {
    setEnv({
      [key]: "https://configured.example.test",
      BLOCKS_OS_BASE_URL: "https://os.example.test",
    });
    expect(derive()).toBe("https://configured.example.test");
  });

  it.each(merged)("%s falls back to BLOCKS_OS_BASE_URL", (_, derive) => {
    setEnv({ BLOCKS_OS_BASE_URL: "https://os.example.test" });
    expect(derive()).toBe("https://os.example.test");
  });

  it.each(merged)("%s derives the os host, not its own retired host", (_, derive) => {
    setEnv({});
    setOrigin("https://dev-utilities.blocksdevelopers.com");
    expect(derive()).toBe("https://dev-os.blocksdevelopers.com");
  });
});
