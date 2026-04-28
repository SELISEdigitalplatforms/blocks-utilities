import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockAuthClientsServiceFactory,
  mockClientCredentialsResponse,
  mockSaveClientPayload,
  mockDeleteClientPayload,
} from "../../test-utils/__mocks__";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__";
import { authClientService } from "@blocks-idp/authentication/services/auth-clients.service";
import {
  useGetAuthClientCredentials,
  useSaveAuthClient,
  useDeleteAuthClient,
} from "./use-auth-clients";

vi.mock("@blocks-idp/authentication/services/auth-clients.service", () =>
  mockAuthClientsServiceFactory(),
);

describe("use-auth-clients hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetAuthClientCredentials", () => {
    it("should fetch client credentials successfully", async () => {
      vi.mocked(authClientService.clients.getClientCredentials).mockResolvedValue(
        mockClientCredentialsResponse,
      );

      const { result } = renderHook(
        () => useGetAuthClientCredentials({ projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockClientCredentialsResponse);
      expect(authClientService.clients.getClientCredentials).toHaveBeenCalledWith({
        projectKey: TEST_PROJECT_KEY,
      });
    });
  });

  describe("useSaveAuthClient", () => {
    it("should save client credential successfully", async () => {
      vi.mocked(authClientService.clients.saveClientCredential).mockResolvedValue(
        undefined as never,
      );

      const { result } = renderHook(() => useSaveAuthClient({ projectKey: TEST_PROJECT_KEY }), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveClientPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(authClientService.clients.saveClientCredential).toHaveBeenCalledWith(
        mockSaveClientPayload,
      );
    });
  });

  describe("useDeleteAuthClient", () => {
    it("should delete client credential successfully", async () => {
      vi.mocked(authClientService.clients.deleteClientCredential).mockResolvedValue(
        undefined as never,
      );

      const { result } = renderHook(() => useDeleteAuthClient({ projectKey: TEST_PROJECT_KEY }), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockDeleteClientPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(authClientService.clients.deleteClientCredential).toHaveBeenCalledWith(
        mockDeleteClientPayload,
      );
    });
  });
});
