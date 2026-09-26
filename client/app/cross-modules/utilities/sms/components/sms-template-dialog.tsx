import { useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Save } from "lucide-react";
import { useForm, useWatch } from "react-hook-form";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Form, FormControl, FormDescription, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { toast } from "@/hooks/use-toast";
import { MAX_MESSAGE_LENGTH } from "../constants/sms.constants";
import { useSaveSmsTemplate } from "../hooks/use-sms";
import type { SmsTemplate } from "../models/sms.model";
import { smsTemplateSchema, type SmsTemplateFormValues } from "../schemas/sms.schema";
import { SmsRequestError } from "../services/sms.service";
import { applyServerErrors, countSegments, extractPlaceholders } from "../utils/sms.util";

interface SmsTemplateDialogProps {
  open: boolean;
  /** Null creates a template. */
  template: SmsTemplate | null;
  onOpenChange: (open: boolean) => void;
}

export const SmsTemplateDialog = ({ open, template, onOpenChange }: SmsTemplateDialogProps) => (
  <Dialog open={open} onOpenChange={onOpenChange}>
    {open && (
      // Remounted per open, so the form always starts from the template being edited.
      <SmsTemplateDialogContent key={template?.itemId ?? "new"} template={template} onClose={() => onOpenChange(false)} />
    )}
  </Dialog>
);

const SmsTemplateDialogContent = ({ template, onClose }: { template: SmsTemplate | null; onClose: () => void }) => {
  const { mutateAsync, isPending } = useSaveSmsTemplate();
  const [bannerError, setBannerError] = useState<string | null>(null);
  const form = useForm<SmsTemplateFormValues>({
    resolver: zodResolver(smsTemplateSchema),
    mode: "onBlur",
    defaultValues: {
      name: template?.name ?? "",
      language: template?.language ?? "en-US",
      body: template?.body ?? "",
    },
  });
  const body = useWatch({ control: form.control, name: "body" }) ?? "";
  const placeholders = extractPlaceholders(body);
  const { segments, encoding } = countSegments(body);

  const submit = async (values: SmsTemplateFormValues) => {
    setBannerError(null);
    try {
      const saved = await mutateAsync({ templateId: template?.itemId, ...values });
      toast({ variant: "success", title: template ? "Template updated" : "Template created", description: `${saved.name} (${saved.language})` });
      onClose();
    } catch (error) {
      if (error instanceof SmsRequestError) {
        const unplaced = applyServerErrors(error.fieldErrors, ["name", "language", "body"], form.setError);
        setBannerError(unplaced[0] ?? (Object.keys(error.fieldErrors).length ? null : error.message));
      } else {
        setBannerError("The template could not be saved.");
      }
    }
  };

  return (
    <DialogContent className="max-w-2xl">
      <DialogHeader>
        <DialogTitle>{template ? "Edit template" : "New template"}</DialogTitle>
        <DialogDescription>
          Use <code className="rounded bg-muted px-1">{"{{key}}"}</code> for values supplied when sending. A send is refused if any
          value is missing.
        </DialogDescription>
      </DialogHeader>

      <Form {...form}>
        <form className="space-y-5" onSubmit={form.handleSubmit(submit)} noValidate>
          <div className="grid gap-5 sm:grid-cols-[minmax(0,1fr)_10rem]">
            <FormField
              control={form.control}
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Name</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="otp-login" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="language"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Language</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="en-US" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          </div>
          <FormDescription className="-mt-3">Name and language together are unique; senders pick a template by both.</FormDescription>

          <FormField
            control={form.control}
            name="body"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Message</FormLabel>
                <FormControl>
                  <Textarea {...field} rows={6} maxLength={MAX_MESSAGE_LENGTH} placeholder="Hi {{name}}, your code is {{code}}." />
                </FormControl>
                <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
                  <span>
                    {body.length}/{MAX_MESSAGE_LENGTH} characters · {encoding} · about {segments} segment{segments === 1 ? "" : "s"}
                  </span>
                  <span className="flex flex-wrap items-center gap-1">
                    {placeholders.length ? (
                      placeholders.map((key) => (
                        <Badge key={key} variant="info">
                          {key}
                        </Badge>
                      ))
                    ) : (
                      <span>No placeholders</span>
                    )}
                  </span>
                </div>
                <FormMessage />
              </FormItem>
            )}
          />

          {bannerError && (
            <div role="alert" className="rounded-lg border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
              {bannerError}
            </div>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={isPending}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending}>
              {isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}
              {template ? "Save changes" : "Create template"}
            </Button>
          </DialogFooter>
        </form>
      </Form>
    </DialogContent>
  );
};
