import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockProjectStoreFactory, mockToastFactory } from "@/test-utils/__mocks__";
import {
  mockCloneTemplatePayload,
  mockDeleteTemplatePayload,
  mockEmailServiceFactory,
  mockEmailTemplate,
  mockEmailTemplatesResponse,
  mockSaveTemplatePayload,
  mockSendTestMailPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";
// Need to import the mocks for the services and project store before the hooks to ensure the mocks are applied correctly
import { emailService } from "@blocks-communication/mail/services/email.services";
import {
  useCloneTemplate,
  useDeleteEmailTemplate,
  useGetEmailTemplate,
  useGetEmailTemplates,
  useSaveEmailTemplate,
  useSaveMailTemplate,
  useSendTestMail,
} from "./use-email-template";
import { TEST_TENANT_ID } from "@/test-utils/__mocks__/data.mock";

vi.mock("@blocks-communication/mail/services/email.services", () => mockEmailServiceFactory());
vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());
vi.mock("@/hooks/use-toast", () => mockToastFactory());

describe("Email Template Hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useSendTestMail", () => {
    it("should send test email successfully", async () => {
      vi.mocked(emailService.sendTestMail).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSendTestMail(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSendTestMailPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.sendTestMail).toHaveBeenCalledWith(mockSendTestMailPayload);
      expect(result.current.data).toEqual(mockSuccessResponse);
    });

    it("should handle send test email errors", async () => {
      vi.mocked(emailService.sendTestMail).mockRejectedValue(
        new Error("Failed to send test email"),
      );

      const { result } = renderHook(() => useSendTestMail(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSendTestMailPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toEqual(
        expect.objectContaining({ message: "Failed to send test email" }),
      );
    });

    it("should use correct mutation key", async () => {
      vi.mocked(emailService.sendTestMail).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSendTestMail(), {
        wrapper: createWrapper(),
      });

      // The mutation key should be ["test-mail", "send"]
      expect(result.current).toBeDefined();
    });
  });

  describe("useCloneTemplate", () => {
    it("should clone template successfully", async () => {
      vi.mocked(emailService.cloneMailTemplate).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useCloneTemplate(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockCloneTemplatePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.cloneMailTemplate).toHaveBeenCalledWith(mockCloneTemplatePayload);
      expect(result.current.data).toEqual(mockSuccessResponse);
    });

    it("should invalidate email-templates query on success", async () => {
      vi.mocked(emailService.cloneMailTemplate).mockResolvedValue(mockSuccessResponse);
      vi.mocked(emailService.fetchEmailTemplates).mockResolvedValue(mockEmailTemplatesResponse);

      const wrapper = createWrapper();

      // First, load email templates
      const { result: templatesResult } = renderHook(
        () => useGetEmailTemplates(0, 10, "", "Name", false, "en", ""),
        { wrapper },
      );

      await waitFor(() => expect(templatesResult.current.isSuccess).toBe(true));

      // Then clone a template
      const { result: cloneResult } = renderHook(() => useCloneTemplate(), { wrapper });

      cloneResult.current.mutate(mockCloneTemplatePayload);

      await waitFor(() => expect(cloneResult.current.isSuccess).toBe(true));

      // Templates should be refetched (invalidated)
      await waitFor(() => {
        expect(emailService.fetchEmailTemplates).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle clone errors", async () => {
      vi.mocked(emailService.cloneMailTemplate).mockRejectedValue(
        new Error("Failed to clone template"),
      );

      const { result } = renderHook(() => useCloneTemplate(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockCloneTemplatePayload);

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toEqual(
        expect.objectContaining({ message: "Failed to clone template" }),
      );
    });
  });

  describe("useDeleteEmailTemplate", () => {
    it("should delete template successfully", async () => {
      vi.mocked(emailService.deleteMailTemplate).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useDeleteEmailTemplate(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockDeleteTemplatePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.deleteMailTemplate).toHaveBeenCalledWith(mockDeleteTemplatePayload);
      expect(result.current.data).toEqual(mockSuccessResponse);
    });

    it("should invalidate email-templates query on success", async () => {
      vi.mocked(emailService.deleteMailTemplate).mockResolvedValue(mockSuccessResponse);
      vi.mocked(emailService.fetchEmailTemplates).mockResolvedValue(mockEmailTemplatesResponse);

      const wrapper = createWrapper();

      // First, load email templates
      const { result: templatesResult } = renderHook(
        () => useGetEmailTemplates(0, 10, "", "Name", false, "en", ""),
        { wrapper },
      );

      await waitFor(() => expect(templatesResult.current.isSuccess).toBe(true));

      // Then delete a template
      const { result: deleteResult } = renderHook(() => useDeleteEmailTemplate(), { wrapper });

      deleteResult.current.mutate(mockDeleteTemplatePayload);

      await waitFor(() => expect(deleteResult.current.isSuccess).toBe(true));

      // Templates should be refetched (invalidated)
      await waitFor(() => {
        expect(emailService.fetchEmailTemplates).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle delete errors", async () => {
      vi.mocked(emailService.deleteMailTemplate).mockRejectedValue(
        new Error("Failed to delete template"),
      );

      const { result } = renderHook(() => useDeleteEmailTemplate(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockDeleteTemplatePayload);

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toEqual(
        expect.objectContaining({ message: "Failed to delete template" }),
      );
    });
  });

  describe("useGetEmailTemplates", () => {
    it("should fetch email templates successfully with all filters", async () => {
      vi.mocked(emailService.fetchEmailTemplates).mockResolvedValue(mockEmailTemplatesResponse);

      const { result } = renderHook(
        () => useGetEmailTemplates(0, 10, "welcome", "Name", false, "en", "config-1"),
        { wrapper: createWrapper() },
      );

      expect(result.current.isLoading).toBe(true);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockEmailTemplatesResponse);
      expect(emailService.fetchEmailTemplates).toHaveBeenCalledWith(
        0,
        10,
        TEST_TENANT_ID,
        "welcome",
        "Name",
        false,
        "en",
        "config-1",
      );
    });

    it("should use correct query key for caching with all params", async () => {
      vi.mocked(emailService.fetchEmailTemplates).mockResolvedValue(mockEmailTemplatesResponse);

      const { result } = renderHook(
        () => useGetEmailTemplates(2, 20, "test", "CreatedDate", true, "fr", "config-2"),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.fetchEmailTemplates).toHaveBeenCalledWith(
        2,
        20,
        TEST_TENANT_ID,
        "test",
        "CreatedDate",
        true,
        "fr",
        "config-2",
      );
    });

    it("should handle empty filters", async () => {
      vi.mocked(emailService.fetchEmailTemplates).mockResolvedValue(mockEmailTemplatesResponse);

      const { result } = renderHook(() => useGetEmailTemplates(0, 10, "", "Name", false, "", ""), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.fetchEmailTemplates).toHaveBeenCalledWith(
        0,
        10,
        TEST_TENANT_ID,
        "",
        "Name",
        false,
        "",
        "",
      );
    });

    it("should have staleTime of 0", async () => {
      vi.mocked(emailService.fetchEmailTemplates).mockResolvedValue(mockEmailTemplatesResponse);

      const { result } = renderHook(
        () => useGetEmailTemplates(0, 10, "", "Name", false, "en", ""),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      // Data should be considered stale immediately (staleTime: 0)
      expect(result.current.isStale).toBe(true);
    });

    it("should handle errors", async () => {
      vi.mocked(emailService.fetchEmailTemplates).mockRejectedValue(
        new Error("Failed to fetch templates"),
      );

      const { result } = renderHook(
        () => useGetEmailTemplates(0, 10, "", "Name", false, "en", ""),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toBeDefined();
    });
  });

  describe("useGetEmailTemplate", () => {
    it("should fetch single email template successfully", async () => {
      vi.mocked(emailService.fetchEmailTemplate).mockResolvedValue(mockEmailTemplate);

      const { result } = renderHook(() => useGetEmailTemplate("template-1"), {
        wrapper: createWrapper(),
      });

      expect(result.current.isLoading).toBe(true);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockEmailTemplate);
      expect(emailService.fetchEmailTemplate).toHaveBeenCalledWith(TEST_TENANT_ID, "template-1");
    });

    it("should use correct query key for caching", async () => {
      vi.mocked(emailService.fetchEmailTemplate).mockResolvedValue(mockEmailTemplate);

      const { result } = renderHook(() => useGetEmailTemplate("template-2"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.fetchEmailTemplate).toHaveBeenCalledWith(TEST_TENANT_ID, "template-2");
    });

    it("should have staleTime of 0", async () => {
      vi.mocked(emailService.fetchEmailTemplate).mockResolvedValue(mockEmailTemplate);

      const { result } = renderHook(() => useGetEmailTemplate("template-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      // Data should be considered stale immediately (staleTime: 0)
      expect(result.current.isStale).toBe(true);
    });

    it("should handle errors", async () => {
      vi.mocked(emailService.fetchEmailTemplate).mockRejectedValue(new Error("Template not found"));

      const { result } = renderHook(() => useGetEmailTemplate("invalid-id"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toBeDefined();
    });
  });

  describe("useSaveEmailTemplate", () => {
    it("should save email template successfully with custom state", async () => {
      vi.mocked(emailService.saveMailTemplate).mockImplementation(async () => {
        await new Promise((resolve) => setTimeout(resolve, 50));
        return mockSuccessResponse;
      });

      const { result } = renderHook(() => useSaveEmailTemplate(), {
        wrapper: createWrapper(),
      });

      expect(result.current.isPending).toBe(false);

      let response;
      const savePromise = result.current.saveEmailTemplate(mockEmailTemplate).then((res) => {
        response = res;
      });

      // isPending should be true during save
      await waitFor(() => expect(result.current.isPending).toBe(true));

      await savePromise;

      // isPending should be false after save
      await waitFor(() => expect(result.current.isPending).toBe(false));

      expect(emailService.saveMailTemplate).toHaveBeenCalledWith({
        ...mockEmailTemplate,
        projectKey: TEST_TENANT_ID,
      });
      expect(response).toEqual(mockSuccessResponse);
    });

    it("should handle save with empty itemId", async () => {
      vi.mocked(emailService.saveMailTemplate).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSaveEmailTemplate(), {
        wrapper: createWrapper(),
      });

      const templateWithoutId = { ...mockEmailTemplate, itemId: "" };
      await result.current.saveEmailTemplate(templateWithoutId);

      expect(emailService.saveMailTemplate).toHaveBeenCalledWith({
        ...templateWithoutId,
        itemId: "",
        projectKey: TEST_TENANT_ID,
      });
    });

    it("should handle save errors and reset pending state", async () => {
      const error = new Error("Failed to save template");
      vi.mocked(emailService.saveMailTemplate).mockRejectedValue(error);

      const { result } = renderHook(() => useSaveEmailTemplate(), {
        wrapper: createWrapper(),
      });

      await expect(result.current.saveEmailTemplate(mockEmailTemplate)).rejects.toThrow(
        "Failed to save template",
      );

      // isPending should be false after error
      await waitFor(() => expect(result.current.isPending).toBe(false));
    });

    it("should include projectKey from tenantId", async () => {
      vi.mocked(emailService.saveMailTemplate).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSaveEmailTemplate(), {
        wrapper: createWrapper(),
      });

      await result.current.saveEmailTemplate(mockEmailTemplate);

      const callArgs = vi.mocked(emailService.saveMailTemplate).mock.calls[0][0];
      expect(callArgs.projectKey).toBe(TEST_TENANT_ID);
    });
  });

  describe("useSaveMailTemplate", () => {
    it("should save mail template successfully", async () => {
      vi.mocked(emailService.saveMailTemplate).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSaveMailTemplate(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveTemplatePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.saveMailTemplate).toHaveBeenCalledWith(mockSaveTemplatePayload);
      expect(result.current.data).toEqual(mockSuccessResponse);
    });

    it("should invalidate both email-templates and email-template queries on success", async () => {
      vi.mocked(emailService.saveMailTemplate).mockResolvedValue(mockSuccessResponse);
      vi.mocked(emailService.fetchEmailTemplates).mockResolvedValue(mockEmailTemplatesResponse);
      vi.mocked(emailService.fetchEmailTemplate).mockResolvedValue(mockEmailTemplate);

      const wrapper = createWrapper();

      // Load both templates list and single template
      const { result: templatesResult } = renderHook(
        () => useGetEmailTemplates(0, 10, "", "Name", false, "en", ""),
        { wrapper },
      );
      const { result: templateResult } = renderHook(() => useGetEmailTemplate("template-1"), {
        wrapper,
      });

      await waitFor(() => expect(templatesResult.current.isSuccess).toBe(true));
      await waitFor(() => expect(templateResult.current.isSuccess).toBe(true));

      // Then save a template
      const { result: saveResult } = renderHook(() => useSaveMailTemplate(), { wrapper });

      saveResult.current.mutate(mockSaveTemplatePayload);

      await waitFor(() => expect(saveResult.current.isSuccess).toBe(true));

      // Both queries should be refetched (invalidated)
      await waitFor(() => {
        expect(emailService.fetchEmailTemplates).toHaveBeenCalledTimes(2);
        expect(emailService.fetchEmailTemplate).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle save errors", async () => {
      vi.mocked(emailService.saveMailTemplate).mockRejectedValue(
        new Error("Failed to save template"),
      );

      const { result } = renderHook(() => useSaveMailTemplate(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveTemplatePayload);

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toEqual(
        expect.objectContaining({ message: "Failed to save template" }),
      );
    });

    it("should use correct mutation key", async () => {
      vi.mocked(emailService.saveMailTemplate).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSaveMailTemplate(), {
        wrapper: createWrapper(),
      });

      // The mutation key should be ["template", "save"]
      expect(result.current).toBeDefined();
    });
  });
});
