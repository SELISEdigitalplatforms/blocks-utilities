import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockAuthenticationServiceFactory,
  mockGetAuthConfigPayload,
  mockGetAuthConfigResponse,
  mockSaveAuthConfigPayload,
} from "../../test-utils/__mocks__";
import { authenticationService } from "@blocks-idp/authentication/services/authentication.service";
import { useGetAuthConfig, useSaveAuthConfig } from "./use-auth-config";

vi.mock("@blocks-idp/authentication/services/authentication.service", () =>
  mockAuthenticationServiceFactory(),
);

describe("use-auth-config hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetAuthConfig", () => {
    it("should fetch auth config successfully", async () => {
      vi.mocked(authenticationService.configuration.getConfig).mockResolvedValue(
        mockGetAuthConfigResponse,
      );

      const { result } = renderHook(
        () => useGetAuthConfig({ projectKey: mockGetAuthConfigPayload.projectKey }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockGetAuthConfigResponse);
      expect(authenticationService.configuration.getConfig).toHaveBeenCalledWith({
        projectKey: mockGetAuthConfigPayload.projectKey,
      });
    });
  });

  describe("useSaveAuthConfig", () => {
    it("should save auth config successfully", async () => {
      vi.mocked(authenticationService.configuration.saveAuthConfig).mockResolvedValue(
        undefined as never,
      );

      const { result } = renderHook(
        () => useSaveAuthConfig({ projectKey: mockGetAuthConfigPayload.projectKey }),
        { wrapper: createWrapper() },
      );

      result.current.mutate(mockSaveAuthConfigPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(authenticationService.configuration.saveAuthConfig).toHaveBeenCalledWith(
        mockSaveAuthConfigPayload,
      );
    });
  });
});
