import { z } from "zod";
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
import { tStatic } from "@seliseblocks/genesis-os";

const whole = (min: number, max: number, label: string) =>
  z.coerce
    .number({ invalid_type_error: tStatic("utilities.sms", "SMS_SCHEMA.LABEL_MUST_BE_NUMBER", { label }) })
    .int(tStatic("utilities.sms", "SMS_SCHEMA.LABEL_MUST_BE_WHOLE", { label }))
    .min(min, tStatic("utilities.sms", "SMS_SCHEMA.LABEL_MUST_BE_AT_LEAST", { label, min }))
    .max(max, tStatic("utilities.sms", "SMS_SCHEMA.LABEL_MUST_BE_AT_MOST", { label, max }));

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
      name: z.string().trim().min(1, tStatic("utilities.sms", "SMS_SCHEMA.NAME_IS_REQUIRED")).max(100),
      providerType: z.union([z.literal(SmsProviderType.Twilio), z.literal(SmsProviderType.Telnyx)]),
      isEnabled: z.boolean(),
      isDefault: z.boolean(),
      senderNumber: z
        .string()
        .trim()
        .refine((value) => !value || E164_PATTERN.test(value), tStatic("utilities.sms", "SMS_SCHEMA.USE_E_164_FORMAT_E")),
      senderName: z
        .string()
        .trim()
        .refine(
          (value) => !value || SENDER_NAME_PATTERN.test(value),
          tStatic("utilities.sms", "SMS_SCHEMA.SENDER_NAME_FORMAT"),
        ),
      senderNameExcludedPrefixes: z
        .array(z.string().regex(COUNTRY_CODE_PATTERN, tStatic("utilities.sms", "SMS_SCHEMA.COUNTRY_CODES_LOOK_LIKE_1")))
        .max(50, tStatic("utilities.sms", "SMS_SCHEMA.AT_MOST_50_COUNTRY_CODES")),
      accountId: z.string().trim(),
      apiKey: z.string().max(512),
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
        }, tStatic("utilities.sms", "SMS_SCHEMA.MUST_BE_AN_ABSOLUTE_HTTPS")),
      maxRetryAttempts: whole(1, 10, tStatic("utilities.sms", "SMS_SCHEMA.RETRY_ATTEMPTS")),
      deliveryCheckDelayMinutes: whole(1, 1440, tStatic("utilities.sms", "SMS_SCHEMA.DELIVERY_CHECK_DELAY")),
      rateLimit: z.object({
        tenantMaxPerWindow: whole(1, 100_000, tStatic("utilities.sms", "SMS_SCHEMA.TENANT_LIMIT")),
        tenantWindowSeconds: whole(1, 86_400, tStatic("utilities.sms", "SMS_SCHEMA.TENANT_WINDOW")),
        recipientMaxPerWindow: whole(1, 1_000, tStatic("utilities.sms", "SMS_SCHEMA.RECIPIENT_LIMIT")),
        recipientWindowSeconds: whole(1, 86_400, tStatic("utilities.sms", "SMS_SCHEMA.RECIPIENT_WINDOW")),
      }),
      spamFilter: z.object({
        enabled: z.boolean(),
        maxRecipients: whole(1, 1_000, tStatic("utilities.sms", "SMS_SCHEMA.MAXIMUM_RECIPIENTS")),
        maxMessageLength: whole(1, MAX_MESSAGE_LENGTH, tStatic("utilities.sms", "SMS_SCHEMA.MAXIMUM_LENGTH")),
        urlPolicy: z.union([
          z.literal(SmsUrlPolicy.Allow),
          z.literal(SmsUrlPolicy.Flag),
          z.literal(SmsUrlPolicy.Block),
        ]),
        blockedTerms: z.array(z.string().trim().min(1).max(50)).max(100, tStatic("utilities.sms", "SMS_SCHEMA.AT_MOST_100_TERMS")),
      }),
    })
    .superRefine((values, context) => {
      if (!values.senderNumber && !values.senderName) {
        for (const path of ["senderNumber", "senderName"]) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: [path],
            message: tStatic("utilities.sms", "SMS_SCHEMA.SET_A_SENDER_NUMBER_OR"),
          });
        }
      }

      if (isCreate && !values.apiKey.trim()) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["apiKey"],
          message: tStatic("utilities.sms", "SMS_SCHEMA.THE_PROVIDER_KEY_IS_REQUIRED"),
        });
      }

      if (values.providerType === SmsProviderType.Twilio && !TWILIO_ACCOUNT_SID_PATTERN.test(values.accountId)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["accountId"],
          message: tStatic("utilities.sms", "SMS_SCHEMA.TWILIO_ACCOUNT_SID_IS_AC"),
        });
      }

      if (values.providerType === SmsProviderType.Telnyx) {
        if (!isGuid(values.messagingProfileId)) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["messagingProfileId"],
            message: tStatic("utilities.sms", "SMS_SCHEMA.TELNYX_MESSAGING_PROFILE_ID_IS"),
          });
        }

        if (!values.webhookPublicKey) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["webhookPublicKey"],
            message: tStatic("utilities.sms", "SMS_SCHEMA.REQUIRED_TO_VERIFY_TELNYX_DELIVERY"),
          });
        } else if (!isBase64Of32Bytes(values.webhookPublicKey)) {
          // Shape only; the Api also refuses the weak (small-order) keys that would verify forgeries.
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["webhookPublicKey"],
            message: tStatic("utilities.sms", "SMS_SCHEMA.PASTE_THE_BASE64_PUBLIC_KEY"),
          });
        }
      }
    });

