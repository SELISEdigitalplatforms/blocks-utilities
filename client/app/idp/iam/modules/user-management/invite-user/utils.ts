import { z } from "zod";

export const inviteUserFormDefaultValue = {
  email: "",
  firstName: "",
  lastName: "",
};

export const inviteUserFormSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, "Email is required")
    .email({ message: "Please enter a valid email address" }),
  firstName: z
    .string()
    .trim()
    .min(1, "First name is required")
    .max(150, "First name must be at most 150 characters"),
  lastName: z
    .string()
    .trim()
    .min(1, "Last name is required")
    .max(150, "Last name must be at most 150 characters"),
});
