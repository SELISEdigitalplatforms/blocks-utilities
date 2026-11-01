import { useMemo, useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { AlertTriangle, CheckCircle2, Loader2, Send } from "lucide-react";
import { useForm, useWatch } from "react-hook-form";
import { Link, useParams } from "react-router";
import { ChipsInput, ChipsInputField, ChipsInputList } from "@/components/chip-input/chips-input";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Form, FormControl, FormDescription, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { SmsPageHeader } from "../components/sms-page-header";
import { DESTINATION_PATTERN, MAX_MESSAGE_LENGTH } from "../constants/sms.constants";
import { useSendSms, useSendSmsByTemplate, useSmsProviderConfiguration, useSmsTemplates } from "../hooks/use-sms";
import { createSendSmsSchema, type SendSmsFormValues } from "../schemas/sms.schema";
import { SmsRequestError } from "../services/sms.service";
import { applyServerErrors, countSegments } from "../utils/sms.util";
import { useT } from "@seliseblocks/genesis-os";

const SMS_SEND_PAGE_TEMPLATE = "SMS_SEND_PAGE.TEMPLATE";

const templateKey = (name: string, language: string) => `${name}\u0000${language}`;

export const SmsSendPage = () => {
  const { t, language: uiLanguage } = useT("utilities.sms");
  const schema = useMemo(() => createSendSmsSchema(), [uiLanguage, t]);
  const { itemId } = useParams();
  const { data: provider, isLoading: providerLoading } = useSmsProviderConfiguration();
  // ponytail: the picker shows the first 100 templates; switch to a searchable combobox if tenants outgrow it.
  const { data: templates } = useSmsTemplates({ page: 1, pageSize: 100 });
  const sendText = useSendSms();
  const sendTemplate = useSendSmsByTemplate();
  const [result, setResult] = useState<{ messageId: string; recipients: number } | null>(null);
  const [bannerErrors, setBannerErrors] = useState<string[]>([]);

  const form = useForm<SendSmsFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      mode: "text",
      destinationNumbers: [],
      messageText: "",
      templateName: "",
      language: "",
      dataContext: {},
      correlationId: "",
    },
  });
  const [mode, messageText, templateName, language] = useWatch({
    control: form.control,
    name: ["mode", "messageText", "templateName", "language"],
  });
  const selectedTemplate = templates?.items.find((t) => t.name === templateName && t.language === language);
  const { segments, encoding } = countSegments(messageText ?? "");
  const isPending = sendText.isPending || sendTemplate.isPending;

  const submit = async (values: SendSmsFormValues) => {
    setBannerErrors([]);
    setResult(null);

    if (values.mode === "template") {
      const missing = (selectedTemplate?.placeholders ?? []).filter((key) => !values.dataContext[key]?.trim());
      if (missing.length) {
        missing.forEach((key) => form.setError(`dataContext.${key}`, { message: t("SMS_SEND_PAGE.REQUIRED_BY_THE_TEMPLATE") }));
        return;
      }
    }

    const correlationId = values.correlationId || undefined;
    try {
      const response =
        values.mode === "text"
          ? await sendText.mutateAsync({ destinationNumbers: values.destinationNumbers, messageText: values.messageText, correlationId })
          : await sendTemplate.mutateAsync({
              destinationNumbers: values.destinationNumbers,
              templateName: values.templateName,
              language: values.language,
              dataContext: values.dataContext,
              correlationId,
            });

      setResult({ messageId: response.messageId ?? "", recipients: values.destinationNumbers.length });
    } catch (error) {
      if (error instanceof SmsRequestError) {
        const unplaced = applyServerErrors(
          error.fieldErrors,
          ["destinationNumbers", "messageText", "templateName", "language", "correlationId"],
          form.setError,
        );
        const hasFieldErrors = Object.keys(error.fieldErrors).length > 0;
        const fallbackBanner = hasFieldErrors ? [] : [error.message];
        setBannerErrors(unplaced.length ? unplaced : fallbackBanner);
      } else {
        setBannerErrors([t("SMS_SEND_PAGE.SMS_NOT_SENT")]);
      }
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SmsPageHeader
        title={t("SMS_SEND_PAGE.SEND_SMS")}
        description={t("SMS_SEND_PAGE.SEND_A_MESSAGE_OR_A")}
        icon={<Send className="h-6 w-6" />}
      />

      {!providerLoading && !provider && (
        <div role="alert" className="flex items-start gap-3 rounded-xl border border-amber-300/50 bg-amber-50 p-4 text-sm text-amber-900 dark:bg-amber-950/30 dark:text-amber-200">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            {t("SMS_SEND_PAGE.NO_PROVIDER_CONFIGURED")}{" "}
            <Link className="font-medium underline" to={`/app/${itemId ?? ""}/sms/provider`}>
              {t("SMS_SEND_PAGE.SET_ONE_UP")}
            </Link>
            .
          </p>
        </div>
      )}

      <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card className="rounded-xl p-5 sm:p-6">
          <Form {...form}>
            <form className="space-y-6" onSubmit={form.handleSubmit(submit)} noValidate>
              <FormField
                control={form.control}
                name="destinationNumbers"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("SMS_SEND_PAGE.RECIPIENTS")}</FormLabel>
                    <FormControl>
                      <ChipsInput
                        value={field.value}
                        onChange={field.onChange}
                        validatorRegex={DESTINATION_PATTERN}
                        validatorRegexErrorMessage={t("SMS_SEND_PAGE.DESTINATION_DIGITS")}
                      >
                        <ChipsInputList />
                        <ChipsInputField />
                      </ChipsInput>
                    </FormControl>
                    <FormDescription>{t("SMS_SEND_PAGE.INTERNATIONAL_FORMAT_E_G")}</FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <Tabs value={mode} onValueChange={(value) => form.setValue("mode", value as SendSmsFormValues["mode"])}>
                <TabsList>
                  <TabsTrigger value="text">{t("SMS_SEND_PAGE.MESSAGE")}</TabsTrigger>
                  <TabsTrigger value="template">{t(SMS_SEND_PAGE_TEMPLATE)}</TabsTrigger>
                </TabsList>
              </Tabs>

              {mode === "text" ? (
                <FormField
                  control={form.control}
                  name="messageText"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>{t("SMS_SEND_PAGE.MESSAGE")}</FormLabel>
                      <FormControl>
                        <Textarea {...field} rows={5} maxLength={MAX_MESSAGE_LENGTH} />
                      </FormControl>
                      <FormDescription>
                        {t("SMS_SEND_PAGE.MESSAGE_STATS", { length: messageText.length, max: MAX_MESSAGE_LENGTH, encoding, count: segments })}
                      </FormDescription>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              ) : (
                <div className="space-y-5">
                  <FormField
                    control={form.control}
                    name="templateName"
                    render={() => (
                      <FormItem>
                        <FormLabel>{t(SMS_SEND_PAGE_TEMPLATE)}</FormLabel>
                        <Select
                          value={templateName ? templateKey(templateName, language) : ""}
                          onValueChange={(value) => {
                            const [name, lang] = value.split("\u0000");
                            form.setValue("templateName", name, { shouldValidate: true });
                            form.setValue("language", lang);
                            form.setValue("dataContext", {});
                          }}
                        >
                          <FormControl>
                            <SelectTrigger aria-label={t(SMS_SEND_PAGE_TEMPLATE)}>
                              <SelectValue placeholder={templates?.items.length ? t("SMS_SEND_PAGE.CHOOSE_A_TEMPLATE") : t("SMS_SEND_PAGE.NO_TEMPLATES_YET")} />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            {templates?.items.map((template) => (
                              <SelectItem key={template.itemId} value={templateKey(template.name, template.language)}>
                                {template.name} · {template.language}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  {selectedTemplate && (
                    <div className="space-y-4 rounded-xl border bg-muted/20 p-4">
                      <p className="whitespace-pre-wrap text-sm text-muted-foreground">{selectedTemplate.body}</p>
                      {selectedTemplate.placeholders.length > 0 && (
                        <div className="grid gap-4 sm:grid-cols-2">
                          {selectedTemplate.placeholders.map((key) => (
                            <FormField
                              key={key}
                              control={form.control}
                              name={`dataContext.${key}`}
                              render={({ field }) => (
                                <FormItem>
                                  <FormLabel>{key}</FormLabel>
                                  <FormControl>
                                    <Input {...field} value={field.value ?? ""} />
                                  </FormControl>
                                  <FormMessage />
                                </FormItem>
                              )}
                            />
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}

              <FormField
                control={form.control}
                name="correlationId"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("SMS_SEND_PAGE.CORRELATION_ID_OPTIONAL")}</FormLabel>
                    <FormControl>
                      <Input {...field} placeholder={t("SMS_SEND_PAGE.GENERATED_WHEN_EMPTY")} />
                    </FormControl>
                    <FormDescription>{t("SMS_SEND_PAGE.CARRIED_THROUGH_LOGS_AND_STATUS")}</FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />

              {bannerErrors.length > 0 && (
                <div role="alert" className="rounded-lg border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
                  {bannerErrors.map((message) => (
                    <p key={message}>{message}</p>
                  ))}
                </div>
              )}

              <div className="flex justify-end border-t pt-6">
                <Button type="submit" disabled={isPending}>
                  {isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Send className="mr-2 h-4 w-4" />}
                  {isPending ? t("SMS_SEND_PAGE.SENDING") : t("SMS_SEND_PAGE.SEND")}
                </Button>
              </div>
            </form>
          </Form>
        </Card>

        <aside className="space-y-5">
          {result && (
            <output className="block">
              <Card className="rounded-xl">
                <div className="flex items-start gap-3">
                  <CheckCircle2 className="mt-0.5 h-5 w-5 text-green-600" />
                  <div className="min-w-0">
                    <h2 className="font-semibold">{t("SMS_SEND_PAGE.QUEUED")}</h2>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {t("SMS_SEND_PAGE.ACCEPTED_FOR_RECIPIENTS", { count: result.recipients })}
                    </p>
                    <code className="mt-2 block break-all rounded-md bg-muted px-2 py-1.5 text-xs">{result.messageId}</code>
                  </div>
                </div>
              </Card>
            </output>
          )}
          <Card className="rounded-xl">
            <h2 className="font-semibold">{t("SMS_SEND_PAGE.BEFORE_A_MESSAGE_IS_QUEUED")}</h2>
            <ul className="mt-2 list-disc space-y-1 pl-5 text-sm leading-6 text-muted-foreground">
              <li>{t("SMS_SEND_PAGE.THE_SPAM_FILTER_CHECKS_IT")}</li>
              <li>{t("SMS_SEND_PAGE.TENANT_AND_PER_RECIPIENT_RATE")}</li>
              <li>{t("SMS_SEND_PAGE.DUPLICATE_NUMBERS_ARE_SENT_TO")}</li>
            </ul>
          </Card>
        </aside>
      </div>
    </main>
  );
};
