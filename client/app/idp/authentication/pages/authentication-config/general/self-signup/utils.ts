import { z } from "zod";

export const selfSignUpFormDefaultValues = {
  isSelfSignUpAllowed: false,
};

export const selfSignUpFormSchema = z.object({
  isSelfSignUpAllowed: z.boolean(),
});

export type SelfSignUpFormType = z.infer<typeof selfSignUpFormSchema>;
