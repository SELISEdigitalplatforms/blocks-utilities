import {
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { storageConfigurationFormDefaultValue, storageConfigurationFormSchema } from "./utils";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { z } from "zod";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useSaveStorageConfiguration } from "@blocks-storage/hooks/use-storage-configuration";
import { useProjectStore } from "@/store/useProjectStore";
import {
  IStorageConfiguration,
  STORAGE_STRATEGIES,
  StorageStrategyType,
} from "@blocks-storage/models/storage.model";
import { isErrorWithErrors } from "@/lib/error";

type SaveStorageConfigurationProps = {
  configuration?: IStorageConfiguration;
  onClose: (val: boolean) => void;
};

export const SaveStorageConfiguration = ({
  onClose,
  configuration,
}: SaveStorageConfigurationProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const form = useForm({
    defaultValues: configuration || storageConfigurationFormDefaultValue,
    resolver: zodResolver(storageConfigurationFormSchema),
  });
  const { isPending, mutateAsync } = useSaveStorageConfiguration();

  const onFormSubmitHandler = async (values: z.infer<typeof storageConfigurationFormSchema>) => {
    try {
      const payload = {
        ...values,
        storageStrategy: values.storageStrategy as StorageStrategyType,
        projectKey: tenantId,
        updateRequest: configuration ? true : false,
        itemId: configuration?.itemId || null,
      };
      const res = await mutateAsync(payload);
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({
        description: configuration
          ? "Configuration updated successfully"
          : "New configuration added successfully",
      });
      onClose(false);
      form.reset();
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  const storageStrategy = form.watch("storageStrategy") as StorageStrategyType;

  return (
    <DialogContent className="rounded-md sm:max-w-[700px]">
      <DialogHeader>
        <DialogTitle>{configuration ? "Edit" : "Add"} Storage Configuration</DialogTitle>
        <DialogDescription>
          Ensure you have selected a storage configuration provider to move forward.
        </DialogDescription>
      </DialogHeader>
      <div>
        <Form {...form}>
          <form className="flex flex-col gap-4" onSubmit={form.handleSubmit(onFormSubmitHandler)}>
            <FormField
              control={form.control}
              name="storageStrategy"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Storage Provider</FormLabel>
                  <FormControl>
                    <Select
                      onValueChange={(value) => {
                        field.onChange(value);
                        form.clearErrors();
                      }}
                      value={field.value}
                      disabled={!!configuration}
                    >
                      <SelectTrigger className="border-default col-span-3 flex h-10 w-full items-center justify-between rounded-md border bg-background px-3 py-2 text-sm shadow-none placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2">
                        <SelectValue placeholder="Select configuration provider" />
                      </SelectTrigger>
                      <SelectContent>
                        {STORAGE_STRATEGIES.map((item) => (
                          <SelectItem key={item.id} value={item.value}>
                            {item.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </FormControl>
                </FormItem>
              )}
            />
            <div className="mt-2 grid grid-cols-1 gap-4 text-left text-sm md:grid-cols-2">
              <FormField
                name="name"
                key="name"
                control={form.control}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Name</FormLabel>
                    <FormControl>
                      <Input placeholder="Enter name" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              {storageStrategy === "Amazon" && (
                <>
                  <FormField
                    name="accessKey"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Access Key</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter access key" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="secretKey"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Secret Key</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter secret key" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="cloudStorageRegionEndPoint"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Region Endpoint</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter region endpoint" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </>
              )}
              {storageStrategy === "Azure" && (
                <FormField
                  name="connectionString"
                  control={form.control}
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Connection String</FormLabel>
                      <FormControl>
                        <Input placeholder="Enter connection string" {...field} value={field.value ?? ""} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              )}
              {storageStrategy === "S3Compatible" && (
                <>
                  <FormField
                    name="accessKey"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Access Key</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter access key" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="secretKey"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Secret Key</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter secret key" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="host"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Host URL</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter host URL" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </>
              )}
              {storageStrategy === "SftpStorage" && (
                <>
                  <FormField
                    name="remoteBasePath"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Remote Base Path</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter remote base path" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="host"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Host IP Address</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter host" {...field} value={field.value ?? ""} />
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
                        <FormLabel>PORT</FormLabel>
                        <FormControl>
                          <Input type="number" placeholder="Enter port" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="userName"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Username</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter username" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="password"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Password</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter password" {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </>
              )}
            </div>
            <div className="mt-6 flex w-full items-center justify-end">
              <div className="flex flex-row gap-2">
                <DialogTrigger asChild>
                  <Button variant="outline" disabled={isPending}>
                    Cancel
                  </Button>
                </DialogTrigger>
                <Button variant="default" disabled={isPending}>
                  Save
                </Button>
              </div>
            </div>
          </form>
        </Form>
      </div>
    </DialogContent>
  );
};
