import type { SmsProviderType, SmsUrlPolicy } from "../constants/sms.constants";

export interface SmsRateLimitSettings {
  tenantMaxPerWindow: number;
  tenantWindowSeconds: number;
  recipientMaxPerWindow: number;
  recipientWindowSeconds: number;
}

export interface SmsSpamFilterSettings {
  enabled: boolean;
  maxRecipients: number;
  maxMessageLength: number;
  urlPolicy: SmsUrlPolicy;
  blockedTerms: string[];
}

/** What the Api returns: everything but the key. */
export interface SmsProviderConfiguration {
  itemId: string;
  name: string;
  providerType: SmsProviderType;
  isDefault: boolean;
  isEnabled: boolean;
  senderNumber: string;
  senderName?: string | null;
  senderNameExcludedPrefixes: string[];
  accountId: string;
  hasApiKey: boolean;
  messagingProfileId?: string | null;
  webhookPublicKey?: string | null;
  statusCallbackBaseUrl?: string | null;
  maxRetryAttempts: number;
  deliveryCheckDelayMinutes: number;
  rateLimit: SmsRateLimitSettings;
  spamFilter: SmsSpamFilterSettings;
  lastUpdatedDate: string;
}

export interface SaveSmsProviderConfigurationRequest {
  configurationId?: string;
  name: string;
  providerType: SmsProviderType;
  isDefault: boolean;
  isEnabled: boolean;
  senderNumber?: string;
  senderName?: string;
  senderNameExcludedPrefixes: string[];
  accountId?: string;
  /** Omitted on update to keep the stored key; a value rotates it. */
  apiKey?: string;
  messagingProfileId?: string;
  webhookPublicKey?: string;
  statusCallbackBaseUrl?: string;
  maxRetryAttempts: number;
  deliveryCheckDelayMinutes: number;
  rateLimit: SmsRateLimitSettings;
  spamFilter: SmsSpamFilterSettings;
}

export interface SmsTemplate {
  itemId: string;
  name: string;
  language: string;
  body: string;
  placeholders: string[];
  createdDate: string;
  lastUpdatedDate: string;
}

export interface SaveSmsTemplateRequest {
  templateId?: string;
  name: string;
  language: string;
  body: string;
}

export interface SmsTemplateQuery {
  search?: string;
  language?: string;
  page: number;
  pageSize: number;
}

export interface SmsTemplateList {
  items: SmsTemplate[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface SendSmsRequest {
  destinationNumbers: string[];
  messageText: string;
  correlationId?: string;
}

export interface SendSmsByTemplateRequest {
  destinationNumbers: string[];
  templateName: string;
  language: string;
  dataContext: Record<string, string>;
  correlationId?: string;
}

export interface SmsMutationResult {
  isSuccess: boolean;
  messageId?: string | null;
  errors: Record<string, string>;
}
