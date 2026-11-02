import { z } from "zod";
import { isParsableUrl } from "@/lib/url-format";

export const createClientSchema = z.object({
  clientNameService: z.string().trim().min(1, "Client name is required"),
  // zod 3's default message, kept so the form reads the same after the zod 4 upgrade.
  audienceUrlService: z.string().trim().refine(isParsableUrl, "Invalid url"),
  roles: z.array(z.string().trim()),
});

export type CreateClientModalFormValues = z.infer<typeof createClientSchema>;

export const CreateClientModalFormDefaultValues: CreateClientModalFormValues = {
  clientNameService: "",
  audienceUrlService: "",
  roles: [],
};
