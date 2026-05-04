

import React, { useEffect } from "react";
import { Input } from "@/components/ui-kits/input/input";
import {
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogContent,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { IEmailConfig, MailServiceProvider } from "../../../../models/email";
import { useSaveEmailConfig } from "../../../../hooks/use-email-config";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { showErrorToast, toast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { isErrorWithErrors } from "@/lib/error";

interface NewConfigurationProps {
  dialogTitle: string;
  onClose: () => void;
  previousData?: IEmailConfig;
  isEdit?: boolean;
}

const schema = z
  .object({
    configurationName: z
      .string()
      .min(3, { message: "Configuration name must be at least 3 characters" })
      .max(100, { message: "Configuration name must be at most 100 characters" }),
    host: z
      .string()
      .regex(/^([\w-]+\.)*[\w-]+\.[a-z]{2,}$/, { message: "Host must be a valid domain" }),
    port: z.coerce
      .number()
      .min(1, { message: "Port must be between 1 and 65535" })
      .max(65535, { message: "Port must be between 1 and 65535" }),
    enableSSL: z.boolean(),
    senderName: z.string().optional(),
    senderAddress: z.string().optional(),
    senderUserName: z.string().min(1, { message: "Sender username is required" }),
    accountPassword: z.string().superRefine((val, ctx) => {
      if (!val || val.length === 0) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Password is required",
        });
      } else if (val.length < 6) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Password must be at least 6 characters long",
        });
      }
    }),
    isInbound: z.boolean(),
    provider: z.coerce.number(),
  })
  .superRefine((data, ctx) => {
    if (data.isInbound && data.provider === MailServiceProvider.AmazonSes) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Amazon SES is not supported for inbound configurations",
        path: ["provider"],
      });
    }
    if (!data.isInbound) {
      if (!data.senderName || data.senderName.length < 3 || data.senderName.length > 100) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Amazon SES is not supported for inbound configurations",
          path: ["provider"],
        });
      }
      if (!data.isInbound) {
        if (!data.senderName || data.senderName.length < 3 || data.senderName.length > 100) {
          ctx.addIssue({
            code: z.ZodIssueCode.custom,
            message: "Sender name must be between 3 and 100 characters",
            path: ["senderName"],
          });
        }
        if (
          !data.senderAddress ||
          !/^(?=.{1,320}$)[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/.test(data.senderAddress)
        ) {
          ctx.addIssue({
            code: z.ZodIssueCode.custom,
            message: "Sender Address must be a valid email",
            path: ["senderAddress"],
          });
        }
      }
    }
  });

const INBOUND_PROVIDERS = [{ value: MailServiceProvider.Zoho, label: "Zoho" }];
const OUTBOUND_PROVIDERS = [
  { value: MailServiceProvider.AmazonSes, label: "Amazon SES" },
  { value: MailServiceProvider.Zoho, label: "Zoho" },
];