export type SmsProviderFormValues = z.infer<ReturnType<typeof createSmsProviderSchema>>;

export const createSmsTemplateSchema = () =>
  z.object({
  name: z
    .string()
    .trim()
    .min(1, tStatic("utilities.sms", "SMS_SCHEMA.NAME_IS_REQUIRED"))
    .max(100)
    .regex(TEMPLATE_NAME_PATTERN, tStatic("utilities.sms", "SMS_SCHEMA.LETTERS_DIGITS_ONLY")),
  language: z.string().trim().regex(LANGUAGE_PATTERN, tStatic("utilities.sms", "SMS_SCHEMA.USE_A_CODE_LIKE_EN")),
  body: z.string().min(1, tStatic("utilities.sms", "SMS_SCHEMA.MESSAGE_TEXT_IS_REQUIRED")).max(MAX_MESSAGE_LENGTH),
});

export type SmsTemplateFormValues = z.infer<ReturnType<typeof createSmsTemplateSchema>>;

export const createSendSmsSchema = () =>
  z
  .object({
    mode: z.enum(["text", "template"]),
    destinationNumbers: z
      .array(z.string().regex(DESTINATION_PATTERN, tStatic("utilities.sms", "SMS_SCHEMA.DESTINATION_DIGITS")))
      .min(1, tStatic("utilities.sms", "SMS_SCHEMA.ADD_AT_LEAST_ONE_RECIPIENT")),
    messageText: z.string().max(MAX_MESSAGE_LENGTH),
    templateName: z.string().trim(),
    language: z.string().trim(),
    dataContext: z.record(z.string()),
    correlationId: z
      .string()
      .trim()
      .max(100)
      .refine((value) => !value || /^[A-Za-z0-9_.-]+$/.test(value), tStatic("utilities.sms", "SMS_SCHEMA.LETTERS_DIGITS_ONLY")),
  })
  .superRefine((values, context) => {
    if (values.mode === "text" && !values.messageText.trim()) {
      context.addIssue({ code: z.ZodIssueCode.custom, path: ["messageText"], message: tStatic("utilities.sms", "SMS_SCHEMA.MESSAGE_TEXT_IS_REQUIRED") });
    }

    if (values.mode === "template" && !values.templateName) {
      context.addIssue({ code: z.ZodIssueCode.custom, path: ["templateName"], message: tStatic("utilities.sms", "SMS_SCHEMA.CHOOSE_A_TEMPLATE") });
    }
  });

export type SendSmsFormValues = z.infer<ReturnType<typeof createSendSmsSchema>>;
