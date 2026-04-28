import { z } from "zod";

export const storageConfigurationFormSchema = z
  .object({
    name: z.string().nonempty("Name is required").trim(),
    storageStrategy: z.enum(["Amazon", "Azure", "SftpStorage", "S3Compatible"]),
    secretKey: z.string().trim().nullable(),
    accessKey: z.string().trim().nullable(),
    cloudStorageRegionEndPoint: z.string().trim().nullable(),
    connectionString: z.string().trim().nullable(),
    host: z.string().trim().nullable(),
    port: z
      .string()
      .trim()
      .pipe(
        z.coerce
          .number()
          .min(0)
          .transform((arg) => arg.toString()),
      )
      .nullable(),
    userName: z.string().trim().nullable(),
    password: z.string().trim().nullable(),
    remoteBasePath: z.string().trim().nullable(),
  })
  .superRefine((data, ctx) => {
    const { storageStrategy } = data;

    const requireFields = (fields: (keyof typeof data)[], messages: Record<string, string>) => {
      fields.forEach((field) => {
        if (!data[field]) {
          ctx.addIssue({
            path: [field],
            code: z.ZodIssueCode.custom,
            message: messages[field] || `${field} is required`,
          });
        }
      });
    };

    if (storageStrategy === "Amazon") {
      requireFields(["secretKey", "accessKey", "cloudStorageRegionEndPoint"], {
        secretKey: "Secret key is required",
        accessKey: "Access key is required",
        cloudStorageRegionEndPoint: "Region endpoint is required",
      });
    }

    if (storageStrategy === "Azure") {
      requireFields(["connectionString"], {
        connectionString: "Connection string is required",
      });
    }

    if (storageStrategy === "SftpStorage") {
      requireFields(["host", "port", "userName", "password", "remoteBasePath"], {
        host: "Host is required",
        port: "Port is required",
        userName: "Username is required",
        password: "Password is required",
        remoteBasePath: "Remote base path is required",
      });
    }

    if (storageStrategy === "S3Compatible") {
      requireFields(["accessKey", "secretKey", "host"], {
        accessKey: "Access key is required",
        secretKey: "Secret key is required",
        host: "Host URL is required",
      });
    }
  });

export type StorageConfigurationFormValues = z.infer<typeof storageConfigurationFormSchema>;

export const storageConfigurationFormDefaultValue: StorageConfigurationFormValues = {
  name: "",
  storageStrategy: "Amazon",
  secretKey: "",
  accessKey: "",
  cloudStorageRegionEndPoint: "",
  connectionString: "",
  remoteBasePath: "",
  host: null,
  port: "",
  userName: "",
  password: "",
};