const NewConfiguration: React.FC<NewConfigurationProps> = ({
  dialogTitle,
  onClose,
  previousData,
  isEdit,
}) => {
  // const { saveEmailConfig, isPending } = useSaveEmailConfig();
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const { isPending, mutateAsync } = useSaveEmailConfig();

  // eslint-disable-next-line react-hooks/rules-of-hooks
  const form = useForm<IEmailConfig>({
    defaultValues:
      isEdit && previousData && previousData.itemId !== ""
        ? {
            configurationId: previousData?.itemId,
            configurationName: previousData?.name,
            host: previousData?.host,
            port: previousData?.port,
            enableSSL: previousData?.enableSSL,
            senderName: previousData?.senderName,
            senderAddress: previousData?.senderAddress,
            senderUserName: previousData?.senderUserName,
            // accountPassword: previousData?.accountPassword,
            accountPassword: "",
            isInbound: previousData?.isInbound,
            provider: previousData?.provider,
          }
        : {
            configurationId: "",
            configurationName: "",
            host: "",
            port: 0,
            enableSSL: false,
            senderName: "",
            senderAddress: "",
            senderUserName: "",
            accountPassword: "",
            isInbound: false,
            provider: 0,
          },
    resolver: zodResolver(schema),
    mode: "onChange",
  });

  const isInbound = form.watch("isInbound");

  useEffect(() => {
    if (isInbound && form.getValues("provider") === MailServiceProvider.AmazonSes) {
      form.setValue("provider", MailServiceProvider.Zoho);
    }
  }, [isInbound, form]);

  if (isEdit && previousData?.itemId == "") {
    return <div>loading</div>;
  }

  const formSubmitHandler = async (data: IEmailConfig) => {
    try {
      data.configurationId = isEdit && previousData?.itemId ? previousData?.itemId : "";
      const payload = {
        ...data,
        projectKey: tenantId,
      };
      const res = await mutateAsync(payload);
      if (res?.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: isEdit ? "Configuration updated" : "New configuration added",
        });
        form.reset();
        onClose();
      } else {
        toast({
          variant: "destructive",
          title: "Error",
          description: JSON.stringify(res?.errors),
        });
      }
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors as Record<string, string | string[]> });
      }
    }
  };

  return (
    <DialogContent className="rounded-md sm:max-w-[700px]">
      <Form {...form}>
        {" "}
        <form onSubmit={form.handleSubmit(formSubmitHandler)}>
          <DialogHeader>
            <DialogTitle className="mb-2 text-left">{dialogTitle}</DialogTitle>
            <DialogDescription asChild>
              <div className="pb-4 pt-4 text-left">
                <div className="grid grid-cols-1 gap-4">
                  <FormField
                    name="configurationName"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          {" "}
                          Name
                        </FormLabel>
                        <FormControl>
                          <Input
                            id="configName"
                            placeholder="Enter name"
                            className="border-default col-span-3 mt-1 border shadow-none"
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
                <div className="mt-4 grid grid-cols-2 gap-4">
                  <FormField
                    name="isInbound"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          Type
                        </FormLabel>
                        <Select
                          onValueChange={(value) => field.onChange(value === "true")}
                          value={field.value ? "true" : "false"}
                        >
                          <FormControl>
                            <SelectTrigger className="border-default col-span-3 mt-1 border shadow-none">
                              <SelectValue placeholder="Select type" />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            <SelectItem value="true">Inbound</SelectItem>
                            <SelectItem value="false">Outbound</SelectItem>
                          </SelectContent>
                        </Select>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="provider"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          Provider
                        </FormLabel>
                        <Select
                          onValueChange={(value) => field.onChange(parseInt(value))}
                          value={field.value?.toString()}
                        >
                          <FormControl>
                            <SelectTrigger className="border-default col-span-3 mt-1 border shadow-none">
                              <SelectValue placeholder="Select provider" />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            {(isInbound ? INBOUND_PROVIDERS : OUTBOUND_PROVIDERS).map(
                              (provider) => (
                                <SelectItem key={provider.value} value={provider.value.toString()}>
                                  {provider.label}
                                </SelectItem>
                              ),
                            )}
                          </SelectContent>
                        </Select>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
                <div className="mt-4 grid grid-cols-2 gap-4">
                  <FormField
                    name="host"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          {" "}
                          {isInbound ? "Server Name" : "Host"}
                        </FormLabel>
                        <FormControl>
                          <Input
                            placeholder={isInbound ? "Enter Server Name" : "Enter Host"}
                            className="border-default col-span-3 mt-1 border shadow-none"
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  <FormField
                    name="port"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          {" "}
                          Port
                        </FormLabel>
                        <FormControl>
                          <Input
                            type="number"
                            placeholder="Enter port"
                            className="border-default col-span-3 mt-1 border shadow-none"
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>

                {!isInbound && (
                  <div className="mt-4 grid grid-cols-2 gap-4">
                    <div>
                      <FormField
                        name="senderName"
                        control={form.control}
                        render={({ field }) => (
                          <FormItem>
                            <FormLabel className="text-left font-medium text-high-emphasis">
                              {" "}
                              Sender Name
                            </FormLabel>
                            <FormControl>
                              <Input
                                placeholder="Enter sender name"
                                className="border-default col-span-3 mt-1 border shadow-none"
                                {...field}
                              />
                            </FormControl>
                            <FormMessage />
                          </FormItem>
                        )}
                      />
                    </div>
                    <div>
                      <FormField
                        name="senderAddress"
                        control={form.control}
                        render={({ field }) => (
                          <FormItem>
                            <FormLabel className="text-left font-medium text-high-emphasis">
                              {" "}
                              Sender Address
                            </FormLabel>
                            <FormControl>
                              <Input
                                placeholder="Enter sender address"
                                className="border-default col-span-3 mt-1 border shadow-none"
                                {...field}
                              />
                            </FormControl>
                            <FormMessage />
                          </FormItem>
                        )}
                      />
                    </div>
                  </div>
                )}
                <div className="mt-4 grid grid-cols-2 gap-4">
                  <div>
                    <FormField
                      name="senderUserName"
                      control={form.control}
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel className="text-left font-medium text-high-emphasis">
                            {" "}
                            {isInbound ? "Username" : "Sender Username"}
                          </FormLabel>
                          <FormControl>
                            <Input
                              placeholder={isInbound ? "Enter username" : "Enter sender username"}
                              className="border-default col-span-3 mt-1 border shadow-none"
                              {...field}
                            />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </div>
                  <div>
                    <FormField
                      name="accountPassword"
                      control={form.control}
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel className="text-left font-medium text-high-emphasis">
                            {" "}
                            Account password
                          </FormLabel>
                          <FormControl>
                            <Input
                              type="password"
                              placeholder="Enter password"
                              className="border-default col-span-3 mt-1 border shadow-none"
                              {...field}
                            />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </div>
                </div>
                <div className="mt-4">
                  <FormField
                    name="enableSSL"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormControl>
                          <Checkbox
                            className="mr-2"
                            checked={field.value}
                            onCheckedChange={field.onChange}
                          />
                        </FormControl>
                        <FormMessage />
                        <FormLabel className="flex-start inline-flex"> Enable SSL </FormLabel>
                      </FormItem>
                    )}
                  />
                </div>
              </div>
            </DialogDescription>
          </DialogHeader>

          <div className="flex justify-end">
            <div className="flex flex-row justify-end gap-2">
              <DialogTrigger asChild>
                <Button variant="outline" size="default" disabled={isPending}>
                  Cancel
                </Button>
              </DialogTrigger>
              <Button disabled={isPending || !form.formState.isValid} size="default">
                Save
              </Button>
            </div>
          </div>
        </form>
      </Form>
    </DialogContent>
  );
};

export default NewConfiguration;
