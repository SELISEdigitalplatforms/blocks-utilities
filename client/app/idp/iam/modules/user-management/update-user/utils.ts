import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const inviteUserFormDefaultValue = {
  firstName: "",
  lastName: "",
};

export const createInviteUserFormSchema = () => z.object({
  firstName: z
    .string()
    .trim()
    .min(1, tStatic("utilities.idp", "UTILS.FIRST_NAME_IS_REQUIRED"))
    .max(150, tStatic("utilities.idp", "UTILS.FIRST_NAME_MUST_BE_AT")),
  lastName: z
    .string()
    .trim()
    .min(1, tStatic("utilities.idp", "UTILS.LAST_NAME_IS_REQUIRED"))
    .max(150, tStatic("utilities.idp", "UTILS.LAST_NAME_MUST_BE_AT")),
});
