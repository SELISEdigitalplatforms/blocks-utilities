import { z } from "zod";

export const iamConfigFormDefaultValues = {
  accountActivationUrl: "",
  accountVerificationUrl: "",
  recoverAccountUrl: "",
  activationUrlLifetimeInMinutes: 1,
  recoverAccountUrlLifetimeInMinutes: 1,
  logoutOnPasswordChange: true,
};

export const iamConfigFormSchema = z.object({
  accountActivationUrl: z.string().url({ message: "Account activation URL must be a valid URL." }),
  accountVerificationUrl: z
    .string()
    .url({ message: "Account verification URL must be a valid URL." }),
  recoverAccountUrl: z.string().url({ message: "Recovery account URL must be a valid URL." }),
  activationUrlLifetimeInMinutes: z.coerce
    .number()
    .int({ message: "Must be a whole number." })
    .positive({ message: "Must be a positive number." })
    .max(2147483647, {
      message: "Value exceeds the allowed limit (1 - 2,147,483,647).",
    }),
  recoverAccountUrlLifetimeInMinutes: z.coerce
    .number()
    .int({ message: "Must be a whole number." })
    .positive({ message: "Must be a positive number." })
    .max(2147483647, {
      message: "Value exceeds the allowed limit (1 - 2,147,483,647).",
    }),

  logoutOnPasswordChange: z.boolean(),
});
