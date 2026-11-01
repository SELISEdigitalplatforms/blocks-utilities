import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
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
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";

import {
  useGetAuthConfig,
  useSaveAuthConfig,
} from "@blocks-idp/authentication/hooks/use-auth-config";
import { zodResolver } from "@hookform/resolvers/zod";
import { Pen } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { authConfigFormDefaultValues, createAuthConfigFormSchema } from "./utils";

export const EditGeneralSettings = () => {
  const { t } = useT("utilities.idp.authentication");
  const [open, setOpen] = useState<boolean>(false);
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { data } = useGetAuthConfig({ projectKey: tenantId });

  const { mutateAsync, isPending } = useSaveAuthConfig({ projectKey: tenantId });

  const form = useForm<z.infer<ReturnType<typeof createAuthConfigFormSchema>>>({
    defaultValues: authConfigFormDefaultValues,
    resolver: zodResolver(createAuthConfigFormSchema()),
  });

  const {
    formState: { isDirty },
  } = form;

  const handleDialogOpenChange = (isOpen: boolean) => {
    if (isOpen) {
      form.reset(data || authConfigFormDefaultValues);
    }
    setOpen(isOpen);
  };

  const submitHandler = async (values: z.infer<ReturnType<typeof createAuthConfigFormSchema>>) => {
    try {
      if (!tenantId || !data) return showErrorToast({ errors: t("EDIT_SETTINGS.SOMETHING_WENT_WRONG") });

      const res = await mutateAsync({
        ...data,
        ...values,
        projectKey: tenantId,
      });

      if (!res.isSuccess) return showErrorToast({ errors: res.errors });

      showSuccessToast({ description: t("EDIT_SETTINGS.CONFIGURATION_UPDATED_SUCCESSFULLY") });
      form.reset();
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: t("EDIT_SETTINGS.SOMETHING_WENT_WRONG") });
    }
  };
  return (
    <Dialog open={open} onOpenChange={handleDialogOpenChange}>
      <DialogTrigger asChild>
        <PrimaryButton label={t("common:EDIT")} Icon={Pen} />
      </DialogTrigger>
      <DialogContent className="max-w-3xl">
        <DialogHeader>
          <DialogTitle>{t("common:SETTINGS")}</DialogTitle>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(submitHandler)} onReset={() => form.reset()}>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <FormField
                name="accessTokenValidForNumberMinutes"
                control={form.control}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel className="text-muted-foreground">
                      {t("EDIT_SETTINGS.ACCESS_TOKEN_VALIDITY_MINUTES")}
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder={t("EDIT_SETTINGS.SET_A_DURATION_IN_MINUTES")} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                name="refreshTokenValidForNumberMinutes"
                control={form.control}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel className="text-muted-foreground">
                      {t("EDIT_SETTINGS.REFRESH_TOKEN_VALIDITY_MINUTES")}
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder={t("EDIT_SETTINGS.SET_A_DURATION_IN_MINUTES")} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                name="rememberMeRefreshTokenValidForNumberMinutes"
                control={form.control}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel className="text-muted-foreground">
                      {t("EDIT_SETTINGS.REMEMBER_ME_TOKEN_VALIDITY_MINUTES")}
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder={t("EDIT_SETTINGS.SET_A_DURATION_IN_MINUTES")} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                name="getNumberOfWrongAttemptsToLockTheAccount"
                control={form.control}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel className="text-muted-foreground">
                      {t("EDIT_SETTINGS.MAX_WRONG_ATTEMPTS_BEFORE_LOCK")}
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder={t("EDIT_SETTINGS.ENTER_NUMBER")} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                name="accountLockDurationInMinutes"
                control={form.control}
                render={({ field }) => (
                  <FormItem className="">
                    <FormLabel className="text-muted-foreground">
                      {t("EDIT_SETTINGS.ACCOUNT_LOCK_DURATION_MINUTES")}
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder={t("EDIT_SETTINGS.SET_A_DURATION_IN_MINUTES")} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>
            <div className="mt-5 flex items-center justify-end gap-4">
              <DialogClose>
                <Button variant="outline" className="h-9 w-20" size="sm" type="reset">
                  {t("common:CANCEL")}
                </Button>
              </DialogClose>
              <Button
                size="sm"
                variant="default"
                className="h-9 w-20 text-primary-foreground"
                disabled={isPending || !isDirty}
              >
                {t("common:SAVE")}
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
