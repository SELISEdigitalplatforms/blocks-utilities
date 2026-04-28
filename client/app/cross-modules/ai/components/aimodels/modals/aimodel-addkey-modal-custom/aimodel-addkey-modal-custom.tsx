import React, { Dispatch, SetStateAction } from "react";
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
import { Info, Trash2 } from "lucide-react";
import { transformToUniversal } from "@blocks-ai/utils/aimodel-form.utils";
import { useProjectStore } from "@/store/useProjectStore";
import { useCreateModel } from "@blocks-ai/hooks/use-aimodel";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

const CustomAddKeyFormSchema = z.object({
  model: z.string().trim().min(1, "Model name is required"),
  providerName: z.string().trim().min(1, "Provider name is required"),
  url: z.string().trim().url("Must be a valid URL"),
  apiKey: z.string().min(1, "API key is required"),
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
type FormSchema = z.infer<typeof CustomAddKeyFormSchema>;

const CustomAddKeyFormDefaultValue: FormSchema = {
  model: "",
  providerName: "Custom",
  url: "",
  apiKey: "",
  apiVersion: "",
  DefaultTemp: 0.3,
  MaxTokens: 10922,
  customHeaders: [
    { key: "", value: "" },
    { key: "", value: "" },
  ],
};

interface CustomModelAddKeyModalProps {
  addKeyModalOpen: boolean;
  setAddKeyModalOpen: Dispatch<SetStateAction<boolean>>;
}

export const CustomModelAddKeyModal = ({
  addKeyModalOpen,
  setAddKeyModalOpen,
}: CustomModelAddKeyModalProps) => {
  const provider = "CUSTOM";
  const project_key = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useCreateModel();

  const form = useForm<FormSchema>({
    defaultValues: CustomAddKeyFormDefaultValue,
    resolver: zodResolver(CustomAddKeyFormSchema),
    mode: "onChange",
  });

  const { control, handleSubmit } = form;

  const { fields, append, remove } = useFieldArray({
    control,
    name: "customHeaders" as const,
  });

  const onSubmitHandler = async (data: FormSchema) => {
    try {
      const headersObject = Object.fromEntries(
        (data.customHeaders || [])
          .filter((h) => h.key && h.value)
          .map((h) => [h.key!, h.value!]),
      );
      const payload = transformToUniversal(provider.toLowerCase(), project_key, {
        ...data,
        customHeaders: headersObject,
      });
      payload.model_name = payload.model_name.trim() ?? "Custom Model";
      payload.display_name = payload.model_name;
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
        if (!open) form.reset(CustomAddKeyFormDefaultValue);
      }}
    >
      <DialogContent className="w-[calc(100%-2rem)] p-6 sm:mx-0 sm:w-[720px]">
        <DialogHeader>
          <DialogTitle>Add custom model</DialogTitle>
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
                    <Input placeholder="Enter model name" {...field} />
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
                  <FormLabel>API Key</FormLabel>
                  <FormControl>
                    <Input placeholder="Enter API Key" {...field} />
                  </FormControl>
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
                  <div className="flex items-center justify-between">
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
                  </div>
                  <div className="mt-2 flex items-center gap-4">
                    <Slider
                      min={0}
                      max={2}
                      step={0.01}
                      value={[field.value ?? 0.3]}
                      onValueChange={(values: number[]) => field.onChange(values[0])}
                      className="flex-1"
                    />
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
                  <div className="flex items-center justify-between">
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
                  </div>
                  <div className="mt-2 flex items-center gap-4">
                    <Slider
                      min={1}
                      max={32768}
                      step={1}
                      value={[field.value ?? 10922]}
                      onValueChange={(values: number[]) => field.onChange(values[0])}
                      className="flex-1"
                    />
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
              <h4 className="text-sm font-medium">Custom Headers (Optional)</h4>
              <div className="mt-3 space-y-2">
                {fields.map((f, idx) => (
                  <div key={f.id} className="flex items-center gap-3">
                    <FormField
                      control={control}
                      name={`customHeaders.${idx}.key` as const}
                      render={({ field }) => (
                        <FormItem className="flex-1">
                          <FormControl>
                            <Input placeholder="Header Key" value={field.value ?? ""} onChange={field.onChange} onBlur={field.onBlur} name={field.name} ref={field.ref} />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                    <FormField
                      control={control}
                      name={`customHeaders.${idx}.value` as const}
                      render={({ field }) => (
                        <FormItem className="flex-1">
                          <FormControl>
                            <Input placeholder="Enter value" value={field.value ?? ""} onChange={field.onChange} onBlur={field.onBlur} name={field.name} ref={field.ref} />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                    <Button type="button" variant="ghost" size="icon" onClick={() => remove(idx)} className="text-medium-emphasis hover:text-red-600">
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                ))}
                <Button type="button" variant="outline" onClick={() => append({ key: "", value: "" })} className="mt-2">
                  + Add
                </Button>
              </div>
            </div>
            <DialogFooter className="sticky bottom-0 left-0 right-0 mt-4 flex items-center justify-end gap-3 border-t bg-background py-4">
              <DialogClose asChild>
                <Button variant="secondary" type="button" onClick={() => setAddKeyModalOpen(false)}>Cancel</Button>
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
