import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";
const UTILS_MUST_BE_A_WHOLE_NUMBER = "UTILS.MUST_BE_A_WHOLE_NUMBER";
const UTILS_MUST_BE_A_POSITIVE_NUMBER = "UTILS.MUST_BE_A_POSITIVE_NUMBER";
const UTILS_VALUE_EXCEEDS_THE_ALLOWED_LIMIT = "UTILS.VALUE_EXCEEDS_THE_ALLOWED_LIMIT";

export const authConfigFormDefaultValues = {
  refreshTokenValidForNumberMinutes: 0,
  getNumberOfWrongAttemptsToLockTheAccount: 0,
  accountLockDurationInMinutes: 0,
  accessTokenValidForNumberMinutes: 0,
  rememberMeRefreshTokenValidForNumberMinutes: 0,
};

// Number.isInteger, not .int(): zod 4's .int() also rejects unsafe integers and skips the checks
// after it; zod 3's did neither.
export const createAuthConfigFormSchema = () => z.object({
  refreshTokenValidForNumberMinutes: z.coerce
    .number<number>()
    .refine(Number.isInteger, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_WHOLE_NUMBER))
    .positive({ message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_POSITIVE_NUMBER) })
    .max(2147483647, {
      message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_VALUE_EXCEEDS_THE_ALLOWED_LIMIT),
    }),
  getNumberOfWrongAttemptsToLockTheAccount: z.coerce
    .number<number>()
    .refine(Number.isInteger, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_WHOLE_NUMBER))
    .positive({ message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_POSITIVE_NUMBER) })
    .max(2147483647, {
      message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_VALUE_EXCEEDS_THE_ALLOWED_LIMIT),
    }),
  accountLockDurationInMinutes: z.coerce
    .number<number>()
    .refine(Number.isInteger, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_WHOLE_NUMBER))
    .positive({ message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_POSITIVE_NUMBER) })
    .max(2147483647, {
      message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_VALUE_EXCEEDS_THE_ALLOWED_LIMIT),
    }),
  accessTokenValidForNumberMinutes: z.coerce
    .number<number>()
    .refine(Number.isInteger, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_WHOLE_NUMBER))
    .positive({ message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_POSITIVE_NUMBER) })
    .max(2147483647, {
      message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_VALUE_EXCEEDS_THE_ALLOWED_LIMIT),
    }),
  rememberMeRefreshTokenValidForNumberMinutes: z.coerce
    .number<number>()
    .refine(Number.isInteger, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_WHOLE_NUMBER))
    .positive({ message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_MUST_BE_A_POSITIVE_NUMBER) })
    .max(2147483647, {
      message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, UTILS_VALUE_EXCEEDS_THE_ALLOWED_LIMIT),
    }),
});
