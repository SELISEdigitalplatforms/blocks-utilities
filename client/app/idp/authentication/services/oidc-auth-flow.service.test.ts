import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  AUTH_ENDPOINTS,
  AUTH_OIDC_ENDPOINTS,
  OIDC_FLOW_ENDPOINTS,
} from "../constants/endpoint.constant";
import { ACCOUNT_ENDPOINTS } from "@blocks-idp/iam/constants/endpoint.constant";
import {
  mockOidcFlowCredentialPayload,
  mockOidcFlowCredentialResponse,
  mockUserAcknowledgementPayload,
  mockUserAcknowledgementResponse,
  mockOidcFlowAccountRecoverPayload,
  mockOidcFlowAccountRecoverResponse,
  mockRefreshTokenStorage,
  mockRefreshedTokenResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: vi.fn(),
}));

const MOCK_API_BASE = "https://api.blocks.test";

describe("oidc-auth-flow.service", () => {
  let refreshAccessToken: typeof import("./oidc-auth-flow.service").refreshAccessToken;
  let getOidcCredential: typeof import("./oidc-auth-flow.service").getOidcCredential;
  let userAcknowledgement: typeof import("./oidc-auth-flow.service").userAcknowledgement;
  let accountRecover: typeof import("./oidc-auth-flow.service").accountRecover;

  beforeEach(async () => {
    vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", MOCK_API_BASE);
    vi.stubGlobal("fetch", vi.fn());

    const mod = await import("./oidc-auth-flow.service");
    refreshAccessToken = mod.refreshAccessToken;
    getOidcCredential = mod.getOidcCredential;
    userAcknowledgement = mod.userAcknowledgement;
    accountRecover = mod.accountRecover;
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // ─── refreshAccessToken ─────────────────────────────────────────────────────
  describe("refreshAccessToken", () => {
    it("should refresh token and update localStorage", async () => {
      localStorage.setItem("oidc-auth-storage", mockRefreshTokenStorage);

      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockRefreshedTokenResponse),
      } as Response);

      const result = await refreshAccessToken("test-project-key");

      expect(fetch).toHaveBeenCalledWith(
        `${MOCK_API_BASE}${AUTH_ENDPOINTS.TOKEN}`,
        expect.objectContaining({ method: "POST" }),
      );
      expect(result).toBe(mockRefreshedTokenResponse.access_token);

      const stored = JSON.parse(localStorage.getItem("oidc-auth-storage")!);
      expect(stored.access_token).toBe(mockRefreshedTokenResponse.access_token);
    });

    it("should return null when no oidc-auth-storage exists", async () => {
      const result = await refreshAccessToken("test-project-key");
      expect(result).toBeNull();
    });

    it("should return null when response has an error field", async () => {
      localStorage.setItem("oidc-auth-storage", mockRefreshTokenStorage);

      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        json: () => Promise.resolve({ error: "invalid_grant" }),
      } as Response);

      const result = await refreshAccessToken("test-key");
      expect(result).toBeNull();
    });

    it("should return null when fetch fails", async () => {
      localStorage.setItem("oidc-auth-storage", mockRefreshTokenStorage);

      vi.mocked(fetch).mockResolvedValue({
        ok: false,
        status: 400,
      } as Response);

      const result = await refreshAccessToken("test-key");
      expect(result).toBeNull();
    });
  });

  // ─── getOidcCredential ───────────────────────────────────────────────────────
  describe("getOidcCredential", () => {
    it("should fetch OIDC credential with auth headers", async () => {
      localStorage.setItem("oidc-auth-storage", JSON.stringify({ access_token: "test-token" }));

      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        status: 200,
        json: () => Promise.resolve(mockOidcFlowCredentialResponse),
      } as Response);

      const result = await getOidcCredential(mockOidcFlowCredentialPayload);

      expect(fetch).toHaveBeenCalledWith(
        expect.stringContaining(AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENT),
        expect.objectContaining({ method: "GET" }),
      );
      expect(result).toEqual(mockOidcFlowCredentialResponse);
    });

    it("should retry with refreshed token on 401", async () => {
      localStorage.setItem("oidc-auth-storage", mockRefreshTokenStorage);

      vi.mocked(fetch)
        .mockResolvedValueOnce({ ok: false, status: 401 } as Response)
        .mockResolvedValueOnce({
          ok: true,
          json: () => Promise.resolve(mockRefreshedTokenResponse),
        } as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          json: () => Promise.resolve(mockOidcFlowCredentialResponse),
        } as Response);

      const result = await getOidcCredential(mockOidcFlowCredentialPayload);
      expect(result).toEqual(mockOidcFlowCredentialResponse);
      expect(fetch).toHaveBeenCalledTimes(3);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: false,
        status: 500,
        statusText: "Internal Server Error",
      } as Response);

      await expect(getOidcCredential(mockOidcFlowCredentialPayload)).rejects.toThrow();
    });
  });

  // ─── userAcknowledgement ──────────────────────────────────────────────────────
  describe("userAcknowledgement", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockUserAcknowledgementResponse),
      } as Response);

      const result = await userAcknowledgement(mockUserAcknowledgementPayload);

      expect(fetch).toHaveBeenCalledWith(
        `${MOCK_API_BASE}${OIDC_FLOW_ENDPOINTS.USER_ACKNOWLEDGEMENT}`,
        expect.objectContaining({
          method: "POST",
          body: expect.any(String),
        }),
      );
      expect(result).toEqual(mockUserAcknowledgementResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: false,
        status: 400,
        statusText: "Bad Request",
      } as Response);

      await expect(userAcknowledgement(mockUserAcknowledgementPayload)).rejects.toThrow();
    });
  });

  // ─── accountRecover ───────────────────────────────────────────────────────────
  describe("accountRecover", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockOidcFlowAccountRecoverResponse),
      } as Response);

      const result = await accountRecover(mockOidcFlowAccountRecoverPayload);

      expect(fetch).toHaveBeenCalledWith(
        `${MOCK_API_BASE}${ACCOUNT_ENDPOINTS.RECOVER}`,
        expect.objectContaining({
          method: "POST",
          body: expect.any(String),
        }),
      );
      expect(result).toEqual(mockOidcFlowAccountRecoverResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: false,
        status: 500,
        statusText: "Internal Server Error",
      } as Response);

      await expect(accountRecover(mockOidcFlowAccountRecoverPayload)).rejects.toThrow();
    });
  });
});
