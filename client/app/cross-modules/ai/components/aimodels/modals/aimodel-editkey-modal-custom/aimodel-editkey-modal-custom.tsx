import React, { Dispatch, SetStateAction, useMemo, useState } from "react";
import { useForm, useFieldArray } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import * as z from "zod";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { Slider } from "@/components/ui-kits/slider/slider";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { Info, Trash2, Pen } from "lucide-react";
import { IModelInfo, IUpdateModelPayload } from "@blocks-ai/types/aimodel.service.type";
import { useProjectStore } from "@/store/useProjectStore";
import { useUpdateModel } from "@blocks-ai/hooks/use-aimodel";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

const CustomEditKeyFormSchema = z.object({
  model: z.string().trim().min(1, "Model name is required"),
  providerName: z.string().trim().min(1, "Provider name is required"),
  url: z.string().trim().url("Must be a valid URL"),
  apiKey: z.string().optional(),
  apiVersion: z.string().trim().min(1, "API version is required"),
  DefaultTemp: z.number().min(0).max(2),
  MaxTokens: z.number().min(1).max(32768),
  customHeaders: z
    .array(
      z.object({
        key: z.string().trim().optional().nullable(),
        value: z.string().trim().optional().nullable(),
      }),
    )
    .default([]),
});

type FormSchema = z.infer<typeof CustomEditKeyFormSchema>;

interface CustomModelEditKeyModalProps {
  editKeyModalOpen: boolean;
  setEditKeyModalOpen: Dispatch<SetStateAction<boolean>>;
  model: IModelInfo;
}

