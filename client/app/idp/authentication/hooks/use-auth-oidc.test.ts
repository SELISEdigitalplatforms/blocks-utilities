import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockAuthOidcServiceFactory,
  mockOidcCredentialsResponse,
  mockOidcCredentialResponse,
  mockSaveOidcPayload,
  mockDeleteClientPayload,
  MOCK_OIDC_ITEM_ID,
} from "../../test-utils/__mocks__";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__";
import { authOidc } from "@blocks-idp/authentication/services/auth-clients-oidc.service";
import {
  useGetAuthOidcCredentials,
  useGetAuthOidcCredential,
  useSaveAuthOidc,
  useDeleteAuthOidc,
} from "./use-auth-oidc";

vi.mock("@blocks-idp/authentication/services/auth-clients-oidc.service", () =>
  mockAuthOidcServiceFactory(),
);

describe("use-auth-oidc hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetAuthOidcCredentials", () => {
    it("should fetch OIDC credentials list successfully", async () => {
      vi.mocked(authOidc.clients.getOidcCredentials).mockResolvedValue(mockOidcCredentialsResponse);

      const { result } = renderHook(
        () => useGetAuthOidcCredentials({ projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockOidcCredentialsResponse);
      expect(authOidc.clients.getOidcCredentials).toHaveBeenCalledWith({
        projectKey: TEST_PROJECT_KEY,
      });
    });
  });

  describe("useGetAuthOidcCredential", () => {
    it("should fetch a single OIDC credential successfully", async () => {
      vi.mocked(authOidc.clients.getOidcCredential).mockResolvedValue(mockOidcCredentialResponse);

      const options = { projectKey: TEST_PROJECT_KEY, clientId: MOCK_OIDC_ITEM_ID };
      const { result } = renderHook(() => useGetAuthOidcCredential(options), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockOidcCredentialResponse);
      expect(authOidc.clients.getOidcCredential).toHaveBeenCalledWith(options);
    });
  });

  describe("useSaveAuthOidc", () => {
    it("should save OIDC credential successfully", async () => {
      vi.mocked(authOidc.clients.saveOidcCredential).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSaveAuthOidc(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveOidcPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(authOidc.clients.saveOidcCredential).toHaveBeenCalledWith(mockSaveOidcPayload);
    });
  });

  describe("useDeleteAuthOidc", () => {
    it("should delete OIDC credential successfully", async () => {
      vi.mocked(authOidc.clients.deleteOidcCredential).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useDeleteAuthOidc({ projectKey: TEST_PROJECT_KEY }), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockDeleteClientPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(authOidc.clients.deleteOidcCredential).toHaveBeenCalledWith(mockDeleteClientPayload);
    });
  });
});
