import {
  IEmailConfig,
  IEmailTemplate,
  IEmailUsageResponse,
  IGetMailBoxMailResponse,
} from "../models/email";
import { http } from "@/lib/http-client";
import {
  EMAIL_TEMPLATE_ENDPOINTS,
  MAIL_CONFIG_ENDPOINTS,
  MAIL_ENDPOINTS,
} from "../constants/endpoint.constant";

class EmailService {
  fetchEmailConfigs = (
    projectKey: string,
    pageNumber: number,
    pageSize: number,
  ): Promise<IEmailConfig[]> => {
    return http.get(
      `${MAIL_CONFIG_ENDPOINTS.GET_CONFIGS}?projectKey=${projectKey}&pageNumber=${pageNumber + 1}&pageSize=${pageSize}`,
    );
  };

  fetchEmailTemplates = (
    pageNumber: number,
    pageSize: number,
    projectKey: string,
    searchKey: string,
    sortProperty: string = "Name",
    isDescending: boolean = false,
    language: string,
    mailConfigurationId: string,
  ): Promise<{ templates: IEmailTemplate[]; totalCount: number }> => {
    const url = `https://dev-logic.blocksdevelopers.com/api/template/gets?pageNumber=0&pageSize=10`;
    return http.get(url, undefined, { absoluteUrl: true });
  };

  fetchEmailTemplate = (projectKey: string, itemId: string): Promise<IEmailTemplate> => {
    return http.get(
      `${EMAIL_TEMPLATE_ENDPOINTS.GET_TEMPLATE}?itemId=${itemId}&projectKey=${projectKey}`,
    );
  };

  getMailBoxMails = (
    projectKey: string,
    pageNumber: number,
    pageSize: number,
    isInbound: boolean,
    searchText?: string,
    status?: string,
    startDate?: string,
    endDate?: string,
  ): Promise<IEmailUsageResponse> => {
    const params = new URLSearchParams({
      ProjectKey: projectKey,
      PageNumber: pageNumber.toString(),
      PageSize: pageSize.toString(),
      IsInbound: isInbound.toString(),
    });

    if (searchText) {
      params.append("SearchText", searchText);
    }
    if (status) {
      params.append("Status", status);
    }
    if (startDate) {
      params.append("SendDateRange.StartDate", startDate);
    }
    if (endDate) {
      params.append("SendDateRange.EndDate", endDate);
    }

    return http.get(`${MAIL_ENDPOINTS.GET_MAILBOX_MAILS}?${params.toString()}`);
  };

  getMailBoxMail = (projectKey: string, messageId: string): Promise<IGetMailBoxMailResponse> => {
    return http.get(
      `${MAIL_ENDPOINTS.GET_MAILBOX_MAIL}?ProjectKey=${projectKey}&MessageId=${messageId}`,
    );
  };

  saveMailConfig = (payload: {
    configurationId: string;
    configurationName: string;
    host: string;
    port: number;
    enableSSL: boolean;
    senderName: string;
    senderAddress: string;
    senderUserName: string;
    accountPassword: string;
    projectKey: string;
    isInbound: boolean;
    provider: number;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
    itemId: string;
  }> => {
    return http.post(MAIL_CONFIG_ENDPOINTS.SAVE_CONFIG, payload);
  };

  sendTestMail = (data: {
    to: string;
    purpose: string;
    language: string;
    projectKey: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
    itemId: string;
  }> => {
    const payload = {
      to: [data.to],
      purpose: data.purpose,
      language: data.language,
      replyTo: [data.to],
      projectKey: data.projectKey,
      isTestMail: true,
    };
    return http.post(MAIL_ENDPOINTS.SEND_TO_ANY, payload);
  };

  saveMailTemplate(requestBody: {
    itemId: string;
    mailConfigurationId?: string;
    language?: string;
    name?: string;
    templateSubject?: string;
    generatedBy?: string;
    templateBody?: string;
    jsonContent?: string;
    projectKey?: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
    itemId: string;
  }> {
    return http
      .post<{
        errors: null | unknown;
        isSuccess: boolean;
        itemId: string;
      }>(EMAIL_TEMPLATE_ENDPOINTS.SAVE_TEMPLATE, requestBody)
      .then((response) => response);
  }

  cloneMailTemplate(requestBody: {
    itemId: string;
    mailConfigurationId?: string;
    language?: string;
    name?: string;
    templateSubject?: string;
    projectKey: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
    itemId: string;
  }> {
    return http
      .post<{
        errors: null | unknown;
        isSuccess: boolean;
        itemId: string;
      }>(EMAIL_TEMPLATE_ENDPOINTS.CLONE_TEMPLATE, requestBody)
      .then((response) => response);
  }

  deleteMailTemplate(payload: { itemId: string; projectKey: string }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> {
    return http
      .delete<{
        errors: unknown;
        isSuccess: boolean;
      }>(
        `${EMAIL_TEMPLATE_ENDPOINTS.DELETE_TEMPLATE}?itemId=${payload.itemId}&projectKey=${payload.projectKey}`,
      )
      .then((response) => response);
  }

  deleteMailConfig(payload: { configurationId: string; projectKey: string }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> {
    return http
      .delete<{
        errors: unknown;
        isSuccess: boolean;
      }>(
        `${MAIL_CONFIG_ENDPOINTS.DELETE_CONFIG}?configurationId=${payload.configurationId}&projectKey=${payload.projectKey}`,
      )
      .then((response) => response);
  }
}
export default EmailService;
export const emailService = new EmailService();
