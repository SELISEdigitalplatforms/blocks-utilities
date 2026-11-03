import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";
import { isParsableUrl } from "@/lib/url-format";

export const makeClientSchema = () => z.object({
  clientNameService: z.string().trim().min(1, tStatic("utilities.idp.authentication", "UTILS.CLIENT_NAME_IS_REQUIRED")),
  // zod 3's default message, kept so the form reads the same after the zod 4 upgrade.
  audienceUrlService: z.string().trim().refine(isParsableUrl, "Invalid url"),
  roles: z.array(z.string().trim()),
});

export type CreateClientModalFormValues = z.infer<ReturnType<typeof makeClientSchema>>;

export const CreateClientModalFormDefaultValues: CreateClientModalFormValues = {
  clientNameService: "",
  audienceUrlService: "",
  roles: [],
};
