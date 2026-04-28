import { z } from "zod";

export const signupFormDefaultValue = {
  email: "",
};

export const signupFormSchema = z.object({
  email: z.string().trim().email({ message: "Invalid email" }),
});
