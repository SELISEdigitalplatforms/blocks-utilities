import { z } from "zod";
import { zod3Messages } from "@/lib/zod3-messages";
import {
  COUNTRY_CODE_PATTERN,
  DESTINATION_PATTERN,
  E164_PATTERN,
  LANGUAGE_PATTERN,
  MAX_MESSAGE_LENGTH,
  SENDER_NAME_PATTERN,
  SmsProviderType,
  SmsUrlPolicy,
  TEMPLATE_NAME_PATTERN,
  TWILIO_ACCOUNT_SID_PATTERN,
} from "../constants/sms.constants";

// The checks without a message of their own carry zod 3's default one, so the forms read the same
// after the zod 4 upgrade.

// Number.isInteger, not .int(): zod 4's .int() also rejects unsafe integers and skips the checks
// after it and the superRefine below; zod 3's did neither.
const whole = (min: number, max: number, label: string) =>
  z.coerce
    .number<number>({ error: `${label} must be a number.` })
    .refine(Number.isInteger, `${label} must be a whole number.`)
    .min(min, `${label} must be at least ${min}.`)
    .max(max, `${label} must be at most ${max}.`);

const isGuid = (value: string) =>
  /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(value);

const isBase64Of32Bytes = (value: string) => {
  try {
    return atob(value.trim()).length === 32;
  } catch {
    return false;
  }
};

/**
 * The same rules as SaveSmsProviderConfigurationRequestValidator. `isCreate` is true when the
 * tenant has no stored key yet, which is the only time the key is required.
 */
export const createSmsProviderSchema = (isCreate: boolean) =>
  z
    .object({
      name: z.string().trim().min(1, "Name is required.").max(100, zod3Messages.stringMax(100)),
      providerType: z.union([z.literal(SmsProviderType.Twilio), z.literal(SmsProviderType.Telnyx)]),
      isEnabled: z.boolean(),
      isDefault: z.boolean(),
      senderNumber: z
        .string()
        .trim()
        .refine((value) => !value || E164_PATTERN.test(value), "Use E.164 format, e.g. +41791234567."),
      senderName: z
        .string()
        .trim()
        .refine(
          (value) => !value || SENDER_NAME_PATTERN.test(value),
          "1 to 11 letters, digits or spaces, with at least one letter.",
        ),
      // A refine, not .max(): zod 4's .max() stops at an oversized list without checking its items or
      // running the superRefine below, so the form would hide every other error; zod 3's did neither.
      senderNameExcludedPrefixes: z
        .array(z.string().regex(COUNTRY_CODE_PATTERN, "Country codes look like +1 or +86."))
        .refine((prefixes) => prefixes.length <= 50, "At most 50 country codes."),
      accountId: z.string().trim(),
      apiKey: z.string().max(512, zod3Messages.stringMax(512)),
      messagingProfileId: z.string().trim(),
      webhookPublicKey: z.string().trim(),
      statusCallbackBaseUrl: z
        .string()
        .trim()
        .refine((value) => {
          if (!value) return true;
          try {
            return new URL(value).protocol === "https:";
          } catch {
            return false;
          }
        }, "Must be an absolute https URL."),
      maxRetryAttempts: whole(1, 10, "Retry attempts"),
      deliveryCheckDelayMinutes: whole(1, 1440, "Delivery check delay"),
      rateLimit: z.object({
        tenantMaxPerWindow: whole(1, 100_000, "Tenant limit"),
        tenantWindowSeconds: whole(1, 86_400, "Tenant window"),
        recipientMaxPerWindow: whole(1, 1_000, "Recipient limit"),
        recipientWindowSeconds: whole(1, 86_400, "Recipient window"),
      }),
      spamFilter: z.object({
        enabled: z.boolean(),
        maxRecipients: whole(1, 1_000, "Maximum recipients"),
        maxMessageLength: whole(1, MAX_MESSAGE_LENGTH, "Maximum length"),
        urlPolicy: z.union([
          z.literal(SmsUrlPolicy.Allow),
          z.literal(SmsUrlPolicy.Flag),
          z.literal(SmsUrlPolicy.Block),
        ]),
        // A refine, not .max(), for the reason given at senderNameExcludedPrefixes.
        blockedTerms: z
          .array(z.string().trim().min(1, zod3Messages.stringMin(1)).max(50, zod3Messages.stringMax(50)))
          .refine((terms) => terms.length <= 100, "At most 100 terms."),
      }),
    })
    .superRefine((values, context) => {
      if (!values.senderNumber && !values.senderName) {
        for (const path of ["senderNumber", "senderName"]) {
          context.addIssue({
            code: "custom",
            path: [path],
            message: "Set a sender number or a sender name (or both).",
          });
        }
      }

      if (isCreate && !values.apiKey.trim()) {
        context.addIssue({
          code: "custom",
          path: ["apiKey"],
          message: "The provider key is required for a new configuration.",
        });
      }

      if (values.providerType === SmsProviderType.Twilio && !TWILIO_ACCOUNT_SID_PATTERN.test(values.accountId)) {
        context.addIssue({
          code: "custom",
          path: ["accountId"],
          message: "Twilio account SID is 'AC' followed by 32 hex characters.",
        });
      }

      if (values.providerType === SmsProviderType.Telnyx) {
        if (!isGuid(values.messagingProfileId)) {
          context.addIssue({
            code: "custom",
            path: ["messagingProfileId"],
            message: "Telnyx messaging profile id is a GUID.",
          });
        }

        if (!values.webhookPublicKey) {
          context.addIssue({
            code: "custom",
            path: ["webhookPublicKey"],
            message: "Required to verify Telnyx delivery callbacks.",
          });
        } else if (!isBase64Of32Bytes(values.webhookPublicKey)) {
          // Shape only; the Api also refuses the weak (small-order) keys that would verify forgeries.
          context.addIssue({
            code: "custom",
            path: ["webhookPublicKey"],
            message: "Paste the base64 public key from the Telnyx portal (32 bytes).",
          });
        }
      }
    });

