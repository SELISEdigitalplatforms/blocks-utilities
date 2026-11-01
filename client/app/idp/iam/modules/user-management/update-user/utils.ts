import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_MODULE = "utilities.idp";

export const inviteUserFormDefaultValue = {
  firstName: "",
  lastName: "",
};

export const createInviteUserFormSchema = () => z.object({
  firstName: z
    .string()
    .trim()
    .min(1, tStatic(UTILITIES_IDP_MODULE, "UTILS.FIRST_NAME_IS_REQUIRED"))
    .max(150, tStatic(UTILITIES_IDP_MODULE, "UTILS.FIRST_NAME_MUST_BE_AT")),
  lastName: z
    .string()
    .trim()
    .min(1, tStatic(UTILITIES_IDP_MODULE, "UTILS.LAST_NAME_IS_REQUIRED"))
    .max(150, tStatic(UTILITIES_IDP_MODULE, "UTILS.LAST_NAME_MUST_BE_AT")),
});
