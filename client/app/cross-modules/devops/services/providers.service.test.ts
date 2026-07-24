import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  authenticateWithGithub,
  verifyOAuthState,
  authenticateWithGitlab,
  authenticateWithBitbucket,
  authenticateWithAzure,
  authenticateWithAws,
} from "./providers.service";

describe("providers.service", () => {
  let openSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    localStorage.clear();
    openSpy = vi.spyOn(window, "open").mockImplementation(() => null);
  });
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
  });

  describe("authenticateWithGithub", () => {
    it("opens the OAuth url and stores auth state", () => {
      authenticateWithGithub(undefined, "pk-1");
      expect(openSpy).toHaveBeenCalledTimes(1);
      const url = openSpy.mock.calls[0][0] as string;
      expect(url).toContain("https://github.com/login/oauth/authorize");
      expect(url).toContain("scope=repo");
      const state = localStorage.getItem("github_auth_state");
      expect(state).toBeTruthy();
      expect(url).toContain(`state=${state}`);
      expect(localStorage.getItem("github_auth_project_key")).toBe("pk-1");
      expect(localStorage.getItem("github_auth_destination")).toBe("/");
    });

    it("uses the stored destination when present and omits project key", () => {
      localStorage.setItem("destination", "/dashboard");
      authenticateWithGithub();
      expect(localStorage.getItem("github_auth_destination")).toBe("/dashboard");
      expect(localStorage.getItem("github_auth_project_key")).toBeNull();
    });
  });

  describe("verifyOAuthState", () => {
    it("matches the stored state", () => {
      localStorage.setItem("github_auth_state", "xyz");
      expect(verifyOAuthState("xyz")).toBe(true);
      expect(verifyOAuthState("other")).toBe(false);
      expect(verifyOAuthState(null)).toBe(false);
    });
  });

  describe("unimplemented providers", () => {
    it("log a not-implemented notice without throwing", () => {
      const logSpy = vi.spyOn(console, "log").mockImplementation(() => {});
      authenticateWithGitlab();
      authenticateWithBitbucket();
      authenticateWithAzure();
      authenticateWithAws();
      expect(logSpy).toHaveBeenCalledTimes(4);
    });
  });
});
