import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import {
  mockEmailConfigList,
  mockEmailTemplatesResponse,
  mockEmailTemplate,
  mockEmailUsageResponse,
  mockGetMailBoxMailResponse,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";
import { http } from "@/lib/http-client";
import EmailService from "./email.services";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import {
  EMAIL_TEMPLATE_ENDPOINTS,
  MAIL_CONFIG_ENDPOINTS,
  MAIL_ENDPOINTS,
} from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("EmailService", () => {
  let service: EmailService;

  beforeEach(() => {
    service = new EmailService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  describe("fetchEmailConfigs", () => {
    it("should call correct endpoint with pagination params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailConfigList);

      const result = await service.fetchEmailConfigs(TEST_PROJECT_KEY, 0, 10);

      expect(http.get).toHaveBeenCalledWith(
        `${MAIL_CONFIG_ENDPOINTS.GET_CONFIGS}?projectKey=${TEST_PROJECT_KEY}&pageNumber=1&pageSize=10`,
      );
      expect(result).toEqual(mockEmailConfigList);
    });

    it("should convert pageNumber from 0-based to 1-based", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailConfigList);

      await service.fetchEmailConfigs(TEST_PROJECT_KEY, 2, 20);

      expect(http.get).toHaveBeenCalledWith(
        `${MAIL_CONFIG_ENDPOINTS.GET_CONFIGS}?projectKey=${TEST_PROJECT_KEY}&pageNumber=3&pageSize=20`,
      );
    });

    it("should handle API errors", async () => {
      const error = new Error("Network error");
      vi.mocked(http.get).mockRejectedValue(error);

      await expect(service.fetchEmailConfigs(TEST_PROJECT_KEY, 0, 10)).rejects.toThrow(
        "Network error",
      );
    });

    it("should handle different page sizes", async () => {
      vi.mocked(http.get).mockResolvedValue([]);

      await service.fetchEmailConfigs(TEST_PROJECT_KEY, 0, 50);

      expect(http.get).toHaveBeenCalledWith(
        `${MAIL_CONFIG_ENDPOINTS.GET_CONFIGS}?projectKey=${TEST_PROJECT_KEY}&pageNumber=1&pageSize=50`,
      );
    });
  });

  describe("fetchEmailTemplates", () => {
    it("should call correct endpoint with all parameters", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailTemplatesResponse);

      const result = await service.fetchEmailTemplates(
        1,
        10,
        TEST_PROJECT_KEY,
        "welcome",
        "Name",
        false,
        "en",
        "config-1",
      );

      expect(http.get).toHaveBeenCalledWith(
        `${EMAIL_TEMPLATE_ENDPOINTS.GET_TEMPLATES}?pageNumber=1&pageSize=10&projectKey=${TEST_PROJECT_KEY}&searchKey=welcome&sortProperty=Name&isDescending=false&language=en&mailConfigurationId=config-1`,
      );
      expect(result).toEqual(mockEmailTemplatesResponse);
    });

    it("should use default sort property and direction", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailTemplatesResponse);

      await service.fetchEmailTemplates(0, 10, TEST_PROJECT_KEY, "", "Name", false, "", "");

      expect(http.get).toHaveBeenCalledWith(
        `${EMAIL_TEMPLATE_ENDPOINTS.GET_TEMPLATES}?pageNumber=0&pageSize=10&projectKey=${TEST_PROJECT_KEY}&searchKey=&sortProperty=Name&isDescending=false&language=&mailConfigurationId=`,
      );
    });

    it("should handle descending sort", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailTemplatesResponse);

      await service.fetchEmailTemplates(0, 10, TEST_PROJECT_KEY, "", "CreatedDate", true, "en", "");

      expect(http.get).toHaveBeenCalledWith(
        `${EMAIL_TEMPLATE_ENDPOINTS.GET_TEMPLATES}?pageNumber=0&pageSize=10&projectKey=${TEST_PROJECT_KEY}&searchKey=&sortProperty=CreatedDate&isDescending=true&language=en&mailConfigurationId=`,
      );
    });

    it("should handle API errors", async () => {
      const error = new Error("Failed to fetch templates");
      vi.mocked(http.get).mockRejectedValue(error);

      await expect(
        service.fetchEmailTemplates(0, 10, TEST_PROJECT_KEY, "", "Name", false, "", ""),
      ).rejects.toThrow("Failed to fetch templates");
    });
  });

  describe("fetchEmailTemplate", () => {
    it("should call correct endpoint with itemId and projectKey", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailTemplate);

      const result = await service.fetchEmailTemplate(TEST_PROJECT_KEY, "template-1");

      expect(http.get).toHaveBeenCalledWith(
        `${EMAIL_TEMPLATE_ENDPOINTS.GET_TEMPLATE}?itemId=template-1&projectKey=${TEST_PROJECT_KEY}`,
      );
      expect(result).toEqual(mockEmailTemplate);
    });

    it("should handle API errors", async () => {
      const error = new Error("Template not found");
      vi.mocked(http.get).mockRejectedValue(error);

      await expect(service.fetchEmailTemplate(TEST_PROJECT_KEY, "invalid-id")).rejects.toThrow(
        "Template not found",
      );
    });
  });

  describe("getMailBoxMails", () => {
    it("should call correct endpoint with required params only", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailUsageResponse);

      const result = await service.getMailBoxMails(TEST_PROJECT_KEY, 1, 10, false);

      expect(http.get).toHaveBeenCalledWith(
        expect.stringContaining(`${MAIL_ENDPOINTS.GET_MAILBOX_MAILS}?`),
      );
      expect(http.get).toHaveBeenCalledWith(
        expect.stringContaining(`ProjectKey=${TEST_PROJECT_KEY}`),
      );
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining("PageNumber=1"));
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining("PageSize=10"));
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining("IsInbound=false"));
      expect(result).toEqual(mockEmailUsageResponse);
    });

    it("should include optional searchText parameter", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailUsageResponse);

      await service.getMailBoxMails(TEST_PROJECT_KEY, 0, 10, false, "test search");

      expect(http.get).toHaveBeenCalledWith(expect.stringContaining("SearchText=test+search"));
    });

    it("should include optional status parameter", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailUsageResponse);

      await service.getMailBoxMails(TEST_PROJECT_KEY, 0, 10, false, undefined, "Delivered");

      expect(http.get).toHaveBeenCalledWith(expect.stringContaining("Status=Delivered"));
    });

    it("should include optional date range parameters", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailUsageResponse);

      await service.getMailBoxMails(
        TEST_PROJECT_KEY,
        0,
        10,
        false,
        undefined,
        undefined,
        "2024-01-01",
        "2024-01-31",
      );

      expect(http.get).toHaveBeenCalledWith(
        expect.stringContaining("SendDateRange.StartDate=2024-01-01"),
      );
      expect(http.get).toHaveBeenCalledWith(
        expect.stringContaining("SendDateRange.EndDate=2024-01-31"),
      );
    });

    it("should include all optional parameters when provided", async () => {
      vi.mocked(http.get).mockResolvedValue(mockEmailUsageResponse);

      await service.getMailBoxMails(
        TEST_PROJECT_KEY,
        0,
        10,
        true,
        "test",
        "Sent",
        "2024-01-01",
        "2024-01-31",
      );

      const call = vi.mocked(http.get).mock.calls[0][0];
      expect(call).toContain("IsInbound=true");
      expect(call).toContain("SearchText=test");
      expect(call).toContain("Status=Sent");
      expect(call).toContain("SendDateRange.StartDate=2024-01-01");
      expect(call).toContain("SendDateRange.EndDate=2024-01-31");
    });

    it("should handle API errors", async () => {
      const error = new Error("Failed to fetch emails");
      vi.mocked(http.get).mockRejectedValue(error);

      await expect(service.getMailBoxMails(TEST_PROJECT_KEY, 0, 10, false)).rejects.toThrow(
        "Failed to fetch emails",
      );
    });
  });

  describe("getMailBoxMail", () => {
    it("should call correct endpoint with messageId and projectKey", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetMailBoxMailResponse);

      const result = await service.getMailBoxMail(TEST_PROJECT_KEY, "msg-123");

      expect(http.get).toHaveBeenCalledWith(
        `${MAIL_ENDPOINTS.GET_MAILBOX_MAIL}?ProjectKey=${TEST_PROJECT_KEY}&MessageId=msg-123`,
      );
      expect(result).toEqual(mockGetMailBoxMailResponse);
    });

    it("should handle API errors", async () => {
      const error = new Error("Email not found");
      vi.mocked(http.get).mockRejectedValue(error);

      await expect(service.getMailBoxMail(TEST_PROJECT_KEY, "invalid-id")).rejects.toThrow(
        "Email not found",
      );
    });
  });

  describe("saveMailConfig", () => {
    it("should call correct endpoint with payload", async () => {
      const payload = {
        configurationId: "",
        configurationName: "New Config",
        host: "smtp.example.com",
        port: 587,
        enableSSL: true,
        senderName: "Test",
        senderAddress: "test@example.com",
        senderUserName: "test@example.com",
        accountPassword: "password",
        projectKey: TEST_PROJECT_KEY,
        isInbound: false,
        provider: 1,
      };
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveMailConfig(payload);

      expect(http.post).toHaveBeenCalledWith(MAIL_CONFIG_ENDPOINTS.SAVE_CONFIG, payload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle update request with configurationId", async () => {
      const payload = {
        configurationId: "config-123",
        configurationName: "Updated Config",
        host: "smtp.example.com",
        port: 587,
        enableSSL: true,
        senderName: "Test",
        senderAddress: "test@example.com",
        senderUserName: "test@example.com",
        accountPassword: "password",
        projectKey: TEST_PROJECT_KEY,
        isInbound: false,
        provider: 1,
      };
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.saveMailConfig(payload);

      expect(http.post).toHaveBeenCalledWith(MAIL_CONFIG_ENDPOINTS.SAVE_CONFIG, payload);
    });

    it("should handle API errors", async () => {
      const error = new Error("Failed to save config");
      vi.mocked(http.post).mockRejectedValue(error);

      const payload = {
        configurationId: "",
        configurationName: "New Config",
        host: "smtp.example.com",
        port: 587,
        enableSSL: true,
        senderName: "Test",
        senderAddress: "test@example.com",
        senderUserName: "test@example.com",
        accountPassword: "password",
        projectKey: TEST_PROJECT_KEY,
        isInbound: false,
        provider: 1,
      };

      await expect(service.saveMailConfig(payload)).rejects.toThrow("Failed to save config");
    });
  });

  describe("sendTestMail", () => {
    it("should call correct endpoint with transformed payload", async () => {
      const data = {
        to: "test@example.com",
        purpose: "test-purpose",
        language: "en",
        projectKey: TEST_PROJECT_KEY,
      };
      const expectedPayload = {
        to: ["test@example.com"],
        purpose: "test-purpose",
        language: "en",
        replyTo: ["test@example.com"],
        projectKey: TEST_PROJECT_KEY,
        isTestMail: true,
      };
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.sendTestMail(data);

      expect(http.post).toHaveBeenCalledWith(MAIL_ENDPOINTS.SEND_TO_ANY, expectedPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      const error = new Error("Failed to send test email");
      vi.mocked(http.post).mockRejectedValue(error);

      const data = {
        to: "test@example.com",
        purpose: "test-purpose",
        language: "en",
        projectKey: TEST_PROJECT_KEY,
      };

      await expect(service.sendTestMail(data)).rejects.toThrow("Failed to send test email");
    });
  });

  describe("saveMailTemplate", () => {
    it("should call correct endpoint with payload", async () => {
      const requestBody = {
        itemId: "",
        mailConfigurationId: "config-1",
        language: "en",
        name: "New Template",
        templateSubject: "Subject",
        generatedBy: "BeeJS",
        templateBody: "<html></html>",
        jsonContent: "{}",
        projectKey: TEST_PROJECT_KEY,
      };
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveMailTemplate(requestBody);

      expect(http.post).toHaveBeenCalledWith(EMAIL_TEMPLATE_ENDPOINTS.SAVE_TEMPLATE, requestBody);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle update request with itemId", async () => {
      const requestBody = {
        itemId: "template-123",
        name: "Updated Template",
        projectKey: TEST_PROJECT_KEY,
      };
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.saveMailTemplate(requestBody);

      expect(http.post).toHaveBeenCalledWith(EMAIL_TEMPLATE_ENDPOINTS.SAVE_TEMPLATE, requestBody);
    });

    it("should handle API errors", async () => {
      const error = new Error("Failed to save template");
      vi.mocked(http.post).mockRejectedValue(error);

      const requestBody = {
        itemId: "",
        name: "New Template",
        projectKey: TEST_PROJECT_KEY,
      };

      await expect(service.saveMailTemplate(requestBody)).rejects.toThrow(
        "Failed to save template",
      );
    });
  });

  describe("cloneMailTemplate", () => {
    it("should call correct endpoint with payload", async () => {
      const requestBody = {
        itemId: "template-1",
        mailConfigurationId: "config-1",
        language: "en",
        name: "Cloned Template",
        templateSubject: "Cloned Subject",
        projectKey: TEST_PROJECT_KEY,
      };
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.cloneMailTemplate(requestBody);

      expect(http.post).toHaveBeenCalledWith(EMAIL_TEMPLATE_ENDPOINTS.CLONE_TEMPLATE, requestBody);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      const error = new Error("Failed to clone template");
      vi.mocked(http.post).mockRejectedValue(error);

      const requestBody = {
        itemId: "template-1",
        name: "Cloned Template",
        projectKey: TEST_PROJECT_KEY,
      };

      await expect(service.cloneMailTemplate(requestBody)).rejects.toThrow(
        "Failed to clone template",
      );
    });
  });

  describe("deleteMailTemplate", () => {
    it("should call correct endpoint with itemId and projectKey", async () => {
      const payload = {
        itemId: "template-1",
        projectKey: TEST_PROJECT_KEY,
      };
      vi.mocked(http.delete).mockResolvedValue(mockSuccessResponse);

      const result = await service.deleteMailTemplate(payload);

      expect(http.delete).toHaveBeenCalledWith(
        `${EMAIL_TEMPLATE_ENDPOINTS.DELETE_TEMPLATE}?itemId=template-1&projectKey=${TEST_PROJECT_KEY}`,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      const error = new Error("Failed to delete template");
      vi.mocked(http.delete).mockRejectedValue(error);

      const payload = {
        itemId: "template-1",
        projectKey: TEST_PROJECT_KEY,
      };

      await expect(service.deleteMailTemplate(payload)).rejects.toThrow(
        "Failed to delete template",
      );
    });
  });

  describe("deleteMailConfig", () => {
    it("should call correct endpoint with configurationId and projectKey", async () => {
      const payload = {
        configurationId: "config-1",
        projectKey: TEST_PROJECT_KEY,
      };
      vi.mocked(http.delete).mockResolvedValue(mockSuccessResponse);

      const result = await service.deleteMailConfig(payload);

      expect(http.delete).toHaveBeenCalledWith(
        `${MAIL_CONFIG_ENDPOINTS.DELETE_CONFIG}?configurationId=config-1&projectKey=${TEST_PROJECT_KEY}`,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      const error = new Error("Failed to delete config");
      vi.mocked(http.delete).mockRejectedValue(error);

      const payload = {
        configurationId: "config-1",
        projectKey: TEST_PROJECT_KEY,
      };

      await expect(service.deleteMailConfig(payload)).rejects.toThrow("Failed to delete config");
    });
  });
});
