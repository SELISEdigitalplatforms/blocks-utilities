import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";

export const signinFormDefaultValue = {
  username: "",
  password: "",
};

export const createSigninFormSchema = () => z.object({
  username: z.string().min(1, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SCHEMA.EMAIL_IS_REQUIRED")).email(tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SCHEMA.INVALID_EMAIL_FORMAT")),
  password: z.string().min(1, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SCHEMA.PASSWORD_IS_REQUIRED")),
});
