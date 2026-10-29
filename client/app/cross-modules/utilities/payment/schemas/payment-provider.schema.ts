import { z } from "zod";
import { zod3Messages } from "@/lib/zod3-messages";
import type { PaymentProviderName } from "../models/payment-provider.model";

// The checks without a message of their own carry zod 3's default one, so the forms read the same
// after the zod 4 upgrade.

const optionalText = (maximumLength: number) =>
  z
    .string()
    .trim()
    .max(maximumLength, zod3Messages.stringMax(maximumLength))
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
  // Held as text so "left blank" (unknown) and "0" (captures at once) stay distinct; a number
  // input cannot tell them apart once emptied. Converted by parseCaptureDelayHours on submit.
  captureDelayHours: z
    .string()
    .trim()
    .refine(
      (value) =>
        value.length === 0 ||
        (/^\d+$/.test(value) && Number(value) <= 168),
      "Use a whole number of hours from 0 to 168.",
    )
    .default(""),
  // Number.isInteger, not .int(): zod 4's .int() also rejects unsafe integers and skips the checks
  // after it; zod 3's did neither. The form sends valueAsNumber, so an emptied input is NaN.
  maxRefundDays: z.coerce
    .number<number>({ error: zod3Messages.notANumber })
    .refine(Number.isInteger, "Use a whole number.")
    .min(0, zod3Messages.numberMin(0))
    .max(3650, zod3Messages.numberMax(3650)),
  storeId: optionalText(200),
  // Loose on purpose. The list is authored through a fixed set of checkboxes, so nothing unknown
  // can be added here — but a provider configured through the API may already hold a method this
  // form does not offer, and rejecting it would leave that provider unsaveable from the portal.
  // The cap mirrors the organizationIds convention: comfortably above anything real, so an
  // oversized list is reported in the form rather than as a request that fails.
  // A refine, not .max(): zod 4's .max() stops at an oversized list without checking its items or
  // running the superRefine below, so the form would hide every other error; zod 3's did neither.
  checkoutPaymentMethodTypes: z
    .array(
      z.string().trim().min(1, zod3Messages.stringMin(1)).max(100, zod3Messages.stringMax(100)),
    )
    .refine((types) => types.length <= 50, zod3Messages.arrayMax(50))
    .default([]),
  // A typo guard the server does not have: neither of these two fields is validated server-side,
  // so a mistyped id would otherwise reach Stripe and fail there, at checkout, for a shopper.
  paymentMethodConfigurationId: z
    .string()
    .trim()
    .max(100, zod3Messages.stringMax(100))
    .refine(
      (value) => value.length === 0 || value.startsWith("pmc_"),
      "A Stripe payment method configuration id starts with pmc_.",
    )
    .optional()
    .or(z.literal("")),
};

/** The form's text value as the request field: blank means "not set", so it is left out. */
export const parseCaptureDelayHours = (value: string | undefined) =>
  value === undefined || value.trim().length === 0
    ? undefined
    : Number(value);

const isAdyenHmac =(value: string) => /^[0-9A-Fa-f]{64}$/.test(value);

export const registerPaymentProviderSchema = z
  .object({
    providerName: z.enum(["ADYEN-ONLINE", "STRIPE"]),
    merchantId: z
      .string()
      .trim()
      .min(1, zod3Messages.stringMin(1))
      .max(200, zod3Messages.stringMax(200)),
    // Blank means "use whichever organization my context carries", which is what every
    // registration did before this field existed.
    organizationId: optionalText(200),
    // Several organizations configured identically in one submission. Empty is the ordinary
    // case and falls back to organizationId above. Capped to match the server, so an oversized
    // selection is reported in the form rather than as a request that fails.
    // A refine, not .max(): zod 4's .max() stops at an oversized list without checking its items or
    // running the superRefine below, so the form would hide every other error; zod 3's did neither.
    organizationIds: z
      .array(
        z.string().trim().min(1, zod3Messages.stringMin(1)).max(200, zod3Messages.stringMax(200)),
      )
      .refine((ids) => ids.length <= 50, zod3Messages.arrayMax(50))
      .default([]),
    ...providerConfigurationShape,
    apiBaseUrl: optionalText(500),
    apiKey: z
      .string()
      .trim()
      .min(1, zod3Messages.stringMin(1))
      .max(8192, zod3Messages.stringMax(8192)),
    webhookHmacKey: z
      .string()
      .trim()
      .min(1, zod3Messages.stringMin(1))
      .max(8192, zod3Messages.stringMax(8192)),
    tokenHmacKey: optionalText(8192),
  })
  .superRefine((value, context) => {
    if (value.providerName === "ADYEN-ONLINE") {
      if (!value.apiBaseUrl) {
        context.addIssue({
          code: "custom",
          path: ["apiBaseUrl"],
          message: "Adyen requires its Checkout API base URL.",
        });
      }

      if (!isAdyenHmac(value.webhookHmacKey)) {
        context.addIssue({
          code: "custom",
          path: ["webhookHmacKey"],
          message: "Use the 64-character hexadecimal Adyen HMAC key.",
        });
      }

      if (!value.tokenHmacKey || !isAdyenHmac(value.tokenHmacKey)) {
        context.addIssue({
          code: "custom",
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
        code: "custom",
        path: ["apiKey"],
        message: "Stripe API keys start with sk_ or rk_.",
      });
    }

    if (!value.webhookHmacKey.startsWith("whsec_")) {
      context.addIssue({
        code: "custom",
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
          code: "custom",
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
              code: "custom",
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
          code: "custom",
          path: ["apiKey"],
          message: "Stripe API keys start with sk_ or rk_.",
        });
      }

      if (
        value.webhookHmacKey &&
        !value.webhookHmacKey.startsWith("whsec_")
      ) {
        context.addIssue({
          code: "custom",
          path: ["webhookHmacKey"],
          message: "Stripe endpoint secrets start with whsec_.",
        });
      }

      if (value.tokenHmacKey) {
        context.addIssue({
          code: "custom",
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
