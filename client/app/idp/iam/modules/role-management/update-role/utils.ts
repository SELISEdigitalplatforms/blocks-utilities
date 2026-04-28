import { z } from "zod";

export const updateRoleFormDefaultValue = {
  name: "",
  description: "",
};

export const updateRoleFormSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Name field must not be empty")
    .max(50, "Name must be at most 50 characters"),
  description: z.string().trim().max(150, "Description must be at most 150 characters").optional(),
});
