import { z } from "zod";
import { INotificationConfig } from "../models/notification.model";
import { useSaveNotificationConfig } from "../hooks/use-notifications";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { channelsToNotify, notificationTypes } from "../constants/notification.constant";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { useEffect } from "react";
import { useProjectStore } from "@/store/useProjectStore";
import { showErrorToast, toast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import {
  DialogContent,
  DialogDescription,
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Button } from "@/components/ui-kits/button/button";

interface NewNotificationConfigurationProps {
  dialogTitle: string;
  // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
  onClose: Function;
  previousData?: INotificationConfig;
  isEdit: boolean;
}

const schema = z.object({
  name: z
    .string()
    .min(3, { message: "Configuration name must be at least 3 characters" })
    .max(100, { message: "Configuration name must be at most 100 characters" })
    .refine((val) => val.trim().length > 0, {
      message: "Name cannot contain only whitespace",
    }),
  channelToNotify: z.number().min(0, { message: "Channel to notify is required" }),
  notificationType: z.number().min(0, { message: "Notification type is required" }),
  enablePersistence: z.boolean(),
  notifyMethod: z
    .string()
    .min(3, { message: "Notify method must be at least 3 characters" })
    .max(100, { message: "Notify method must be at most 100 characters" })
    .refine((val) => val.trim().length > 0, {
      message: "Notify method cannot contain only whitespace",
    }),
});

const NewNotificationConfiguration: React.FC<NewNotificationConfigurationProps> = ({
  dialogTitle,
  onClose,
  previousData,
  isEdit,
}) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const { isPending, mutateAsync } = useSaveNotificationConfig();

  if (isEdit && previousData?.itemId == "") {
    return <div>loading</div>;
  }

  // eslint-disable-next-line react-hooks/rules-of-hooks
  const form = useForm<INotificationConfig>({
    defaultValues: isEdit
      ? {
          name: previousData?.name || "",
          channelToNotify: previousData?.channelToNotify || 0,
          notificationType: previousData?.notificationType || 0,
          enablePersistence: previousData?.enablePersistence || false,
          notifyMethod: previousData?.notifyMethod || "",
        }
      : {
          name: "",
          channelToNotify: 0,
          notificationType: 0,
          enablePersistence: false,
          notifyMethod: "",
        },
    resolver: zodResolver(schema),
    mode: "onChange",
  });

  // eslint-disable-next-line react-hooks/rules-of-hooks
  useEffect(() => {
    if (isEdit && previousData) {
      form.reset({
        name: previousData?.name || "",
        channelToNotify: previousData?.channelToNotify || 0,
        notificationType: previousData?.notificationType || 0,
        enablePersistence: previousData?.enablePersistence || false,
        notifyMethod: previousData?.notifyMethod || "",
      });
    } else if (!isEdit) {
      form.reset({
        name: "",
        channelToNotify: 0,
        notificationType: 0,
        enablePersistence: false,
        notifyMethod: "",
      });
    }
  }, [previousData, isEdit, form]);

  const formSubmitHandler = async (data: INotificationConfig) => {
    try {
      data.itemId = isEdit && previousData?.itemId ? previousData?.itemId : "";
      const payload = {
        ...data,
        projectKey: tenantId,
        isUpdateRequest: isEdit,
      };
      const res = await mutateAsync(payload);
      if (res?.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: isEdit ? "Configuration updated" : "New configuration added",
        });
        form.reset();
        onClose(false);
      } else {
        showErrorToast({ errors: res?.errors });
      }
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors as Record<string, string | string[]> });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    }
  };

  return (
    <DialogContent className="rounded-md sm:max-w-[700px]">
      <Form {...form}>
        <form onSubmit={form.handleSubmit(formSubmitHandler)}>
          <DialogHeader>
            <DialogTitle className="mb-2 text-left">{dialogTitle}</DialogTitle>
            <DialogDescription asChild>
              <div className="pb-4 pt-4 text-left">
                <div className="grid grid-cols-1 gap-4">
                  <FormField
                    name="name"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          Name *
                        </FormLabel>
                        <FormControl>
                          <Input
                            disabled={isEdit}
                            id="configName"
                            placeholder="Enter name"
                            className="border-default col-span-3 mt-1 border shadow-none"
                            {...field}
                            onBlur={(e) => {
                              field.onChange(e.target.value.trim());
                              field.onBlur();
                            }}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
                <div className="mt-4 grid grid-cols-2 gap-4">
                  <FormField
                    name="channelToNotify"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          Channel to Notify *
                        </FormLabel>
                        <Select
                          disabled={true}
                          onValueChange={(val) => field.onChange(Number(val))}
                          defaultValue={String(field.value)}
                        >
                          <FormControl>
                            <SelectTrigger className="border-default col-span-3 flex h-10 w-full items-center justify-between rounded-md border bg-background px-3 py-2 text-sm shadow-none">
                              <SelectValue placeholder="Select Configuration" />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            {channelsToNotify.map((channel) => (
                              <SelectItem key={channel.value} value={String(channel.value)}>
                                {channel.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="notificationType"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          Notification Type *
                        </FormLabel>
                        <Select
                          onValueChange={(val) => field.onChange(Number(val))}
                          defaultValue={String(field.value)}
                        >
                          <FormControl>
                            <SelectTrigger className="border-default col-span-3 flex h-10 w-full items-center justify-between rounded-md border bg-background px-3 py-2 text-sm shadow-none">
                              <SelectValue placeholder="Select Notification Type" />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            {notificationTypes.map((type) => (
                              <SelectItem key={type.value} value={String(type.value)}>
                                {type.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
                <div className="mt-4 grid grid-cols-1 gap-4">
                  <FormField
                    name="notifyMethod"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          Notify Method *
                        </FormLabel>
                        <FormControl>
                          <Input
                            id="notifyMethod"
                            placeholder="Enter notify method"
                            className="border-default col-span-3 mt-1 border shadow-none"
                            {...field}
                            onBlur={(e) => {
                              field.onChange(e.target.value.trim());
                              field.onBlur();
                            }}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
                <div className="mt-4">
                  <FormField
                    name="enablePersistence"
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
                        <FormLabel className="flex-start inline-flex">Enable Persistence</FormLabel>
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

export default NewNotificationConfiguration;
