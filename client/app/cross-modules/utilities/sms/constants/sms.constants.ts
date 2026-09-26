export const SMS_ENDPOINT = "/api/Sms";

export const SMS_PROVIDER_QUERY_KEY = "sms-provider-configuration";
export const SMS_TEMPLATES_QUERY_KEY = "sms-templates";

/** Mirrors SmsProviderType on the server; sent as its number. */
export const SmsProviderType = {
  Twilio: 1,
  Telnyx: 2,
} as const;
export type SmsProviderType =
  (typeof SmsProviderType)[keyof typeof SmsProviderType];

export const SMS_PROVIDER_OPTIONS = [
  { value: SmsProviderType.Twilio, label: "Twilio" },
  { value: SmsProviderType.Telnyx, label: "Telnyx" },
] as const;

/** Mirrors SmsUrlPolicy on the server. */
export const SmsUrlPolicy = {
  Allow: 1,
  Flag: 2,
  Block: 3,
} as const;
export type SmsUrlPolicy = (typeof SmsUrlPolicy)[keyof typeof SmsUrlPolicy];

export const SMS_URL_POLICY_OPTIONS = [
  {
    value: SmsUrlPolicy.Allow,
    label: "Allow",
    hint: "Messages with links are sent without comment.",
  },
  {
    value: SmsUrlPolicy.Flag,
    label: "Flag",
    hint: "Sent, but recorded as high risk.",
  },
  {
    value: SmsUrlPolicy.Block,
    label: "Block",
    hint: "Messages with links are quarantined.",
  },
] as const;

export const SMS_TEMPLATE_PAGE_SIZE = 20;

// Same rules the server validates, so the form fails before the round trip.
export const E164_PATTERN = /^\+[1-9][0-9]{6,14}$/;
export const DESTINATION_PATTERN = /^\+?[0-9]{7,15}$/;
export const SENDER_NAME_PATTERN = /^(?=.*[A-Za-z])[A-Za-z0-9 ]{1,11}$/;
export const COUNTRY_CODE_PATTERN = /^\+[1-9][0-9]{0,3}$/;
export const TWILIO_ACCOUNT_SID_PATTERN = /^AC[0-9a-fA-F]{32}$/;
export const TEMPLATE_NAME_PATTERN = /^[A-Za-z0-9_.-]+$/;
export const LANGUAGE_PATTERN = /^[a-z]{2,3}(-[A-Z]{2})?$/;
export const MAX_MESSAGE_LENGTH = 1600;

/** Mirrors SmsTemplateRenderer's placeholder syntax. */
export const PLACEHOLDER_PATTERN = /\{\{\s*([A-Za-z0-9_.-]+)\s*\}\}/g;
