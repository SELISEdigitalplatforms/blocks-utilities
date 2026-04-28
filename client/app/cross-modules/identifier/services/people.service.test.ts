import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import {
  mockGetPeopleResponse,
  mockSuccessResponse,
  mockInvitePeopleResponse,
  mockPeopleAcceptInvitationResponse,
  mockConfirmInvitationResponse,
} from "../test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { PEOPLE_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";
import { PeopleService } from "./people.service";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("PeopleService", () => {
  let service: PeopleService;

  beforeEach(() => {
    service = new PeopleService();
  });

  // ─── peopleAcceptInvitation ─────────────────────────────────────────────────

  describe("peopleAcceptInvitation", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockPeopleAcceptInvitationResponse);

      const payload = { code: "invite-code-123" };
      const result = await service.peopleAcceptInvitation(payload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.CONFIRM_INVITATION, payload);
      expect(result).toEqual(mockPeopleAcceptInvitationResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Failed to accept invitation"));

      await expect(service.peopleAcceptInvitation({ code: "bad" })).rejects.toThrow(
        "Failed to accept invitation",
      );
    });
  });

  // ─── getPeople ──────────────────────────────────────────────────────────────

  describe("getPeople", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockGetPeopleResponse);

      const payload = { page: 1, pageSize: 10, filter: "", projectGroupId: "group-1" };
      const result = await service.getPeople(payload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.GETS, payload);
      expect(result).toEqual(mockGetPeopleResponse);
    });

    it("should pass filter parameter correctly", async () => {
      vi.mocked(http.post).mockResolvedValue(mockGetPeopleResponse);

      const payload = { page: 1, pageSize: 20, filter: "john", projectGroupId: "group-1" };
      await service.getPeople(payload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.GETS, payload);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Failed to fetch people"));

      await expect(
        service.getPeople({ page: 1, pageSize: 10, filter: "", projectGroupId: "group-1" }),
      ).rejects.toThrow("Failed to fetch people");
    });
  });

  // ─── invitePeople ───────────────────────────────────────────────────────────

  describe("invitePeople", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockInvitePeopleResponse);

      const payload = {
        invitations: { admin: ["new@example.com"] },
        groupId: "group-1",
      };
      const result = await service.invitePeople(payload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.INVITE, payload);
      expect(result).toEqual(mockInvitePeopleResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Invite failed"));

      await expect(service.invitePeople({ invitations: {}, groupId: "group-1" })).rejects.toThrow(
        "Invite failed",
      );
    });
  });

  // ─── resendInvitation ───────────────────────────────────────────────────────

  describe("resendInvitation", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { email: "user@example.com", groupId: "group-1" };
      const result = await service.resendInvitation(payload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.RESEND_INVITATION, payload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Resend failed"));

      await expect(
        service.resendInvitation({ email: "user@example.com", groupId: "group-1" }),
      ).rejects.toThrow("Resend failed");
    });
  });

  // ─── removeAccess ───────────────────────────────────────────────────────────

  describe("removeAccess", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { userIds: ["user-1"], projectKey: "proj-key" };
      const result = await service.removeAccess(payload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.REMOVE_ACCESS, payload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Remove access failed"));

      await expect(
        service.removeAccess({ userIds: ["user-1"], projectKey: "proj-key" }),
      ).rejects.toThrow("Remove access failed");
    });
  });

  // ─── removeEnvironmentAccess ────────────────────────────────────────────────

  describe("removeEnvironmentAccess", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { email: "user@example.com", projectKeys: ["key-1"], groupId: "group-1" };
      const result = await service.removeEnvironmentAccess(payload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.REMOVE_ACCESS, payload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Remove env access failed"));

      await expect(
        service.removeEnvironmentAccess({
          email: "user@example.com",
          projectKeys: ["key-1"],
          groupId: "group-1",
        }),
      ).rejects.toThrow("Remove env access failed");
    });
  });

  // ─── confirmInvitation ──────────────────────────────────────────────────────

  describe("confirmInvitation", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockConfirmInvitationResponse);

      const payload = { code: "confirm-code-456" };
      const result = await service.confirmInvitation(payload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.CONFIRM_INVITATION, payload);
      expect(result).toEqual(mockConfirmInvitationResponse);
    });

    it("should return activationKey on success", async () => {
      vi.mocked(http.post).mockResolvedValue(mockConfirmInvitationResponse);

      const result = await service.confirmInvitation({ code: "code" });

      expect(result.activationKey).toBe("mock-activation-key-456");
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Confirm failed"));

      await expect(service.confirmInvitation({ code: "bad" })).rejects.toThrow("Confirm failed");
    });
  });

  // ─── transferOwnership ──────────────────────────────────────────────────────

  describe("transferOwnership", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = {
        tenantGroupId: "group-1",
        transferToUserEmail: "new-owner@example.com",
      };
      const result = await service.transferOwnership(payload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.TRANSFER_OWNERSHIP, payload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Transfer failed"));

      await expect(
        service.transferOwnership({
          tenantGroupId: "group-1",
          transferToUserEmail: "new-owner@example.com",
        }),
      ).rejects.toThrow("Transfer failed");
    });
  });
});
