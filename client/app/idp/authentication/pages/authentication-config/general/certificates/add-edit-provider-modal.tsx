

import {
  FileInput,
  FileUploader,
  FileUploaderContent,
  FileUploaderItem,
} from "@/components/file-uploader/file-uploader";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { IGetPublicCertificateResponse } from "@blocks-identifier/models/project.model";
import { useProjectStore } from "@/store/useProjectStore";
import { providers } from "@blocks-idp/authentication/constants/authentication.constant";
import {
  useSavePublicCertificates,
  useValidateJwksUrl,
} from "@blocks-idp/authentication/hooks/use-identifier";
import { usePublicCertificateFile } from "@blocks-storage/hooks/use-storage-file";
import { zodResolver } from "@hookform/resolvers/zod";
import { Eye, Paperclip, Plus, Pencil, UploadCloud } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";

interface AddEditProviderModalProps {
  existingData?: IGetPublicCertificateResponse | null;
}

const formSchema = z.object({
  url: z.string().trim().optional().or(z.literal("")),
  password: z.string().trim().optional().or(z.literal("")),
  issuer: z.string().trim().optional().or(z.literal("")),
  audience: z.string().trim().optional().or(z.literal("")),
});

type FormData = z.infer<typeof formSchema>;

export const AddEditProviderModal = ({ existingData }: AddEditProviderModalProps) => {
  const projectKey = useProjectStore().selectedProject?.tenantId ?? "";

  const [open, setOpen] = useState(false);
  const [selectedProvider, setSelectedProvider] = useState("Keycloak");
  const [certificateMethod, setCertificateMethod] = useState("public-url");
  const [showPassword, setShowPassword] = useState(false);
  const [certificateFiles, setCertificateFiles] = useState<File[] | null>([]);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const { mutateAsync: uploadFileMutate } = usePublicCertificateFile();
  const { mutateAsync: savePublicCertificates } = useSavePublicCertificates();
  const { mutateAsync: validateJwksUrl } = useValidateJwksUrl();
  const form = useForm<FormData>({
    resolver: zodResolver(formSchema),
    mode: "onChange",
    defaultValues: {
      url: existingData?.jwksUrl || existingData?.publicCertificatePath || "",
      password: "",
      issuer: existingData?.issuer || "",
      audience: existingData?.audiences?.join(", ") || "",
    },
  });

  const resetForm = (data?: IGetPublicCertificateResponse | null) => {
    form.reset({
      url: data?.jwksUrl || data?.publicCertificatePath || "",
      password: "",
      issuer: data?.issuer || "",
      audience: data?.audiences?.join(", ") || "",
    });
    setCertificateMethod("public-url");
    setCertificateFiles([]);
    setShowPassword(false);
    setSelectedProvider(data?.providerName || "Keycloak");
  };

  const {
    register,
    formState: { errors, isDirty },
  } = form;

  // Update form when existingData changes
  useEffect(() => {
    if (existingData) {
      form.reset({
        url: existingData?.jwksUrl || existingData?.publicCertificatePath || "",
        password: "",
        issuer: existingData.issuer || "",
        audience: existingData.audiences?.join(", ") || "",
      });

      // Set provider based on existing data
      if (existingData.providerName) {
        setSelectedProvider(existingData.providerName);
      }

      // Always select public-url in edit mode
      setCertificateMethod("public-url");
    }
  }, [existingData, form]);

  // Reset certificate method to public-url when switching from "others" to another provider
  const prevProviderRef = useRef(selectedProvider);

  useEffect(() => {
    if (prevProviderRef.current === "Others" && selectedProvider !== "Others") {
      setCertificateMethod("public-url");
      setCertificateFiles([]);
    }
    prevProviderRef.current = selectedProvider;
  }, [selectedProvider]);

  const handleSubmit = async () => {
    // Validate based on certificate method first
    if (certificateMethod === "public-url") {
      const urlValue = form.getValues("url");

      // Skip URL validation if "Others" provider is selected
      if (selectedProvider !== "Others") {
        if (!urlValue || urlValue.trim().length === 0) {
          form.setError("url", { message: "JWKS URL is required" });
          return;
        }

        // Validate JWKS URL by making HTTP call
        const validationResult = await validateJwksUrl(urlValue);
        if (!validationResult.isValid) {
          form.setError("url", {
            message: validationResult.error || "Invalid, provide a valid jwks URL",
          });
          return;
        }
      }
    } else if (certificateMethod === "upload-file") {
      if (!certificateFiles || certificateFiles.length === 0) {
        showErrorToast({ errors: "Please upload a certificate file" });
        return;
      }

      const allowedExtensions = [".crt", ".der", ".pfx", ".p12"];
      const hasInvalidFile = certificateFiles.some((file) => {
        const fileName = file.name.toLowerCase();
        return !allowedExtensions.some((ext) => fileName.endsWith(ext));
      });
      if (hasInvalidFile) {
        showErrorToast({
          errors: "Only certificate files are allowed (.crt, .pfx, .der, .p12)",
        });
        return;
      }
    }

    // Trigger form validation and submit
    const isValid = await form.trigger();
    if (!isValid) return;

    const data = form.getValues();
    const { url, audience, issuer, password } = data;

    const audiences = audience
      ? audience
          .split(",")
          .map((value) => value.trim())
          .filter((value) => value.length > 0)
      : [];

    setIsSubmitting(true);

    if (certificateMethod === "public-url") {
      try {
        let jwksUrl = "";
        let publicCertificatePath = "";

        // For "Others" provider, determine if URL is JWKS or certificate path.
        if (selectedProvider === "Others" && url) {
          // Check if the URL is a JWKS URL by attempting validation.
          try {
            const validationResult = await validateJwksUrl(url);
            if (validationResult.isValid) {
              jwksUrl = url;
            } else {
              publicCertificatePath = url;
            }
          } catch {
            // If validation fails, treat as certificate path
            publicCertificatePath = url;
          }
        } else {
          // For other providers, always use jwksUrl
          jwksUrl = url || "";
        }

        const res = await savePublicCertificates({
          projectKey,
          publicCertificatePassword: password || "",
          issuer: issuer || "",
          audiences,
          publicCertificatePath,
          jwksUrl,
          providerName: selectedProvider,
        });

        if (res?.isSuccess) {
          showSuccessToast({ description: "Public certificate saved successfully." });
          setOpen(false);
        } else {
          showErrorToast({ errors: res.errors });
        }
      } catch (error) {
        showErrorToast({ errors: error });
      } finally {
        setIsSubmitting(false);
      }

      return;
    }

    const certificate = certificateFiles?.[0];
    if (!certificate) {
      showErrorToast({ errors: "No certificate file selected" });
      setIsSubmitting(false);
      return;
    }

    try {
      const uploadResponse = await uploadFileMutate({ TenantId: projectKey, file: certificate });

      if (!uploadResponse || !uploadResponse.downloadUrl) {
        showErrorToast({ errors: "Failed to get upload URL" });
        setIsSubmitting(false);
        return;
      }

      const res = await savePublicCertificates({
        projectKey,
        publicCertificatePassword: password || "",
        issuer: issuer || "",
        audiences,
        publicCertificatePath: uploadResponse.downloadUrl,
        jwksUrl: "",
        providerName: selectedProvider,
      });

      if (res?.isSuccess) {
        showSuccessToast({ description: "Public certificate saved successfully." });
        setOpen(false);
      } else {
        showErrorToast({ errors: res.errors });
      }
    } catch (error) {
      showErrorToast({ errors: error });
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleCancel = () => {
    setOpen(false);
  };

  const handleOpenChange = (isOpen: boolean) => {
    if (!isOpen) {
      resetForm(existingData);
    }
    setOpen(isOpen);
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger asChild>
        <Button variant="outline" size="sm" className="mb-4">
          {existingData ? (
            <>
              <Pencil className="mr-2 h-4 w-4" /> Edit
            </>
          ) : (
            <>
              <Plus className="mr-2 h-4 w-4" /> Add
            </>
          )}
        </Button>
      </DialogTrigger>
      <DialogContent className="flex max-h-[80vh] w-[95vw] max-w-md flex-col sm:w-full">
        <DialogHeader>
          <DialogTitle>{existingData ? "Edit provider" : "Add provider"}</DialogTitle>
          <DialogDescription>Configure your identity provider</DialogDescription>
        </DialogHeader>

        <div className="flex-1 space-y-6 overflow-y-auto px-2 pb-1">
          {/* Provider Selection */}
          <div className="space-y-3">
            <RadioGroup value={selectedProvider} onValueChange={setSelectedProvider}>
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 sm:gap-4">
                {providers.map((provider) => (
                  <div key={provider.id} className="flex items-center space-x-2">
                    <RadioGroupItem value={provider.name} id={provider.id} />
                    <Label htmlFor={provider.id} className="flex cursor-pointer items-center gap-2">
                      {provider.icon && (
                        <div className="flex h-7 w-7 items-center justify-center rounded-full p-1 dark:bg-white">
                          <img
                            src={provider.icon}
                            alt={provider.name}
                            width={20}
                            height={20}
                            className="h-full w-full object-contain"
                          />
                        </div>
                      )}
                      <span className="text-sm sm:text-base">{provider.name}</span>
                    </Label>
                  </div>
                ))}
              </div>
            </RadioGroup>
          </div>

          {/* Certificate Method Selection */}
          <div className="space-y-3">
            <Label className="text-sm sm:text-base">
              Choose how you want to add the certificate
            </Label>
            <RadioGroup value={certificateMethod} onValueChange={setCertificateMethod}>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 sm:gap-4">
                <div
                  className={`flex cursor-pointer items-center space-x-2 rounded-md border p-3 ${
                    certificateMethod === "public-url"
                      ? "border-primary bg-primary/5"
                      : "border-input"
                  }`}
                  onClick={() => setCertificateMethod("public-url")}
                >
                  <RadioGroupItem value="public-url" id="public-url" />
                  <Label htmlFor="public-url" className="cursor-pointer text-sm sm:text-base">
                    Public URL
                  </Label>
                </div>

                {selectedProvider == "Others" && (
                  <div
                    className={`flex cursor-pointer items-center space-x-2 rounded-md border p-3 ${
                      certificateMethod === "upload-file"
                        ? "border-primary bg-primary/5"
                        : "border-input"
                    }`}
                    onClick={() => setCertificateMethod("upload-file")}
                  >
                    <RadioGroupItem value="upload-file" id="upload-file" />
                    <Label htmlFor="upload-file" className="cursor-pointer text-sm sm:text-base">
                      Upload file
                    </Label>
                  </div>
                )}
              </div>
            </RadioGroup>
          </div>

          {/* Form Fields */}
          <div className="space-y-4">
            {certificateMethod === "upload-file" && (
              <div className="space-y-2">
                <Label>Upload certificate</Label>
                <FileUploader
                  value={certificateFiles}
                  onValueChange={(files) => setCertificateFiles(files ?? [])}
                  dropzoneOptions={{
                    accept: {
                      "application/x-pkcs12": [".pfx", ".p12"],
                      "application/x-x509-ca-cert": [".crt", ".der"],
                    },
                    maxFiles: 1,
                    multiple: false,
                    maxSize: 2 * 1024 * 1024,
                  }}
                  className="rounded-lg"
                >
                  <FileInput className="rounded-lg border border-dashed border-border/70 bg-muted/10">
                    <div className="flex w-full flex-col items-center justify-center gap-2 py-8">
                      <UploadCloud className="h-6 w-6 text-muted-foreground" />
                      <p className="text-sm font-medium text-foreground">
                        Click to upload or drag and drop
                      </p>
                      <p className="text-center text-[10px] leading-tight text-muted-foreground">
                        Certificate files (.crt, .pfx, .p12, .der) up to 2 MB
                      </p>
                    </div>
                  </FileInput>
                  <FileUploaderContent>
                    {(certificateFiles ?? []).map((file, index) => (
                      <FileUploaderItem
                        key={`${file.name}-${index}`}
                        index={index}
                        className="flex items-center gap-2 rounded-md border border-border/60 bg-background px-3 py-2 text-sm"
                      >
                        <Paperclip className="h-4 w-4 text-muted-foreground" />
                        <span className="truncate text-foreground">{file.name}</span>
                      </FileUploaderItem>
                    ))}
                  </FileUploaderContent>
                </FileUploader>
              </div>
            )}

            {certificateMethod === "public-url" && (
              <div className="space-y-2">
                <div className="flex items-center gap-1.5">
                  <Label htmlFor="url">URL</Label>
                </div>
                <Input
                  id="url"
                  placeholder={
                    selectedProvider == "Others"
                      ? "Enter public certificate url"
                      : "Enter JWKS (JSON Web Key Set) url"
                  }
                  autoComplete="off"
                  {...register("url")}
                />
                {errors.url && <p className="text-sm text-destructive">{errors.url.message}</p>}
              </div>
            )}

            {selectedProvider === "Others" && (
              <div className="space-y-2">
                <Label htmlFor="password">Password (Optional)</Label>
                <div className="relative">
                  <Input
                    id="password"
                    type={showPassword ? "text" : "password"}
                    placeholder="********"
                    autoComplete="new-password"
                    {...register("password")}
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="absolute right-0 top-0 h-full px-3 hover:bg-transparent"
                    onClick={() => setShowPassword(!showPassword)}
                  >
                    <Eye className="h-4 w-4" />
                  </Button>
                </div>
                {errors.password && (
                  <p className="text-sm text-destructive">{errors.password.message}</p>
                )}
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="issuer">Issuer (Optional)</Label>
              <Input id="issuer" placeholder="Enter issuer" {...register("issuer")} />
              {errors.issuer && <p className="text-sm text-destructive">{errors.issuer.message}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="audience">Audience (Optional)</Label>
              <Input
                id="audience"
                placeholder="Enter audience (comma-separated for multiple)"
                {...register("audience")}
              />
              {errors.audience && (
                <p className="text-sm text-destructive">{errors.audience.message}</p>
              )}
            </div>
          </div>
        </div>

        {/* Action Buttons - Fixed at bottom */}
        <div className="flex flex-col gap-2 border-t pt-4 sm:flex-row sm:justify-end sm:gap-3">
          <DialogClose>
            <Button
              variant="outline"
              onClick={handleCancel}
              disabled={isSubmitting}
              className="w-full sm:w-auto"
            >
              Cancel
            </Button>
          </DialogClose>
          <Button
            onClick={handleSubmit}
            disabled={isSubmitting || !isDirty}
            className="w-full sm:w-auto"
          >
            {isSubmitting ? "Saving..." : "Save"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
};
