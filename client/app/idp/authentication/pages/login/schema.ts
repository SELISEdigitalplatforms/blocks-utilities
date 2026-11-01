import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const signinFormDefaultValue = {
  username: "",
  password: "",
};

export const createSigninFormSchema = () => z.object({
  username: z.string().min(1, tStatic("utilities.idp.authentication", "SCHEMA.EMAIL_IS_REQUIRED")).email(tStatic("utilities.idp.authentication", "SCHEMA.INVALID_EMAIL_FORMAT")),
  password: z.string().min(1, tStatic("utilities.idp.authentication", "SCHEMA.PASSWORD_IS_REQUIRED")),
});
