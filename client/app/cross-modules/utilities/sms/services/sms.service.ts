import { HttpError } from "@seliseblocks/genesis-os/lib";
import { serviceInstances } from "@/lib/http-client";
import { SMS_ENDPOINT } from "../constants/sms.constants";
import type {
  SaveSmsProviderConfigurationRequest,
  SaveSmsTemplateRequest,
  SendSmsByTemplateRequest,
  SendSmsRequest,
  SmsMutationResult,
  SmsProviderConfiguration,
  SmsTemplate,
  SmsTemplateList,
  SmsTemplateQuery,
} from "../models/sms.model";

/**
 * The SMS Api answers a refused request with 400 and `{ errors: { Field: message } }`, which the
 * HttpClient hands over as `HttpError.errors`. This keeps the field names, so a form can put each
 * message under the input it belongs to.
 */
export class SmsRequestError extends Error {
  constructor(
    message: string,
    readonly fieldErrors: Record<string, string>,
    readonly status?: number,
  ) {
    super(message);
    this.name = "SmsRequestError";
  }
}

const toSmsRequestError = (error: unknown, fallback: string): SmsRequestError => {
  if (error instanceof SmsRequestError) {
    return error;
  }

  if (error instanceof HttpError) {
    const fieldErrors = Object.fromEntries(
      Object.entries(error.errors ?? {}).map(([field, value]) => [
        field,
        Array.isArray(value) ? value.join(" ") : String(value),
      ]),
    );
    const first = Object.values(fieldErrors)[0];
    return new SmsRequestError(first || fallback, fieldErrors, error.status);
  }

  return new SmsRequestError(
    error instanceof Error ? error.message : fallback,
    {},
  );
};

const call = async <T>(request: () => Promise<T>, fallback: string): Promise<T> => {
  try {
    return await request();
  } catch (error) {
    throw toSmsRequestError(error, fallback);
  }
};

const client = () => serviceInstances.utitlitiesService;

export const smsService = {
  /** Null when the tenant has no enabled configuration yet (the Api answers 404). */
  async getProviderConfiguration(): Promise<SmsProviderConfiguration | null> {
    try {
      const response = await client().get<{
        configuration?: SmsProviderConfiguration | null;
      }>(`${SMS_ENDPOINT}/GetProviderConfiguration`);
      return response.configuration ?? null;
    } catch (error) {
      if (error instanceof HttpError && error.status === 404) {
        return null;
      }

      throw toSmsRequestError(error, "The SMS provider configuration could not be loaded.");
    }
  },

  saveProviderConfiguration(request: SaveSmsProviderConfigurationRequest) {
    return call(
      () => client().post<SmsMutationResult>(`${SMS_ENDPOINT}/SaveProviderConfiguration`, request),
      "The SMS provider configuration could not be saved.",
    );
  },

  getTemplates(query: SmsTemplateQuery) {
    const parameters = new URLSearchParams({
      page: query.page.toString(),
      pageSize: query.pageSize.toString(),
    });
    if (query.search?.trim()) parameters.append("search", query.search.trim());
    if (query.language?.trim()) parameters.append("language", query.language.trim());

    return call(
      () => client().get<SmsTemplateList>(`${SMS_ENDPOINT}/GetTemplates?${parameters}`),
      "SMS templates could not be loaded.",
    );
  },

  async saveTemplate(request: SaveSmsTemplateRequest): Promise<SmsTemplate> {
    const response = await call(
      () => client().post<{ template?: SmsTemplate }>(`${SMS_ENDPOINT}/SaveTemplate`, request),
      "The SMS template could not be saved.",
    );

    if (!response.template) {
      throw new SmsRequestError("The SMS template could not be saved.", {});
    }

    return response.template;
  },

  deleteTemplate(templateId: string) {
    return call(
      () =>
        client().delete<SmsMutationResult>(
          `${SMS_ENDPOINT}/DeleteTemplate?templateId=${encodeURIComponent(templateId)}`,
        ),
      "The SMS template could not be deleted.",
    );
  },

  send(request: SendSmsRequest) {
    return call(
      () => client().post<SmsMutationResult>(`${SMS_ENDPOINT}/Send`, request),
      "The SMS could not be sent.",
    );
  },

  sendByTemplate(request: SendSmsByTemplateRequest) {
    return call(
      () => client().post<SmsMutationResult>(`${SMS_ENDPOINT}/SendByTemplate`, request),
      "The SMS could not be sent.",
    );
  },
};
