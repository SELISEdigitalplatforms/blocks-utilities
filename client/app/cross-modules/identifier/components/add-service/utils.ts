import { z } from "zod";

export const addServiceSchema = z.object({
  serviceName: z
    .string()
    .trim()
    .min(1, "Service name is required")
    .max(100, "Service name too long. Maximum 100 characters allowed."),
  tags: z
    .string()
    .array()
    .refine((tags) => new Set(tags).size === tags.length, "Duplicate tags are not allowed"),
  serviceType: z.enum(["frontend", "backend"]).default("frontend"),
  description: z.string().optional(),
});

export type AddServiceForm = z.infer<typeof addServiceSchema>;

export const addServiceDefaultValues: AddServiceForm = {
  serviceName: "",
  tags: [],
  description: "",
  serviceType: "frontend",
};
