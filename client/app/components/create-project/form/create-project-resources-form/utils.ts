import { z } from "zod";

export interface Asset {
  id?: number;
  name: string;
  html_url: string;
  full_name: string;
}

export const CreateProjectResourcesFormDefaultValue: { assets: Asset[] } = {
  assets: [],
};

export const CreateProjectResourcesFormSchema = z.object({
  assets: z
    .array(
      z.object({
        id: z.number().optional(),
        name: z.string().min(1, "Asset name is required"),
        html_url: z.string().url("Link must be a valid URL"),
        full_name: z.string().optional(),
      }),
    )
    .optional(),
});
