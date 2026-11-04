import { useMemo, useState } from "react";
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
import { createSmsTemplateSchema, type SmsTemplateFormValues } from "../schemas/sms.schema";
import { SmsRequestError } from "../services/sms.service";
import { applyServerErrors, countSegments, extractPlaceholders } from "../utils/sms.util";
import { useT } from "@seliseblocks/genesis-os";

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
  const { t, language } = useT("utilities.sms");
  const schema = useMemo(() => createSmsTemplateSchema(), [language, t]);
  const { mutateAsync, isPending } = useSaveSmsTemplate();
  const [bannerError, setBannerError] = useState<string | null>(null);
  const form = useForm<SmsTemplateFormValues>({
    resolver: zodResolver(schema),
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
      toast({ variant: "success", title: template ? t("SMS_TEMPLATE_DIALOG.TEMPLATE_UPDATED") : t("SMS_TEMPLATE_DIALOG.TEMPLATE_CREATED"), description: `${saved.name} (${saved.language})` });
      onClose();
    } catch (error) {
      if (error instanceof SmsRequestError) {
        const unplaced = applyServerErrors(error.fieldErrors, ["name", "language", "body"], form.setError);
        setBannerError(unplaced[0] ?? (Object.keys(error.fieldErrors).length ? null : error.message));
      } else {
        setBannerError(t("SMS_TEMPLATE_DIALOG.TEMPLATE_NOT_SAVED"));
      }
    }
  };

  return (
    <DialogContent className="max-w-2xl">
      <DialogHeader>
        <DialogTitle>{template ? t("SMS_TEMPLATE_DIALOG.EDIT_TEMPLATE") : t("SMS_TEMPLATE_DIALOG.NEW_TEMPLATE")}</DialogTitle>
        <DialogDescription>
          {t("SMS_TEMPLATE_DIALOG.USE_KEY_FOR_VALUES", { token: "{{key}}" })}
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
                  <FormLabel>{t("common:NAME")}</FormLabel>
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
                  <FormLabel>{t("SMS_TEMPLATE_DIALOG.LANGUAGE")}</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="en-US" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          </div>
          <FormDescription className="-mt-3">{t("SMS_TEMPLATE_DIALOG.NAME_AND_LANGUAGE_TOGETHER_ARE")}</FormDescription>

          <FormField
            control={form.control}
            name="body"
            render={({ field }) => (
              <FormItem>
                <FormLabel>{t("SMS_TEMPLATE_DIALOG.MESSAGE")}</FormLabel>
                <FormControl>
                  <Textarea {...field} rows={6} maxLength={MAX_MESSAGE_LENGTH} placeholder={t("SMS_TEMPLATE_DIALOG.HI_YOUR_CODE_IS")} />
                </FormControl>
                <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
                  <span>
                    {t("SMS_TEMPLATE_DIALOG.MESSAGE_STATS", { length: body.length, max: MAX_MESSAGE_LENGTH, encoding, count: segments })}
                  </span>
                  <span className="flex flex-wrap items-center gap-1">
                    {placeholders.length ? (
                      placeholders.map((key) => (
                        <Badge key={key} variant="info">
                          {key}
                        </Badge>
                      ))
                    ) : (
                      <span>{t("SMS_TEMPLATE_DIALOG.NO_PLACEHOLDERS")}</span>
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
              {t("common:CANCEL")}
            </Button>
            <Button type="submit" disabled={isPending}>
              {isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}
              {template ? t("common:SAVE_CHANGES") : t("SMS_TEMPLATE_DIALOG.CREATE_TEMPLATE")}
            </Button>
          </DialogFooter>
        </form>
      </Form>
    </DialogContent>
  );
};
