import { z } from "zod";

export const activationFormDefaultValue = {
  firstname: "",
  lastname: "",
  password: "",
  confirmPassword: "",
};

const hasWhitespace = /\s/;
const noWhitespaceMessage = "Password must not contain spaces";

const passwordSchema = z
  .string()
  .superRefine((value, ctx) => {
    if (value && hasWhitespace.test(value)) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message: noWhitespaceMessage });
    }
  })
  .transform((value) => value.trim());

export const activationFormSchema = z.object({
  firstname: z.string().min(1, "First name is required"),
  lastname: z.string().min(1, "Last name is required"),
  password: passwordSchema,
  confirmPassword: passwordSchema,
});

