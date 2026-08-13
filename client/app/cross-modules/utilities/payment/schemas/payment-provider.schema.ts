import { z } from "zod";
import type { PaymentProviderName } from "../models/payment-provider.model";

const optionalText = (maximumLength: number) =>
  z
    .string()
    .trim()
    .max(maximumLength)
    .optional()
    .or(z.literal(""));

const publicHttpsUrl = z
  .string()
  .trim()
  .min(1, "Enter the frontend result URL.")
  .refine((value) => {
    try {
      const url = new URL(value);
      return url.protocol === "https:" && Boolean(url.hostname);
    } catch {
      return false;
    }
  }, "Enter an absolute HTTPS URL.");

const providerConfigurationShape = {
  frontendResultUrl: publicHttpsUrl,
  countryCode: z
    .string()
    .trim()
    .refine(
      (value) => value.length === 0 || /^[A-Za-z]{2}$/.test(value),
      "Use a two-letter ISO country code.",
    ),
  manualCapture: z.boolean(),
  maxRefundDays: z.coerce
    .number()
    .int("Use a whole number.")
    .min(0)
    .max(3650),
  storeId: optionalText(200),
};

const isAdyenHmac = (value: string) => /^[0-9A-Fa-f]{64}$/.test(value);

export const registerPaymentProviderSchema = z
  .object({
    providerName: z.enum(["ADYEN-ONLINE", "STRIPE"]),
    merchantId: z.string().trim().min(1).max(200),
    // Blank means "use whichever organization my context carries", which is what every
    // registration did before this field existed.
    organizationId: optionalText(200),
    ...providerConfigurationShape,
    apiBaseUrl: optionalText(500),
    apiKey: z.string().trim().min(1).max(8192),
    webhookHmacKey: z.string().trim().min(1).max(8192),
    tokenHmacKey: optionalText(8192),
  })
  .superRefine((value, context) => {
    if (value.providerName === "ADYEN-ONLINE") {
      if (!value.apiBaseUrl) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["apiBaseUrl"],
          message: "Adyen requires its Checkout API base URL.",
        });
      }

      if (!isAdyenHmac(value.webhookHmacKey)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["webhookHmacKey"],
          message: "Use the 64-character hexadecimal Adyen HMAC key.",
        });
      }

      if (!value.tokenHmacKey || !isAdyenHmac(value.tokenHmacKey)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["tokenHmacKey"],
          message: "Use the 64-character hexadecimal token-webhook HMAC key.",
        });
      }

      return;
    }

    if (
      !value.apiKey.startsWith("sk_") &&
      !value.apiKey.startsWith("rk_")
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["apiKey"],
        message: "Stripe API keys start with sk_ or rk_.",
      });
    }

    if (!value.webhookHmacKey.startsWith("whsec_")) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["webhookHmacKey"],
        message: "Stripe endpoint secrets start with whsec_.",
      });
    }
  });

export const updatePaymentProviderSchema = z.object({
  ...providerConfigurationShape,
  isEnabled: z.boolean(),
});

export const createRotatePaymentProviderSchema = (
  providerName: string,
) =>
  z
    .object({
      apiKey: optionalText(8192),
      webhookHmacKey: optionalText(8192),
      tokenHmacKey: optionalText(8192),
    })
    .superRefine((value, context) => {
      if (
        !value.apiKey &&
        !value.webhookHmacKey &&
        !value.tokenHmacKey
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["apiKey"],
          message: "Enter at least one credential to rotate.",
        });
      }

      if (providerName === "ADYEN-ONLINE") {
        (
          [
            ["webhookHmacKey", value.webhookHmacKey],
            ["tokenHmacKey", value.tokenHmacKey],
          ] as const
        ).forEach(([field, secret]) => {
          if (secret && !isAdyenHmac(secret)) {
            context.addIssue({
              code: z.ZodIssueCode.custom,
              path: [field],
              message: "Use a 64-character hexadecimal Adyen HMAC key.",
            });
          }
        });

        return;
      }

      if (
        value.apiKey &&
        !value.apiKey.startsWith("sk_") &&
        !value.apiKey.startsWith("rk_")
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["apiKey"],
          message: "Stripe API keys start with sk_ or rk_.",
        });
      }

      if (
        value.webhookHmacKey &&
        !value.webhookHmacKey.startsWith("whsec_")
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["webhookHmacKey"],
          message: "Stripe endpoint secrets start with whsec_.",
        });
      }

      if (value.tokenHmacKey) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["tokenHmacKey"],
          message: "Stripe does not use a separate token-webhook secret.",
        });
      }
    });

export type RegisterPaymentProviderFormValues = z.infer<
  typeof registerPaymentProviderSchema
>;

export type UpdatePaymentProviderFormValues = z.infer<
  typeof updatePaymentProviderSchema
>;

export type RotatePaymentProviderFormValues = {
  apiKey?: string;
  webhookHmacKey?: string;
  tokenHmacKey?: string;
};

export const providerDisplayName = (
  providerName: string,
): string =>
  providerName === "ADYEN-ONLINE"
    ? "Adyen Hosted Checkout"
    : providerName === "STRIPE"
      ? "Stripe Checkout"
      : providerName;

export const isKnownPaymentProvider = (
  value: string,
): value is PaymentProviderName =>
  value === "ADYEN-ONLINE" || value === "STRIPE";
