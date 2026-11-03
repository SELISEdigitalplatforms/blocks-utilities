import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const signupFormDefaultValue = {
  email: "",
};

export const createSignupFormSchema = () => z.object({
  email: z.string().trim().check(z.email(tStatic("utilities.idp.authentication", "UTILS.INVALID_EMAIL"))),
});
