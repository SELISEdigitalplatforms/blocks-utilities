import { PermissionSeverityLevel } from "@blocks-idp/iam/models/permission";
import { z } from "zod";

export const permissionFormDefaultValue: permissionFormSchemaType = {
  name: "",
  type: 0,
  tags: [],
  resource: "",
  resourceGroup: "",
  description: "",
  dependentPermissions: [],
  permissionSeverity: "" as unknown as PermissionSeverityLevel,
};

export const permissionFormSchema = z
  .object({
    name: z.string().min(1, "Name is required").max(50, "Name must be at most 50 characters").trim(),
    type: z.coerce.number().min(1, "Type is required"),
    resource: z
      .string()
      .min(1, "Resource is required")
      .max(50, "Resource must be at most 50 characters")
      .trim()
      .refine((s) => !s.includes(" "), "Resource can't contain spaces"),
    resourceGroup: z.string().nonempty("Group is required").trim(),
    tags: z.string().array(),
    description: z.string().max(150, "Description must be at most 150 characters"),
    dependentPermissions: z.array(z.string()),
    permissionSeverity: z.nativeEnum(PermissionSeverityLevel, {
      message: "Severity is required",
    }),
  })
  .refine(
    (arg) => {
      if (arg.type === 1) {
        const regex = /^[a-zA-Z0-9-]+::[a-zA-Z0-9-]+::[a-zA-Z0-9-]+$/;
        return regex.test(arg.resource);
      }
      return true;
    },
    {
      message: "Resource format should be service :: controller :: name",
      path: ["resource"],
    }
  );

export type permissionFormSchemaType = z.infer<typeof permissionFormSchema>;
