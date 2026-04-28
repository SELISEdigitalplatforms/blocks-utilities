import { z } from "zod";

export const addRoleFormDefaultValue = {
  name: "",
  slug: "",
  description: "",
};

export const addRoleFormSchema = z.object({
  name: z.string().min(1, "Name is required").max(50, "Name must be at most 50 characters").trim(),
  slug: z
    .string()
    .min(1, "Slug is required")
    .max(50, "Slug must be at most 50 characters")
    .trim()
    .refine((s) => !s.includes(" "), "Slug can not contain spaces"),
  description: z.string().max(150, "Description must be at most 150 characters").trim().optional(),
});
