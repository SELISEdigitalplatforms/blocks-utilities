import { useMemo, useState } from "react";
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
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { Pen } from "lucide-react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
import { getProviderDisplayName, ServicePlatform } from "@blocks-ai/utils/aimodel-provider.utils";
import { resolveModelConfig } from "@blocks-ai/utils/aimodel-form.utils";
import { IModelInfo, IUpdateModelPayload } from "@blocks-ai/types/aimodel.service.type";
import { useUpdateModel } from "@blocks-ai/hooks/use-aimodel";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";

interface ModelEditKeyModalProps {
  modelOptions: { model: string; goodName: string }[];
  editKeyModalOpen: boolean;
  setEditKeyModalOpen: (open: boolean) => void;
  model: IModelInfo;
}

type DefaultValuesWithKnownKeys = {
  model?: string;
  url?: string;
  organizationId?: string;
  projectId?: string;
  deploymentName?: string;
};

export const ModelEditKeyModal = ({
  modelOptions,
  editKeyModalOpen,
  setEditKeyModalOpen,
  model,
}: ModelEditKeyModalProps) => {
  const project_key = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useUpdateModel();

  const {
    schema: baseSchema,
    defaultValues,
    fields,
  } = useMemo(
    () =>
      resolveModelConfig(
        (model.Provider ?? "").toLowerCase(),
        model.ServicePlatform as ServicePlatform,
        modelOptions,
      ),
    [model.Provider, model.ServicePlatform, modelOptions],
  );

  const schema = useMemo(
    () => (baseSchema as z.AnyZodObject).extend({ apiKey: z.string().optional() }),
    [baseSchema],
  );

  const initialValues = useMemo(() => {
    const defaults = defaultValues as DefaultValuesWithKnownKeys;
    return {
      ...defaultValues,
      model: model.ModelName ?? modelOptions[0]?.model ?? defaults.model ?? "--",
      url: model.BaseUrl ?? defaults.url ?? "",
      apiKey: model.ApiKey,
      organizationId: model.OpenAiOrganizationId ?? defaults.organizationId ?? "",
      projectId: model.OpenAiProjectId ?? defaults.projectId ?? "",
      deploymentName: model.DeploymentName ?? defaults.deploymentName ?? "",
    };
  }, [defaultValues, model, modelOptions]);

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: initialValues as z.infer<typeof schema>,
    mode: "onChange",
  });

  const originalApiKey = model.ApiKey ?? "";
  const [apiKeyEditable, setApiKeyEditable] = useState(false);
  const maskKey = (key: string) => {
    if (!key) return "";
    if (key.length <= 6) return key;
    return key.slice(0, 5) + "•••••" + key.slice(-3);
  };

  const onSubmitHandler = async (data: z.infer<typeof schema>) => {
    try {
      const rawApiKey = (data.apiKey ?? "") as string;
      const trimmedApiKey = rawApiKey.trim();

      const payload: IUpdateModelPayload & { model_name: string } = {
        project_key,
        display_name: model.DisplayName,
        base_url: data.url ?? model.BaseUrl ?? "",
        openai_organization_id: data.organizationId ?? model.OpenAiOrganizationId,
        openai_project_id: data.projectId ?? model.OpenAiProjectId,
        deployment_name: data.deploymentName ?? model.DeploymentName ?? undefined,
        is_active: model.IsActive,
        api_version: model.ApiVersion,
        model_name: model.ModelName ?? "",
      };

      if (trimmedApiKey && trimmedApiKey !== originalApiKey) payload.api_key = trimmedApiKey;

      const res = await mutateAsync({ modelId: model._id, payload });
      if (res.is_success) showSuccessToast({ description: "Model updated successfully." });
      else showErrorToast({ errors: res.detail });
      setEditKeyModalOpen(false);
    } catch (err) {
      showErrorToast({ errors: err instanceof Error ? err.message : String(err) });
    }
  };

  const selectedModel = form.watch("model");
  const selectedGoodName =
    modelOptions.find((o) => o.model === selectedModel)?.goodName ??
    modelOptions[0]?.goodName ??
    selectedModel ??
    "";

  return (
    <Dialog
      open={editKeyModalOpen}
      onOpenChange={(open) => {
        setEditKeyModalOpen(open);
        if (!open) form.reset(initialValues);
      }}
    >
      <DialogContent
        className="w-[calc(100%-2rem)] p-6 sm:mx-0 sm:w-[500px]"
        onOpenAutoFocus={(e) => e.preventDefault()}
      >
        <DialogHeader>
          <DialogTitle>Update {getProviderDisplayName(model.Provider)} Key</DialogTitle>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmitHandler)}>
            <div className="mt-4 flex flex-col gap-4">
              {fields.includes("model") && (
                <div className="flex flex-col gap-1">
                  <span className="text-sm font-medium">
                    Model <span className="text-red-500">*</span>
                  </span>
                  <div className="flex cursor-not-allowed items-center justify-between rounded-md border bg-muted px-3 py-2 text-sm font-normal text-foreground/80">
                    <span>{selectedGoodName}</span>
                  </div>
                  <input type="hidden" {...form.register("model")} value={selectedModel} />
                </div>
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
                          placeholder="Enter URL"
                          {...field}
                          disabled={
                            model.ServicePlatform === ServicePlatform.OFFICIAL_API ||
                            model.Provider?.toLowerCase() === "openrouter"
                          }
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
                      <FormLabel>API Key</FormLabel>
                      <div className="flex items-center justify-between gap-2 rounded-md border px-2 py-1">
                        <FormControl className="flex-1">
                          <Input
                            {...field}
                            value={
                              apiKeyEditable
                                ? ((field.value as string) ?? "")
                                : maskKey((field.value as string) ?? "")
                            }
                            onChange={(e) => { if (apiKeyEditable) field.onChange(e.target.value); }}
                            disabled={!apiKeyEditable}
                            placeholder={apiKeyEditable ? "Enter new API key" : ""}
                            className="inline-block w-full truncate border-none shadow-none focus-visible:ring-0"
                          />
                        </FormControl>
                        {!apiKeyEditable && (
                          <Button
                            type="button"
                            variant="ghost"
                            className="h-fit w-fit p-1"
                            onClick={() => { field.onChange(""); setApiKeyEditable(true); }}
                          >
                            <Pen className="h-4 w-4 text-foreground/60" />
                          </Button>
                        )}
                      </div>
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
                <Button variant="secondary" disabled={isPending}>Cancel</Button>
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
