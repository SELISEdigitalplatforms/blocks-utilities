import { z } from "zod";

export const addOrganizationFormDefaultValue = {
  name: "",
};

export const addOrganizationFormSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Name is required")
    .max(100, "Name must be at most 100 characters"),
});
