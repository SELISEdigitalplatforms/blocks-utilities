import { z } from "zod";

export const activationFormDefaultValue = {
  password: "",
  confirmPassword: "",
};

const passwordRegex =
  /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&;[\]{}|:(),.])[A-Za-z\d@$!%*?&;[\]{}|:(),.]{8,30}$/;

export const activationFormSchema = z
  .object({
    password: z.string().min(8, "Password must be at least 8 characters long").regex(passwordRegex),
    confirmPassword: z.string().min(8, "Confirm password must be at least 8 characters long"),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: "Passwords must be matched",
    path: ["confirmPassword"],
  });
