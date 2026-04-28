import { z } from "zod";

export const authGrantTypeFormDefaultValues = {
  allowedGrantTypes: [],
};

export const authGrantTypeFormSchema = z.object({
  allowedGrantTypes: z.array(z.string()).min(1),
});

export type authGrantTypeFormType = z.infer<typeof authGrantTypeFormSchema>;
