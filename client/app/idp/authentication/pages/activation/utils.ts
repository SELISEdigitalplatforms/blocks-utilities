import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";

export const activationFormDefaultValue = {
  firstname: "",
  lastname: "",
  password: "",
  confirmPassword: "",
};

const hasWhitespace = /\s/;

const passwordSchema = z
  .string()
  .superRefine((value, ctx) => {
    if (value && hasWhitespace.test(value)) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.PASSWORD_MUST_NOT_CONTAIN_SPACES") });
    }
  })
  .transform((value) => value.trim());

export const createActivationFormSchema = () => z.object({
  firstname: z.string().min(1, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.FIRST_NAME_IS_REQUIRED")),
  lastname: z.string().min(1, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.LAST_NAME_IS_REQUIRED")),
  password: passwordSchema,
  confirmPassword: passwordSchema,
});

