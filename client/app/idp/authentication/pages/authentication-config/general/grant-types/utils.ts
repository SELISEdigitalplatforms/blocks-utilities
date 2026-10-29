import { z } from "zod";
import { zod3Messages } from "@/lib/zod3-messages";

export const authGrantTypeFormDefaultValues = {
  allowedGrantTypes: [],
};

export const authGrantTypeFormSchema = z.object({
  // zod 3's default message, kept so the form's errors read the same after the zod 4 upgrade.
  allowedGrantTypes: z.array(z.string()).min(1, zod3Messages.arrayMin(1)),
});

export type authGrantTypeFormType = z.infer<typeof authGrantTypeFormSchema>;
