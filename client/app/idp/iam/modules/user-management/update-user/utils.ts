import { z } from "zod";

export const inviteUserFormDefaultValue = {
  firstName: "",
  lastName: "",
};

export const inviteUserFormSchema = z.object({
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
