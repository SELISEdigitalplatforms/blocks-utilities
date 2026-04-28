import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockSsoServiceFactory,
  mockGetSsoCredentialsPayload,
  mockSsoCredentialsResponse,
  mockGetSsoCredentialByIdPayload,
  mockSsoCredential,
  mockSaveSsoPayload,
  mockDeleteSsoPayload,
  mockUpdateSsoStatusPayload,
} from "../../test-utils/__mocks__";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__";
import { ssoService } from "@blocks-idp/authentication/services/social.service";
import {
  useGetSsoCredentials,
  useGetSsoCredentialById,
  useSaveSsoCredential,
  useDeleteSsoCredential,
  useUpdateSsoCredentialStatus,
  useSaveOIDCCredential,
  useSaveGetOIDCCredential,
} from "./use-sso";

vi.mock("@blocks-idp/authentication/services/social.service", () => mockSsoServiceFactory());

describe("use-sso hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetSsoCredentials", () => {
    it("should fetch SSO credentials successfully", async () => {
      vi.mocked(ssoService.getSsoCredentials).mockResolvedValue(mockSsoCredentialsResponse);

      const { result } = renderHook(() => useGetSsoCredentials(mockGetSsoCredentialsPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockSsoCredentialsResponse);
      expect(ssoService.getSsoCredentials).toHaveBeenCalledWith(mockGetSsoCredentialsPayload);
    });
  });

  describe("useGetSsoCredentialById", () => {
    it("should fetch SSO credential by ID successfully", async () => {
      vi.mocked(ssoService.getSsoCredentialId).mockResolvedValue(mockSsoCredential);

      const { result } = renderHook(
        () => useGetSsoCredentialById(mockGetSsoCredentialByIdPayload),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockSsoCredential);
      expect(ssoService.getSsoCredentialId).toHaveBeenCalledWith(mockGetSsoCredentialByIdPayload);
    });

    it("should not fetch when itemId is empty", () => {
      const { result } = renderHook(
        () =>
          useGetSsoCredentialById({
            itemId: "",
            projectKey: TEST_PROJECT_KEY,
          }),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe("idle");
    });
  });

  describe("useSaveSsoCredential", () => {
    it("should save SSO credential successfully", async () => {
      vi.mocked(ssoService.saveSsoCredential).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSaveSsoCredential(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveSsoPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(ssoService.saveSsoCredential).toHaveBeenCalledWith(mockSaveSsoPayload);
    });
  });

  describe("useDeleteSsoCredential", () => {
    it("should delete SSO credential successfully", async () => {
      vi.mocked(ssoService.deleteSsoCredential).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useDeleteSsoCredential(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockDeleteSsoPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(ssoService.deleteSsoCredential).toHaveBeenCalledWith(mockDeleteSsoPayload);
    });
  });

  describe("useUpdateSsoCredentialStatus", () => {
    it("should update SSO credential status successfully", async () => {
      vi.mocked(ssoService.updateSsoCredentialStatus).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useUpdateSsoCredentialStatus(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockUpdateSsoStatusPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(ssoService.updateSsoCredentialStatus).toHaveBeenCalledWith(mockUpdateSsoStatusPayload);
    });
  });

  describe("useSaveOIDCCredential", () => {
    it("should save OIDC credential successfully", async () => {
      const mockPayload = { projectKey: TEST_PROJECT_KEY, name: "test" };
      vi.mocked(ssoService.saveBlocksSsoCredential).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSaveOIDCCredential(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(ssoService.saveBlocksSsoCredential).toHaveBeenCalledWith(mockPayload);
    });
  });

  describe("useSaveGetOIDCCredential", () => {
    it("should fetch OIDC credential successfully", async () => {
      const mockResponse = { data: {} };
      vi.mocked(ssoService.getBlocksSsoCredential).mockResolvedValue(mockResponse as never);

      const { result } = renderHook(() => useSaveGetOIDCCredential(TEST_PROJECT_KEY), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(ssoService.getBlocksSsoCredential).toHaveBeenCalledWith(TEST_PROJECT_KEY);
    });

    it("should not fetch when projectKey is empty", () => {
      const { result } = renderHook(() => useSaveGetOIDCCredential(""), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
    });
  });
});
