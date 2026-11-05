import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useEffect, useRef, useState } from "react";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";

import { useForm } from "react-hook-form";
import { Plus, Camera, Pencil } from "lucide-react";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { ISaveOidcCredentialPayload } from "@blocks-idp/authentication/models/auth.oidc.model";
import {
  useGetAuthOidcCredential,
  useSaveAuthOidc,
} from "@blocks-idp/authentication/hooks/use-auth-oidc";
import { createOIDCFormDefaultValue, CreateOIDCFormValues, makeOidcSchema } from "./utils";
import { Input } from "@/components/ui-kits/input/input";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Button } from "@/components/ui-kits/button/button";
import { isErrorWithErrors } from "@/lib/error";
import {
  useCompleteUpload,
  useGetPreSignedUrlForUpload,
  useUploadFile,
} from "@blocks-storage/hooks/use-storage-file";
import { storageService } from "@blocks-storage/services/storage.service";
import { ColorSwatch } from "@/components/color-swatch/color-swatch";
import { ModuleName } from "@/constants/modules.constants";

type CreateOIDCProps = {
  itemId?: string;
  triggerVariant?: "default" | "ghost" | "outline";
};

export const CreateOIDC = ({ itemId, triggerVariant = "default" }: CreateOIDCProps) => {
  const { t } = useT("utilities.idp.authentication");
  const [open, setOpen] = useState<boolean>(false);
  const [clientLogoUrl, setClientLogoUrl] = useState<string>("");
  const [isUploadingImage, setIsUploadingImage] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const MAX_LOGO_FILE_SIZE = 5 * 1024 * 1024; // 5MB
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useSaveAuthOidc();
  const { data: existingOidc, isLoading: isLoadingOidc } = useGetAuthOidcCredential(
    { projectKey: tenantId, clientId: itemId! },
    open && !!itemId,
  );
  const { mutateAsync: getPreSign } = useGetPreSignedUrlForUpload();
  const { mutateAsync: uploadFile } = useUploadFile();
  const { mutateAsync: completeUpload } = useCompleteUpload();

  const form = useForm({
    resolver: zodResolver(makeOidcSchema()),
    mode: "all",
    defaultValues: createOIDCFormDefaultValue,
  });

  const {
    formState: { isDirty, isValid },
  } = form;

  const isEditMode = !!itemId;
  const dialogTitle = isEditMode ? "Edit OIDC Client" : "New OIDC Client";
  const dialogDescription = isEditMode
    ? "Update OIDC client details"
    : "Enter details to create a new key";

  useEffect(() => {
    if (isEditMode && existingOidc?.oIDCClientCredential && open) {
      const credential = existingOidc.oIDCClientCredential;
      form.reset({
        redirectUrlOidc: credential.redirectUri,
        audienceUrlOidc: credential.audience,
        scope: credential.scope || "",
        clientBrandColor: credential.clientBrandColor || "#124091",
        clientDisplayName: credential.clientDisplayName || "",
      });
      setClientLogoUrl(credential.clientLogoUrl || "");
    } else if (!isEditMode && open) {
      form.reset({
        ...createOIDCFormDefaultValue,
        clientBrandColor: "#124091",
      });
      setClientLogoUrl("");
    }
  }, [existingOidc, isEditMode, open, form]);

  const handleImageUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    const allowedImageTypes = [
      "image/jpeg",
      "image/png",
      "image/gif",
      "image/webp",
      "image/svg+xml",
    ];
    const allowedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"];
    const fileName = file.name.toLowerCase();

    if (
      !allowedImageTypes.includes(file.type) &&
      !allowedExtensions.some((ext) => fileName.endsWith(ext))
    ) {
      showErrorToast({
        errors: t("CREATE_OIDC.INVALID_FILE_TYPE_ONLY_JPG"),
      });
      e.target.value = "";
      return;
    }

    if (file.size > MAX_LOGO_FILE_SIZE) {
      showErrorToast({ errors: t("CREATE_OIDC.IMAGE_SIZE_MUST_BE_UNDER") });
      e.target.value = "";
      return;
    }

    try {
      setIsUploadingImage(true);

      const preSign = await getPreSign({
        accessModifier: "Public",
        configurationName: "Default",
        name: file.name,
        projectKey: tenantId ?? "",
        tags: "",
        metaData: "",
        parentDirectoryId: "",
        moduleName: ModuleName.IAMCloud,
      });

      if (!preSign.isSuccess) throw new Error(t("CREATE_OIDC.FAILED_TO_GET_UPLOAD_URL"));

      await uploadFile({ url: preSign.uploadUrl, file });

      if (preSign.uploadCompletionRequired) {
        const completion = await completeUpload({
          fileId: preSign.fileId,
          fileVersionId: preSign.fileVersionId ?? "",
        });
        if (completion.verificationStatus !== "Verified") {
          showErrorToast({
            errors: completion.rejectionReason ?? t("CREATE_OIDC.LOGO_FAILED_VERIFICATION"),
          });
          return;
        }
      }

      const fileInfo = await storageService.file.getFileByFileId({
        itemId: preSign.fileId,
        projectKey: tenantId ?? "",
      });

      setClientLogoUrl(fileInfo.url);

      showSuccessToast({ description: t("CREATE_OIDC.LOGO_UPLOADED_SUCCESSFULLY") });
    } catch (err: unknown) {
      if (isErrorWithErrors(err)) return showErrorToast({ errors: err.errors });
      showErrorToast({ errors: t("CREATE_OIDC.SOMETHING_WENT_WRONG_UPLOADING") });
    } finally {
      setIsUploadingImage(false);
      e.target.value = "";
    }
  };

  const onSubmit = async (data: CreateOIDCFormValues) => {
    try {
      const payload: ISaveOidcCredentialPayload = {
        audience: data.audienceUrlOidc,
        redirectUri: data.redirectUrlOidc,
        scope: data.scope,
        isAutoRedirect: false,
        itemId: isEditMode ? itemId : "",
        projectKey: tenantId,
        clientLogoUrl: clientLogoUrl || undefined,
        clientBrandColor: data.clientBrandColor || undefined,
        clientDisplayName: data.clientDisplayName,
      };

      const res = await mutateAsync(payload);

      if (!res.isSuccess) return showErrorToast({ errors: res.error });
      const message = isEditMode
        ? "OIDC Client updated successfully"
        : "OIDC Client created successfully";
      showSuccessToast({ description: message });
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: t("CREATE_OIDC.SOMETHING_WENT_WRONG") });
    } finally {
      form.reset();
      setClientLogoUrl("");
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        {isEditMode ? (
          <Button variant={triggerVariant} size="sm">
            <Pencil className="h-4 w-4" />
          </Button>
        ) : (
          <Button>
            <Plus className="aspect-square w-4" />
            <span className="sr-only sm:not-sr-only sm:ml-2">{t("common:CREATE")}</span>
          </Button>
        )}
      </DialogTrigger>
      <DialogContent className="flex h-screen max-h-[95vh] w-screen flex-col rounded-none sm:h-auto sm:max-h-[95vh] sm:w-auto sm:rounded-lg md:h-auto md:w-[600px]">
        <DialogHeader>
          <DialogTitle>{dialogTitle}</DialogTitle>
          <DialogDescription>{dialogDescription}</DialogDescription>
        </DialogHeader>
        <div className="flex-1 overflow-y-auto">
          <Form {...form}>
            <form id="oidc-form" onSubmit={form.handleSubmit(onSubmit)} className="space-y-6 px-4">
              <div className="flex flex-col items-center gap-4">
                <div className="relative h-32 w-32 overflow-hidden rounded-lg border border-dashed border-border bg-muted">
                  {clientLogoUrl ? (
                    <img src={clientLogoUrl} alt={t("CREATE_OIDC.OIDC_LOGO")} className="object-cover" />
                  ) : (
                    <div className="flex h-full items-center justify-center">
                      <Camera className="h-6 w-6 text-muted-foreground" />
                    </div>
                  )}
                  {isUploadingImage && <div className="absolute inset-0 bg-muted/50" />}
                </div>
                <div className="flex gap-2">
                  <input
                    ref={fileInputRef}
                    type="file"
                    accept=".jpg,.jpeg,.png,.gif,.webp, .svg"
                    onChange={handleImageUpload}
                    className="hidden"
                  />
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => fileInputRef.current?.click()}
                    disabled={isUploadingImage}
                  >
                    {t("CREATE_OIDC.UPLOAD_LOGO")}
                  </Button>
                  {clientLogoUrl && (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => setClientLogoUrl("")}
                    >
                      {t("common:REMOVE")}
                    </Button>
                  )}
                </div>
              </div>

              <FormField
                control={form.control}
                name="clientDisplayName"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("CREATE_OIDC.CLIENT_NAME")}</FormLabel>
                    <FormControl>
                      <Input placeholder={t("CREATE_OIDC.ENTER_CLIENT_NAME")} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                control={form.control}
                name="redirectUrlOidc"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("CREATE_OIDC.REDIRECT_URL")}</FormLabel>
                    <FormControl>
                      {/* eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose */}
                      <Input placeholder="https://example.com/oidc" {...field} />
                    </FormControl>

                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                control={form.control}
                name="audienceUrlOidc"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("CREATE_OIDC.AUDIENCE")}</FormLabel>
                    <FormControl>
                      {/* eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose */}
                      <Input placeholder="https://example.com" {...field} />
                    </FormControl>

                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                control={form.control}
                name="clientBrandColor"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("CREATE_OIDC.COLOR_THEME_PICKER")}</FormLabel>
                    <FormControl>
                      <ColorSwatch value={field.value || "#124091"} onChange={field.onChange} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                control={form.control}
                name="scope"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("CREATE_OIDC.SCOPE_S")}</FormLabel>
                    <FormControl>
                      <div className="flex items-center gap-2 rounded border p-4">
                        <Checkbox {...field} id="scope" checked={true} disabled={true} />
                        <label htmlFor="scope" className="cursor-pointer">
                          {t("CREATE_OIDC.OPENID")}
                        </label>
                      </div>
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </form>
          </Form>
        </div>
        <DialogFooter>
          <Button onClick={() => setOpen(false)} type="button" variant="outline">
            {t("common:CANCEL")}
          </Button>
          <Button
            form="oidc-form"
            type="submit"
            disabled={!isValid || isPending || isLoadingOidc || !isDirty}
          >
            {isEditMode ? t("common:UPDATE") : t("common:ADD")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
