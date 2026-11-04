import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";

export const activationFormDefaultValue = {
  password: "",
  confirmPassword: "",
};

const passwordRegex =
  /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&;[\]{}|:(),.])[A-Za-z\d@$!%*?&;[\]{}|:(),.]{8,30}$/;

export const createActivationFormSchema = () => z
  .object({
    // "Invalid" is zod 3's default regex message, kept so the form reads the same after the
    // zod 4 upgrade.
    password: z
      .string()
      .min(8, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.PASSWORD_MUST_BE_AT_LEAST"))
      .regex(passwordRegex, "Invalid"),
    confirmPassword: z.string().min(8, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.CONFIRM_PASSWORD_MUST_BE_AT")),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.PASSWORDS_MUST_BE_MATCHED"),
    path: ["confirmPassword"],
  });
