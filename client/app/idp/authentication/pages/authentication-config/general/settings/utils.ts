import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const authConfigFormDefaultValues = {
  refreshTokenValidForNumberMinutes: 0,
  getNumberOfWrongAttemptsToLockTheAccount: 0,
  accountLockDurationInMinutes: 0,
  accessTokenValidForNumberMinutes: 0,
  rememberMeRefreshTokenValidForNumberMinutes: 0,
};

export const createAuthConfigFormSchema = () => z.object({
  refreshTokenValidForNumberMinutes: z.coerce
    .number()
    .int({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_WHOLE_NUMBER") })
    .positive({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_POSITIVE_NUMBER") })
    .max(2147483647, {
      message: tStatic("utilities.idp.authentication", "UTILS.VALUE_EXCEEDS_THE_ALLOWED_LIMIT"),
    }),
  getNumberOfWrongAttemptsToLockTheAccount: z.coerce
    .number()
    .int({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_WHOLE_NUMBER") })
    .positive({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_POSITIVE_NUMBER") })
    .max(2147483647, {
      message: tStatic("utilities.idp.authentication", "UTILS.VALUE_EXCEEDS_THE_ALLOWED_LIMIT"),
    }),
  accountLockDurationInMinutes: z.coerce
    .number()
    .int({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_WHOLE_NUMBER") })
    .positive({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_POSITIVE_NUMBER") })
    .max(2147483647, {
      message: tStatic("utilities.idp.authentication", "UTILS.VALUE_EXCEEDS_THE_ALLOWED_LIMIT"),
    }),
  accessTokenValidForNumberMinutes: z.coerce
    .number()
    .int({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_WHOLE_NUMBER") })
    .positive({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_POSITIVE_NUMBER") })
    .max(2147483647, {
      message: tStatic("utilities.idp.authentication", "UTILS.VALUE_EXCEEDS_THE_ALLOWED_LIMIT"),
    }),
  rememberMeRefreshTokenValidForNumberMinutes: z.coerce
    .number()
    .int({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_WHOLE_NUMBER") })
    .positive({ message: tStatic("utilities.idp.authentication", "UTILS.MUST_BE_A_POSITIVE_NUMBER") })
    .max(2147483647, {
      message: tStatic("utilities.idp.authentication", "UTILS.VALUE_EXCEEDS_THE_ALLOWED_LIMIT"),
    }),
});
