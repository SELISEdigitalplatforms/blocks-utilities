import { z } from "zod";

export const authConfigFormDefaultValues = {
  refreshTokenValidForNumberMinutes: 0,
  getNumberOfWrongAttemptsToLockTheAccount: 0,
  accountLockDurationInMinutes: 0,
  accessTokenValidForNumberMinutes: 0,
  rememberMeRefreshTokenValidForNumberMinutes: 0,
};

// Number.isInteger, not .int(): zod 4's .int() also rejects unsafe integers and skips the checks
// after it; zod 3's did neither.
export const authConfigFormSchema = z.object({
  refreshTokenValidForNumberMinutes: z.coerce
    .number<number>()
    .refine(Number.isInteger, "Must be a whole number.")
    .positive({ message: "Must be a positive number." })
    .max(2147483647, {
      message: "Value exceeds the allowed limit (1 - 2,147,483,647).",
    }),
  getNumberOfWrongAttemptsToLockTheAccount: z.coerce
    .number<number>()
    .refine(Number.isInteger, "Must be a whole number.")
    .positive({ message: "Must be a positive number." })
    .max(2147483647, {
      message: "Value exceeds the allowed limit (1 - 2,147,483,647).",
    }),
  accountLockDurationInMinutes: z.coerce
    .number<number>()
    .refine(Number.isInteger, "Must be a whole number.")
    .positive({ message: "Must be a positive number." })
    .max(2147483647, {
      message: "Value exceeds the allowed limit (1 - 2,147,483,647).",
    }),
  accessTokenValidForNumberMinutes: z.coerce
    .number<number>()
    .refine(Number.isInteger, "Must be a whole number.")
    .positive({ message: "Must be a positive number." })
    .max(2147483647, {
      message: "Value exceeds the allowed limit (1 - 2,147,483,647).",
    }),
  rememberMeRefreshTokenValidForNumberMinutes: z.coerce
    .number<number>()
    .refine(Number.isInteger, "Must be a whole number.")
    .positive({ message: "Must be a positive number." })
    .max(2147483647, {
      message: "Value exceeds the allowed limit (1 - 2,147,483,647).",
    }),
});