export type SmsProviderFormValues = z.infer<ReturnType<typeof createSmsProviderSchema>>;

export const smsTemplateSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Name is required.")
    .max(100, zod3Messages.stringMax(100))
    .regex(TEMPLATE_NAME_PATTERN, "Letters, digits, '_', '.' and '-' only."),
  language: z.string().trim().regex(LANGUAGE_PATTERN, "Use a code like 'en' or 'en-US'."),
  body: z
    .string()
    .min(1, "Message text is required.")
    .max(MAX_MESSAGE_LENGTH, zod3Messages.stringMax(MAX_MESSAGE_LENGTH)),
});

export type SmsTemplateFormValues = z.infer<typeof smsTemplateSchema>;

export const sendSmsSchema = z
  .object({
    mode: z.enum(["text", "template"]),
    destinationNumbers: z
      .array(z.string().regex(DESTINATION_PATTERN, "7 to 15 digits, optionally starting with '+'."))
      .min(1, "Add at least one recipient."),
    messageText: z.string().max(MAX_MESSAGE_LENGTH, zod3Messages.stringMax(MAX_MESSAGE_LENGTH)),
    templateName: z.string().trim(),
    language: z.string().trim(),
    dataContext: z.record(z.string(), z.string()),
    correlationId: z
      .string()
      .trim()
      .max(100, zod3Messages.stringMax(100))
      .refine((value) => !value || /^[A-Za-z0-9_.-]+$/.test(value), "Letters, digits, '_', '.' and '-' only."),
  })
  .superRefine((values, context) => {
    if (values.mode === "text" && !values.messageText.trim()) {
      context.addIssue({ code: "custom", path: ["messageText"], message: "Message text is required." });
    }

    if (values.mode === "template" && !values.templateName) {
      context.addIssue({ code: "custom", path: ["templateName"], message: "Choose a template." });
    }
  });

export type SendSmsFormValues = z.infer<typeof sendSmsSchema>;
