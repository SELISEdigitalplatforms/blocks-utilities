import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_SHARED_MODULE = "utilities.shared";

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
    .min(1, tStatic(UTILITIES_SHARED_MODULE, "CREATE_PROJECT_NAMING_FORM.NAME_REQUIRED"))
    .min(3, tStatic(UTILITIES_SHARED_MODULE, "CREATE_PROJECT_NAMING_FORM.NAME_MIN_LENGTH"))
    .max(100, tStatic(UTILITIES_SHARED_MODULE, "CREATE_PROJECT_NAMING_FORM.NAME_MAX_LENGTH")),
  isAcceptBlocksTerms: z.boolean().refine((val) => val),
  isUseBlocksExclusively: z.boolean().refine((val) => val),
});
