import { z } from "zod";

export const forgotPasswordFormDefaultValue = {
  email: "",
};

export const forgotPasswordFormSchema = z.object({
  email: z.string().trim().check(z.email("Invalid email")),
});
