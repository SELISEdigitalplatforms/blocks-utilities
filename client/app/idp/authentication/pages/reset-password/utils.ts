import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const activationFormDefaultValue = {
  password: "",
  confirmPassword: "",
};

const passwordRegex =
  /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&;[\]{}|:(),.])[A-Za-z\d@$!%*?&;[\]{}|:(),.]{8,30}$/;

export const createActivationFormSchema = () => z
  .object({
    password: z.string().min(8, tStatic("utilities.idp.authentication", "UTILS.PASSWORD_MUST_BE_AT_LEAST")).regex(passwordRegex),
    confirmPassword: z.string().min(8, tStatic("utilities.idp.authentication", "UTILS.CONFIRM_PASSWORD_MUST_BE_AT")),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: tStatic("utilities.idp.authentication", "UTILS.PASSWORDS_MUST_BE_MATCHED"),
    path: ["confirmPassword"],
  });
