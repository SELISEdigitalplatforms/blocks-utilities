import { z } from "zod";
import { PAYMENT_PROVIDER_NAMES } from "../models/payment-provider.model";

export const createPaymentSchema = z.object({
  providerName: z.enum(PAYMENT_PROVIDER_NAMES),
  amount: z
    .number({
      required_error: "Amount is required.",
      invalid_type_error: "Enter a valid amount.",
    })
    .positive("Amount must be greater than zero.")
    .max(999_999_999, "Amount is above the supported limit."),
  currencyCode: z
    .string()
    .trim()
    .length(3, "Currency must contain exactly three letters.")
    .regex(/^[A-Za-z]{3}$/, "Currency must contain letters only.")
    .transform((value) => value.toUpperCase()),
  orderId: z
    .string()
    .trim()
    .min(1, "Order ID is required.")
    .max(80, "Order ID cannot exceed 80 characters."),
  rememberCard: z.boolean(),
  isRecurring: z.literal(false),
  // Blank means "use whichever organization my context carries", which is what every
  // payment did before this field existed.
  organizationId: z.string().trim().max(200).optional().or(z.literal("")),
});
