import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";

import { CAPTCHA_PROVIDERS, CAPTCHA_PROVIDERS_KEY, ICaptchaConfig } from "../../models/captcha";

import { ConfigureGeneralCaptchaFormField } from "./configure-general-captcha-from-field";
import { ConfigureBlockCaptchaFormField } from "./configure-block-captcha-form-field";
import { useGetCaptchaConfigs, useSaveCaptcha } from "../../hooks/use-captcha-config";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { ConfigureCaptchaFormDefaultValue, ConfigureCaptchaFormSchema } from "./utils";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Button } from "@/components/ui-kits/button/button";
import { useProjectStore } from "@/store/useProjectStore";
import { ReactNode, useEffect, useMemo, useState } from "react";

type ConfigureCaptchaModalProps = {
  configuration?: ICaptchaConfig | null;
  children: ReactNode;
};

export const ConfigureCaptchaModal = ({ configuration, children }: ConfigureCaptchaModalProps) => {
  const [open, setOpen] = useState<boolean>(false);
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { isLoading, isFetching, data } = useGetCaptchaConfigs({ projectKey: tenantId });

  const form = useForm({
    defaultValues: configuration || ConfigureCaptchaFormDefaultValue,
    resolver: zodResolver(ConfigureCaptchaFormSchema),
  });
  const {
    formState: { isDirty },
  } = form;
  const { mutateAsync, isPending } = useSaveCaptcha();

  const unConfiguredProviders = useMemo(() => {
    if (configuration) return [CAPTCHA_PROVIDERS[configuration.provider]];
    if (!data?.configurations) return Object.values(CAPTCHA_PROVIDERS);

    return Object.keys(CAPTCHA_PROVIDERS)
      .filter(
        (item) =>
          !data.configurations.find((config: { provider: string }) => config?.provider === item),
      )
      .map((item) => CAPTCHA_PROVIDERS[item as CAPTCHA_PROVIDERS_KEY]);
  }, [data]);

  useEffect(() => {
    if (configuration) {
      form.setValue("provider", configuration.provider);
    }
    if (unConfiguredProviders.length) {
      form.setValue("provider", unConfiguredProviders[0].value);
    }
  }, [unConfiguredProviders]);

  const onSubmitHandler = async (values: typeof ConfigureCaptchaFormDefaultValue) => {
    try {
      const payload = {
        projectKey: tenantId,
        isEnable: configuration ? configuration.isEnable : false,
        ...values,
      };
      const res = await mutateAsync(payload);
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({
        description: configuration ? "Captcha updated successfully" : "Captcha added successfully",
      });
      form.reset();
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      }
    }
  };

  const selectedProvider = form.watch("provider");

  const ConfigureFormField = ConfigureGeneralCaptchaFormField;

  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        form.reset(configuration || ConfigureCaptchaFormDefaultValue);
        setOpen(value);
      }}
    >
      {children}

      <DialogContent aria-describedby={undefined}>
        <DialogHeader>
          <DialogTitle>
            {configuration
              ? `Edit ${CAPTCHA_PROVIDERS[configuration.provider].label}`
              : "Add Captcha Configuration"}{" "}
          </DialogTitle>
        </DialogHeader>
        <div className="mt-2">
          <Form {...form}>
            <form className="flex flex-col gap-4" onSubmit={form.handleSubmit(onSubmitHandler)}>
              <FormField
                control={form.control}
                name="provider"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Captcha Provider</FormLabel>
                    <FormControl>
                      <Select
                        onValueChange={field.onChange}
                        value={field.value}
                        disabled={!!configuration}
                      >
                        <SelectTrigger className="border-default col-span-3 flex h-10 w-full items-center justify-between rounded-md border bg-background px-3 py-2 text-sm shadow-none placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2">
                          <SelectValue placeholder="Select configuration provider" />
                        </SelectTrigger>
                        <SelectContent>
                          {unConfiguredProviders.map((item) => (
                            <SelectItem key={item.value} value={item.value}>
                              {item.label}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <ConfigureFormField key={selectedProvider} form={form} />
              <ConfigureBlockCaptchaFormField form={form} />
              <DialogFooter className="mt-4">
                <DialogTrigger asChild>
                  <Button variant="outline" size="sm">
                    Cancel
                  </Button>
                </DialogTrigger>

                <Button
                  size="sm"
                  disabled={isPending || isLoading || isFetching || !isDirty}
                  type="submit"
                >
                  Save
                </Button>
              </DialogFooter>
            </form>
          </Form>
        </div>
      </DialogContent>
    </Dialog>
  );
};
