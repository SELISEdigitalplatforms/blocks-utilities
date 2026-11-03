import { tStatic } from "@seliseblocks/genesis-os";
import { z } from "zod";
import { zod3Messages } from "@/lib/zod3-messages";
import { PAYMENT_PROVIDER_NAMES } from "../models/payment-provider.model";

const UTILITIES_PAYMENT_MODULE = "utilities.payment";

export const createPaymentSchema = () =>
  z.object({
  providerName: z.enum(PAYMENT_PROVIDER_NAMES),
  amount: z
    .number({
      error: (issue) =>
        issue.input === undefined
          ? tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.AMOUNT_REQUIRED")
          : tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.VALID_AMOUNT"),
    })
    .positive(tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.AMOUNT_POSITIVE"))
    .max(999_999_999, tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.AMOUNT_TOO_LARGE")),
  currencyCode: z
    .string()
    .trim()
    .length(3, tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.CURRENCY_THREE_LETTERS"))
    .regex(/^[A-Za-z]{3}$/, tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.CURRENCY_LETTERS_ONLY"))
    .transform((value) => value.toUpperCase()),
  orderId: z
    .string()
    .trim()
    .min(1, tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.ORDER_ID_REQUIRED"))
    .max(80, tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.ORDER_ID_TOO_LONG")),
  rememberCard: z.boolean(),
  isRecurring: z.literal(false),
  // Blank means "use whichever organization my context carries", which is what every
  // payment did before this field existed.
  // zod 3's default message, kept so the form reads the same after the zod 4 upgrade.
  organizationId: z
    .string()
    .trim()
    .max(200, zod3Messages.stringMax(200))
    .optional()
    .or(z.literal("")),
});
