import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const forgotPasswordFormDefaultValue = {
  email: "",
};

export const createForgotPasswordFormSchema = () => z.object({
  email: z.string().trim().email({ message: tStatic("utilities.idp.authentication", "UTILS.INVALID_EMAIL") }),
});
