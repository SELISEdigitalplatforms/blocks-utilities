import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const makeClientSchema = () => z.object({
  clientNameService: z.string().trim().min(1, tStatic("utilities.idp.authentication", "UTILS.CLIENT_NAME_IS_REQUIRED")),
  audienceUrlService: z.string().trim().url(),
  roles: z.array(z.string().trim()),
});

export type CreateClientModalFormValues = z.infer<ReturnType<typeof makeClientSchema>>;

export const CreateClientModalFormDefaultValues: CreateClientModalFormValues = {
  clientNameService: "",
  audienceUrlService: "",
  roles: [],
};
