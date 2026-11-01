import { z } from "zod";
import type { PaymentProviderName } from "../models/payment-provider.model";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_PAYMENT_MODULE = "utilities.payment";

const optionalText = (maximumLength: number) =>
  z
    .string()
    .trim()
    .max(maximumLength)
    .optional()
    .or(z.literal(""));

const publicHttpsUrl = () =>
  z
  .string()
  .trim()
  .min(1, tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.ENTER_FRONTEND_RESULT_URL"))
  .refine((value) => {
    try {
      const url = new URL(value);
      return url.protocol === "https:" && Boolean(url.hostname);
    } catch {
      return false;
    }
  }, tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.ENTER_ABSOLUTE_HTTPS_URL"));

const providerConfigurationShape = () => ({
  frontendResultUrl: publicHttpsUrl(),
  countryCode: z
    .string()
    .trim()
    .refine(
      (value) => value.length === 0 || /^[A-Za-z]{2}$/.test(value),
      tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.TWO_LETTER_COUNTRY_CODE"),
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
      tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.CAPTURE_DELAY_HOURS"),
    )
    .default(""),
  maxRefundDays: z.coerce
    .number()
    .int(tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.WHOLE_NUMBER"))
    .min(0)
    .max(3650),
  storeId: optionalText(200),
  // Loose on purpose. The list is authored through a fixed set of checkboxes, so nothing unknown
  // can be added here — but a provider configured through the API may already hold a method this
  // form does not offer, and rejecting it would leave that provider unsaveable from the portal.
  // The cap mirrors the organizationIds convention: comfortably above anything real, so an
  // oversized list is reported in the form rather than as a request that fails.
  checkoutPaymentMethodTypes: z
    .array(z.string().trim().min(1).max(100))
    .max(50)
    .default([]),
  // A typo guard the server does not have: neither of these two fields is validated server-side,
  // so a mistyped id would otherwise reach Stripe and fail there, at checkout, for a shopper.
  paymentMethodConfigurationId: z
    .string()
    .trim()
    .max(100)
    .refine(
      (value) => value.length === 0 || value.startsWith("pmc_"),
      tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.PMC_PREFIX"),
    )
    .optional()
    .or(z.literal("")),
});

/** The form's text value as the request field: blank means "not set", so it is left out. */
export const parseCaptureDelayHours = (value: string | undefined) =>
  value === undefined || value.trim().length === 0
    ? undefined
    : Number(value);

const isAdyenHmac =(value: string) => /^[0-9A-Fa-f]{64}$/.test(value);

export const createRegisterPaymentProviderSchema = () =>
  z
  .object({
    providerName: z.enum(["ADYEN-ONLINE", "STRIPE"]),
    merchantId: z.string().trim().min(1).max(200),
    // Blank means "use whichever organization my context carries", which is what every
    // registration did before this field existed.
    organizationId: optionalText(200),
    // Several organizations configured identically in one submission. Empty is the ordinary
    // case and falls back to organizationId above. Capped to match the server, so an oversized
    // selection is reported in the form rather than as a request that fails.
    organizationIds: z
      .array(z.string().trim().min(1).max(200))
      .max(50)
      .default([]),
    ...providerConfigurationShape(),
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
          message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.ADYEN_REQUIRES_ITS_CHECKOUT_API"),
        });
      }

      if (!isAdyenHmac(value.webhookHmacKey)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["webhookHmacKey"],
          message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.USE_THE_64_CHARACTER_HEXADECIMAL"),
        });
      }

      if (!value.tokenHmacKey || !isAdyenHmac(value.tokenHmacKey)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["tokenHmacKey"],
          message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.USE_THE_64_CHARACTER_HEXADECIMAL_2"),
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
        message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.STRIPE_API_KEYS_START_WITH"),
      });
    }

    if (!value.webhookHmacKey.startsWith("whsec_")) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["webhookHmacKey"],
        message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.STRIPE_ENDPOINT_SECRETS_START_WITH"),
      });
    }
  });

export const createUpdatePaymentProviderSchema = () =>
  z.object({
  ...providerConfigurationShape(),
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
          message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.ENTER_AT_LEAST_ONE_CREDENTIAL"),
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
              message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.USE_A_64_CHARACTER_HEXADECIMAL"),
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
          message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.STRIPE_API_KEYS_START_WITH"),
        });
      }

      if (
        value.webhookHmacKey &&
        !value.webhookHmacKey.startsWith("whsec_")
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["webhookHmacKey"],
          message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.STRIPE_ENDPOINT_SECRETS_START_WITH"),
        });
      }

      if (value.tokenHmacKey) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["tokenHmacKey"],
          message: tStatic(UTILITIES_PAYMENT_MODULE, "PAYMENT_PROVIDER_SCHEMA.STRIPE_DOES_NOT_USE_A"),
        });
      }
    });

export type RegisterPaymentProviderFormValues = z.infer<
  ReturnType<typeof createRegisterPaymentProviderSchema>
>;

export type UpdatePaymentProviderFormValues = z.infer<
  ReturnType<typeof createUpdatePaymentProviderSchema>
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
