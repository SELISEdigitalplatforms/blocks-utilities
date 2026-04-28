import { z } from "zod";

export const createClientSchema = z.object({
  clientNameService: z.string().trim().min(1, "Client name is required"),
  audienceUrlService: z.string().trim().url(),
  roles: z.array(z.string().trim()),
});

export type CreateClientModalFormValues = z.infer<typeof createClientSchema>;

export const CreateClientModalFormDefaultValues: CreateClientModalFormValues = {
  clientNameService: "",
  audienceUrlService: "",
  roles: [],
};
