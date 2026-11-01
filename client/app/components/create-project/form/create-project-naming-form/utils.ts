import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const createProjectNamingFormDefaultValue = {
  name: "",
  isAcceptBlocksTerms: false,
  isUseBlocksExclusively: false,
};

export const createProjectNamingFormSchema = () =>
  z.object({
  name: z
    .string()
    .trim()
    .min(1, tStatic("utilities.shared", "CREATE_PROJECT_NAMING_FORM.NAME_REQUIRED"))
    .min(3, tStatic("utilities.shared", "CREATE_PROJECT_NAMING_FORM.NAME_MIN_LENGTH"))
    .max(100, tStatic("utilities.shared", "CREATE_PROJECT_NAMING_FORM.NAME_MAX_LENGTH")),
  isAcceptBlocksTerms: z.boolean().refine((val) => val),
  isUseBlocksExclusively: z.boolean().refine((val) => val),
});
