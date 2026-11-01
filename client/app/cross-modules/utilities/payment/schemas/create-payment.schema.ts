import { tStatic } from "@seliseblocks/genesis-os";
import { z } from "zod";
import { PAYMENT_PROVIDER_NAMES } from "../models/payment-provider.model";

const UTILITIES_PAYMENT_MODULE = "utilities.payment";

export const createPaymentSchema = () =>
  z.object({
  providerName: z.enum(PAYMENT_PROVIDER_NAMES),
  amount: z
    .number({
      required_error: tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.AMOUNT_REQUIRED"),
      invalid_type_error: tStatic(UTILITIES_PAYMENT_MODULE, "CREATE_PAYMENT_SCHEMA.VALID_AMOUNT"),
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
  organizationId: z.string().trim().max(200).optional().or(z.literal("")),
});
