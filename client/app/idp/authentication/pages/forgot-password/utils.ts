import { z } from "zod";

export const forgotPasswordFormDefaultValue = {
  email: "",
};

export const forgotPasswordFormSchema = z.object({
  email: z.string().trim().email({ message: "Invalid email" }),
});
