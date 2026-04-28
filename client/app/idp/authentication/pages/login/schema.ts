import { z } from "zod";

export const signinFormDefaultValue = {
  username: "",
  password: "",
};

export const signinFormSchema = z.object({
  username: z.string().min(1, "Email is required").email("Invalid email format"),
  password: z.string().min(1, "Password is required"),
});