export const CustomModelEditKeyModal = ({
  editKeyModalOpen,
  setEditKeyModalOpen,
  model,
}: CustomModelEditKeyModalProps) => {
  const project_key = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useUpdateModel();

  const initialValues: FormSchema = useMemo(() => {
    const customParams = (model.CustomParameters ?? {}) as Record<string, unknown>;
    const defaultTemp = typeof customParams.DefaultTemp === "number" ? customParams.DefaultTemp : 0.3;
    const maxTokens = typeof customParams.MaxTokens === "number" ? customParams.MaxTokens : 10922;
    const headerEntries = Object.entries(model.CustomHeaders ?? {}) as [string, string][];
    const headerArray =
      headerEntries.length > 0
        ? headerEntries.map(([key, value]) => ({ key, value }))
        : [{ key: "", value: "" }];

    return {
      model: model.ModelName || "",
      providerName: model.Provider || "",
      url: model.BaseUrl || "",
      apiKey: model.ApiKey,
      apiVersion: model.ApiVersion || "",
      DefaultTemp: defaultTemp,
      MaxTokens: maxTokens,
      customHeaders: headerArray,
    };
  }, [model]);

  const form = useForm<FormSchema>({
    defaultValues: initialValues,
    resolver: zodResolver(CustomEditKeyFormSchema),
    mode: "onChange",
  });

  const { control, handleSubmit } = form;

  const { fields, append, remove } = useFieldArray({
    control,
    name: "customHeaders" as const,
  });

  const [apiKeyEditable, setApiKeyEditable] = useState(false);
  const maskKey = (key: string) => {
    if (!key) return "";
    if (key.length <= 6) return key;
    return key.slice(0, 5) + "•••••" + key.slice(-3);
  };

  const onSubmitHandler = async (data: FormSchema) => {
    try {
      const trimmedApiKey = (data.apiKey ?? "").trim();
      const headersObject = Object.fromEntries(
        (data.customHeaders || [])
          .filter((h) => h.key && h.value)
          .map((h) => [h.key as string, h.value as string]),
      );
      const payload: IUpdateModelPayload & { model_name: string } = {
        project_key,
        display_name: model.DisplayName || data.model,
        base_url: data.url || model.BaseUrl || "",
        is_active: model.IsActive,
        api_version: data.apiVersion || model.ApiVersion,
        custom_parameters: {
          ...(model.CustomParameters ?? {}),
          DefaultTemp: data.DefaultTemp,
          MaxTokens: data.MaxTokens,
        },
        custom_headers: headersObject,
        model_name: model.ModelName || data.model,
      };
      if (trimmedApiKey) payload.api_key = trimmedApiKey;
      const res = await mutateAsync({ modelId: model._id, payload });
      if (res.is_success) {
        showSuccessToast({ description: "Model updated successfully." });
      } else {
        showErrorToast({ errors: res.detail });
      }
      setEditKeyModalOpen(false);
    } catch (err) {
      showErrorToast({ errors: err instanceof Error ? err.message : String(err) });
    }
  };

  return (
    <Dialog
      open={editKeyModalOpen}
      onOpenChange={(v) => {
        setEditKeyModalOpen(v);
        if (!v) form.reset(initialValues);
      }}
    >
      <DialogContent className="w-[calc(100%-2rem)] p-6 sm:mx-0 sm:w-[720px]">
        <DialogHeader>
          <DialogTitle>Update Custom Model</DialogTitle>
        </DialogHeader>
        <Form {...form}>
          <form
            onSubmit={handleSubmit(onSubmitHandler)}
            className="-mr-3 max-h-[80vh] space-y-6 overflow-y-auto pl-1 pr-4 [&::-webkit-scrollbar-thumb]:rounded-full [&::-webkit-scrollbar-thumb]:bg-gray-200 [&::-webkit-scrollbar]:w-1.5"
          >
            <FormField
              control={control}
              name="model"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Model Name <span className="text-red-500">*</span></FormLabel>
                  <FormControl>
                    <Input placeholder="Enter model name" {...field} disabled />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <div className="rounded border border-yellow-300 bg-yellow-50 p-3 text-sm text-yellow-900 dark:border-blue-600 dark:bg-blue-950/60 dark:text-blue-200">
              Connect any model via an OpenAI-compatible API
            </div>
            <FormField
              control={control}
              name="url"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>API URL <span className="text-red-500">*</span></FormLabel>
                  <FormControl>
                    <Input placeholder="Enter API URL" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={control}
              name="apiKey"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>API Key [Optional]</FormLabel>
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
                        placeholder={apiKeyEditable ? "Enter new API key" : "API Key"}
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
            <FormField
              control={control}
              name="apiVersion"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>API Version <span className="text-red-500">*</span></FormLabel>
                  <FormControl>
                    <Input placeholder="API Version" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={control}
              name="DefaultTemp"
              render={({ field }) => (
                <FormItem>
                  <FormLabel className="flex items-center gap-2 text-base font-medium">
                    Temperature
                    <TooltipProvider>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Info className="h-4 w-4 text-muted-foreground" />
                        </TooltipTrigger>
                        <TooltipContent>
                          Lower values make LLM responses more accurate and stable, while higher values make them more random and creative.
                        </TooltipContent>
                      </Tooltip>
                    </TooltipProvider>
                  </FormLabel>
                  <div className="mt-2 flex items-center gap-4">
                    <Slider min={0} max={2} step={0.01} value={[field.value ?? 0.3]} onValueChange={(v: number[]) => field.onChange(v[0])} className="flex-1" />
                    <div className="flex min-w-[80px] items-center gap-1 rounded-lg border bg-background px-2 py-1">
                      <span className="font-bold text-high-emphasis">{(field.value ?? 0.3).toFixed(2)}</span>
                      <span className="text-medium-emphasis">/ 2</span>
                    </div>
                  </div>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={control}
              name="MaxTokens"
              render={({ field }) => (
                <FormItem>
                  <FormLabel className="flex items-center gap-2 text-base font-medium">
                    Maximum tokens
                    <TooltipProvider>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Info className="h-4 w-4 text-muted-foreground" />
                        </TooltipTrigger>
                        <TooltipContent>
                          Used to roughly control the maximum number of tokens in LLM responses (1 token ≈ 1 English short word).
                        </TooltipContent>
                      </Tooltip>
                    </TooltipProvider>
                  </FormLabel>
                  <div className="mt-2 flex items-center gap-4">
                    <Slider min={1} max={32768} step={1} value={[field.value ?? 10922]} onValueChange={(v: number[]) => field.onChange(v[0])} className="flex-1" />
                    <div className="flex min-w-[100px] items-center gap-1 rounded-lg border bg-background px-2 py-1">
                      <span className="font-bold text-high-emphasis">{field.value ?? 10922}</span>
                      <span className="text-medium-emphasis">/ 32768</span>
                    </div>
                  </div>
                  <FormMessage />
                </FormItem>
              )}
            />
            <div>
              <div className="flex items-center justify-between">
                <h4 className="text-sm font-medium">Custom Headers (Optional)</h4>
                <span className="text-sm text-medium-emphasis">Key / Value pairs</span>
              </div>
              <div className="mt-3 space-y-2">
                {fields.map((f, idx) => (
                  <div key={f.id} className="flex items-center gap-3">
                    <FormField control={control} name={`customHeaders.${idx}.key` as const} render={({ field }) => (
                      <FormItem className="flex-1">
                        <FormControl>
                          <Input placeholder="Header Key" value={field.value ?? ""} onChange={field.onChange} onBlur={field.onBlur} name={field.name} ref={field.ref} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )} />
                    <FormField control={control} name={`customHeaders.${idx}.value` as const} render={({ field }) => (
                      <FormItem className="flex-1">
                        <FormControl>
                          <Input placeholder="Enter value" value={field.value ?? ""} onChange={field.onChange} onBlur={field.onBlur} name={field.name} ref={field.ref} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )} />
                    <Button type="button" variant="ghost" size="icon" onClick={() => remove(idx)} className="text-medium-emphasis hover:text-red-600">
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                ))}
                <Button type="button" variant="outline" onClick={() => append({ key: "", value: "" })} className="mt-2">
                  + Add ({fields.length})
                </Button>
              </div>
            </div>
            <DialogFooter className="mt-6 flex items-center justify-end gap-3">
              <DialogClose asChild>
                <Button variant="secondary" type="button" onClick={() => setEditKeyModalOpen(false)} disabled={isPending}>Cancel</Button>
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
