import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";
import { isParsableUrl } from "@/lib/url-format";

export interface Asset {
  id?: number;
  name: string;
  html_url: string;
  full_name: string;
}

export const CreateProjectResourcesFormDefaultValue: { assets: Asset[] } = {
  assets: [],
};

export const CreateProjectResourcesFormSchema = () =>
  z.object({
  assets: z
    .array(
      z.object({
        id: z.number().optional(),
        name: z.string().min(1, tStatic("utilities.shared", "CREATE_PROJECT_RESOURCES_FORM.ASSET_NAME_REQUIRED")),
        html_url: z
          .string()
          .refine(isParsableUrl, tStatic("utilities.shared", "CREATE_PROJECT_RESOURCES_FORM.LINK_MUST_BE_VALID_URL")),
        full_name: z.string().optional(),
      }),
    )
    .optional(),
});
