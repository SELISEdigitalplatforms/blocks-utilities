import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { ChevronDown } from "lucide-react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
import { getProviderDisplayName, ServicePlatform } from "@blocks-ai/utils/aimodel-provider.utils";
import { resolveModelConfig, transformToUniversal } from "@blocks-ai/utils/aimodel-form.utils";
import { useCreateModel } from "@blocks-ai/hooks/use-aimodel";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";

interface ModelAddKeyModalProps {
  provider: string;
  baseUrl: string;
  modelOptions: { model: string; goodName: string }[];
  servicePlatform: ServicePlatform;
  addKeyModalOpen: boolean;
  setAddKeyModalOpen: (open: boolean) => void;
}

export const ModelAddKeyModal = ({
  provider,
  baseUrl,
  modelOptions,
  servicePlatform,
  addKeyModalOpen,
  setAddKeyModalOpen,
}: ModelAddKeyModalProps) => {
  const project_key = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useCreateModel();

  const { schema, defaultValues, fields } = resolveModelConfig(provider, servicePlatform, modelOptions);

  type AllFormValues = {
    url: string;
    model: string;
    apiKey: string;
    organizationId?: string;
    projectId?: string;
    deploymentName?: string;
  };

  const form = useForm<AllFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      ...defaultValues,
      url: baseUrl || defaultValues.url,
    } as AllFormValues,
    mode: "onChange",
  });

  const selectedModel = form.watch("model");
  const selectedGoodName =
    modelOptions.find((o) => o.model === selectedModel)?.goodName ??
    modelOptions[0]?.goodName ??
    "";

  const onSubmitHandler = async (data: z.infer<typeof schema>) => {
    try {
      const payload = transformToUniversal(
        provider.toLowerCase(),
        project_key,
        data as Record<string, unknown>,
      );
      payload.model_name = payload.model_name === "--" ? modelOptions[0].model : payload.model_name;
      if (servicePlatform === ServicePlatform.OPEN_DEPLOYMENT)
        payload.deployment_name = selectedModel;
      const res = await mutateAsync(payload);
      if (res.is_success) {
        showSuccessToast({ description: "Model added successfully." });
      } else {
        showErrorToast({ errors: res.detail });
      }
      setAddKeyModalOpen(false);
      form.reset();
    } catch (err) {
      showErrorToast({ errors: err instanceof Error ? err.message : String(err) });
    }
  };

  return (
    <Dialog
      open={addKeyModalOpen}
      onOpenChange={(open) => {
        setAddKeyModalOpen(open);
        if (!open) form.reset();
      }}
    >
      <DialogContent
        className="w-[calc(100%-2rem)] p-6 sm:mx-0 sm:w-[500px]"
        onOpenAutoFocus={(e) => e.preventDefault()}
      >
        <DialogHeader>
          <DialogTitle>Add {getProviderDisplayName(provider)} Key</DialogTitle>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmitHandler)}>
            <div className="mt-4 flex flex-col gap-4">
              {fields.includes("model") && modelOptions.length > 0 ? (
                <DropdownMenu>
                  <DropdownMenuLabel className="p-0 text-sm font-medium">
                    Model <span className="text-red-500">*</span>
                  </DropdownMenuLabel>
                  <DropdownMenuTrigger asChild>
                    <div className="flex cursor-pointer items-center justify-between rounded-md border px-3 py-2">
                      <span className="text-sm font-normal">{selectedGoodName}</span>
                      <ChevronDown className="h-4 w-4" />
                    </div>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent
                    align="start"
                    className="max-h-52 w-[var(--radix-dropdown-menu-trigger-width)] overflow-y-auto"
                  >
                    {modelOptions.map((opt, i) => (
                      <div key={opt.model}>
                        <DropdownMenuItem
                          className={opt.model === form.watch("model") ? "font-normal" : "cursor-pointer"}
                          onClick={() => form.setValue("model", opt.model)}
                        >
                          {opt.goodName}
                        </DropdownMenuItem>
                        {i !== modelOptions.length - 1 && <DropdownMenuSeparator />}
                      </div>
                    ))}
                  </DropdownMenuContent>
                </DropdownMenu>
              ) : (
                fields.includes("model") && (
                  <FormField
                    control={form.control}
                    name="model"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Model <span className="text-red-500">*</span></FormLabel>
                        <FormControl>
                          <Input placeholder="Enter model" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                )
              )}
              {fields.includes("url") && (
                <FormField
                  control={form.control}
                  name="url"
                  render={({ field }) => (
                    <FormItem className="w-full">
                      <FormLabel>URL <span className="text-red-500">*</span></FormLabel>
                      <FormControl className="w-full">
                        <Input
                          className="flex w-full"
                          {...field}
                          disabled={provider.toLowerCase() !== "azure"}
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              )}
              {fields.includes("deploymentName") && (
                <FormItem>
                  <FormLabel>Deployment Name <span className="text-red-500">*</span></FormLabel>
                  <Input value={selectedModel} disabled />
                </FormItem>
              )}
              {fields.includes("apiKey") && (
                <FormField
                  control={form.control}
                  name="apiKey"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>API Key <span className="text-red-500">*</span></FormLabel>
                      <FormControl>
                        <Input placeholder="Enter API key" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              )}
              {fields.includes("organizationId") && (
                <FormField
                  control={form.control}
                  name="organizationId"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>OpenAI Organization ID</FormLabel>
                      <FormControl>
                        <Input placeholder="Enter organization ID" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              )}
              {fields.includes("projectId") && (
                <FormField
                  control={form.control}
                  name="projectId"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>OpenAI Project ID</FormLabel>
                      <FormControl>
                        <Input placeholder="Enter project ID" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              )}
            </div>
            <DialogFooter className="mt-6">
              <DialogClose asChild>
                <Button variant="secondary">Cancel</Button>
              </DialogClose>
              <Button disabled={!form.formState.isValid || isPending} type="submit">
                {isPending ? "Saving..." : "Save"}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
