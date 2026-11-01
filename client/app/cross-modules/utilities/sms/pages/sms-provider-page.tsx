import { useEffect, useMemo, useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";
import { AlertCircle, KeyRound, Loader2, MessageSquare, RefreshCw, Save, ShieldCheck, Webhook } from "lucide-react";
import { useForm, useWatch, type Control } from "react-hook-form";
import { ChipsInput, ChipsInputField, ChipsInputList } from "@/components/chip-input/chips-input";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { PasswordInput } from "@/components/password-input";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Switch } from "@/components/ui-kits/switch/switch";
import { toast } from "@/hooks/use-toast";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { SmsFormSection, SmsPageHeader } from "../components/sms-page-header";
import {
  COUNTRY_CODE_PATTERN,
  SMS_PROVIDER_OPTIONS,
  SmsProviderType,
  SmsUrlPolicy,
} from "../constants/sms.constants";
import { useSaveSmsProviderConfiguration, useSmsProviderConfiguration } from "../hooks/use-sms";
import type { SaveSmsProviderConfigurationRequest, SmsProviderConfiguration } from "../models/sms.model";
import { createSmsProviderSchema, type SmsProviderFormValues } from "../schemas/sms.schema";
import { SmsRequestError } from "../services/sms.service";
import { applyServerErrors } from "../utils/sms.util";

const UTILITIES_SMS_MODULE = "utilities.sms";
const SMS_PROVIDER_PAGE_PROVIDER = "SMS_PROVIDER_PAGE.PROVIDER";

const DEFAULTS: SmsProviderFormValues = {
  name: "Default",
  providerType: SmsProviderType.Twilio,
  isEnabled: true,
  isDefault: true,
  senderNumber: "",
  senderName: "",
  senderNameExcludedPrefixes: ["+1"],
  accountId: "",
  apiKey: "",
  messagingProfileId: "",
  webhookPublicKey: "",
  statusCallbackBaseUrl: "",
  maxRetryAttempts: 5,
  deliveryCheckDelayMinutes: 10,
  rateLimit: { tenantMaxPerWindow: 300, tenantWindowSeconds: 60, recipientMaxPerWindow: 5, recipientWindowSeconds: 300 },
  spamFilter: {
    enabled: true,
    maxRecipients: 100,
    maxMessageLength: 1000,
    urlPolicy: SmsUrlPolicy.Flag,
    blockedTerms: ["password", "bank", "wallet", "crypto"],
  },
};

const FORM_FIELDS = [
  "name", "providerType", "isEnabled", "isDefault", "senderNumber", "senderName", "senderNameExcludedPrefixes",
  "accountId", "apiKey", "messagingProfileId", "webhookPublicKey", "statusCallbackBaseUrl", "maxRetryAttempts",
  "deliveryCheckDelayMinutes", "rateLimit.tenantMaxPerWindow", "rateLimit.tenantWindowSeconds",
  "rateLimit.recipientMaxPerWindow", "rateLimit.recipientWindowSeconds", "spamFilter.enabled",
  "spamFilter.maxRecipients", "spamFilter.maxMessageLength", "spamFilter.urlPolicy", "spamFilter.blockedTerms",
] as const;

const toFormValues = (configuration: SmsProviderConfiguration): SmsProviderFormValues => ({
  name: configuration.name,
  providerType: configuration.providerType,
  isEnabled: configuration.isEnabled,
  isDefault: configuration.isDefault,
  senderNumber: configuration.senderNumber ?? "",
  senderName: configuration.senderName ?? "",
  senderNameExcludedPrefixes: configuration.senderNameExcludedPrefixes ?? [],
  accountId: configuration.accountId ?? "",
  // Never returned by the Api; empty means "keep the stored key".
  apiKey: "",
  messagingProfileId: configuration.messagingProfileId ?? "",
  webhookPublicKey: configuration.webhookPublicKey ?? "",
  statusCallbackBaseUrl: configuration.statusCallbackBaseUrl ?? "",
  maxRetryAttempts: configuration.maxRetryAttempts,
  deliveryCheckDelayMinutes: configuration.deliveryCheckDelayMinutes,
  rateLimit: configuration.rateLimit,
  spamFilter: configuration.spamFilter,
});

const optional = (value: string) => value.trim() || undefined;

export const toSaveRequest = (
  values: SmsProviderFormValues,
  configurationId: string | undefined,
): SaveSmsProviderConfigurationRequest => {
  const isTwilio = values.providerType === SmsProviderType.Twilio;
  return {
    configurationId,
    name: values.name.trim(),
    providerType: values.providerType,
    isEnabled: values.isEnabled,
    isDefault: values.isDefault,
    senderNumber: optional(values.senderNumber),
    senderName: optional(values.senderName),
    senderNameExcludedPrefixes: values.senderNameExcludedPrefixes,
    // Provider-specific fields are sent only for their provider, so switching provider does not
    // carry the other one's identifiers along.
    accountId: isTwilio ? optional(values.accountId) : undefined,
    apiKey: optional(values.apiKey),
    messagingProfileId: isTwilio ? undefined : optional(values.messagingProfileId),
    webhookPublicKey: isTwilio ? undefined : optional(values.webhookPublicKey),
    statusCallbackBaseUrl: optional(values.statusCallbackBaseUrl),
    maxRetryAttempts: values.maxRetryAttempts,
    deliveryCheckDelayMinutes: values.deliveryCheckDelayMinutes,
    rateLimit: values.rateLimit,
    spamFilter: values.spamFilter,
  };
};

export const SmsProviderPage = () => {
  const { t } = useT(UTILITIES_SMS_MODULE);
  const { data: configuration, error, isError, isLoading, refetch } = useSmsProviderConfiguration();

  if (isLoading) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8" aria-label={t("SMS_PROVIDER_PAGE.LOADING_SMS_PROVIDER")}>
        <Skeleton className="h-36 rounded-2xl" />
        <Skeleton className="h-[32rem] rounded-xl" />
      </main>
    );
  }

  if (isError) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <Card className="flex min-h-72 flex-col items-center justify-center rounded-xl px-6 text-center">
          <span className="rounded-full bg-destructive/10 p-4 text-destructive">
            <AlertCircle className="h-7 w-7" />
          </span>
          <h2 className="mt-4 text-lg font-semibold">{t("SMS_PROVIDER_PAGE.SMS_PROVIDER_UNAVAILABLE")}</h2>
          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            {error instanceof Error ? error.message : t("SMS_PROVIDER_PAGE.TRY_AGAIN_2")}
          </p>
          <Button className="mt-5" variant="outline" onClick={() => refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("SMS_PROVIDER_PAGE.TRY_AGAIN")}
          </Button>
        </Card>
      </main>
    );
  }

  // Keyed on the stored version so a save elsewhere, or the first save here, re-seeds the form.
  return <SmsProviderForm key={configuration?.lastUpdatedDate ?? "new"} configuration={configuration ?? null} />;
};

const SmsProviderForm = ({ configuration }: { configuration: SmsProviderConfiguration | null }) => {
  const { t, language } = useT(UTILITIES_SMS_MODULE);
  const urlPolicyOptions = [
    {
      value: SmsUrlPolicy.Allow,
      label: t("SMS_PROVIDER_PAGE.URL_POLICY_ALLOW"),
      hint: t("SMS_PROVIDER_PAGE.URL_POLICY_ALLOW_HINT"),
    },
    {
      value: SmsUrlPolicy.Flag,
      label: t("SMS_PROVIDER_PAGE.URL_POLICY_FLAG"),
      hint: t("SMS_PROVIDER_PAGE.URL_POLICY_FLAG_HINT"),
    },
    {
      value: SmsUrlPolicy.Block,
      label: t("SMS_PROVIDER_PAGE.URL_POLICY_BLOCK"),
      hint: t("SMS_PROVIDER_PAGE.URL_POLICY_BLOCK_HINT"),
    },
  ];
  const isCreate = !configuration?.hasApiKey;
  const schema = useMemo(() => createSmsProviderSchema(isCreate), [isCreate, language, t]);
  const [bannerErrors, setBannerErrors] = useState<string[]>([]);
  const { mutateAsync, isPending } = useSaveSmsProviderConfiguration();

  const form = useForm<SmsProviderFormValues>({
    resolver: zodResolver(schema),
    mode: "onBlur",
    defaultValues: configuration ? toFormValues(configuration) : DEFAULTS,
  });
  const providerType = useWatch({ control: form.control, name: "providerType" });
  const spamEnabled = useWatch({ control: form.control, name: "spamFilter.enabled" });
  const [senderNumber, senderName, excludedPrefixes] = useWatch({
    control: form.control,
    name: ["senderNumber", "senderName", "senderNameExcludedPrefixes"],
  });
  // A name with no number to fall back on fails outright for every excluded country.
  const nameWithoutFallback = !!senderName?.trim() && !senderNumber?.trim() && excludedPrefixes.length > 0;
  const isTwilio = providerType === SmsProviderType.Twilio;

  useEffect(() => {
    // Provider-specific fields keep their errors from the other provider otherwise.
    form.clearErrors(["accountId", "messagingProfileId", "webhookPublicKey"]);
  }, [form, providerType]);

  const submit = async (values: SmsProviderFormValues) => {
    setBannerErrors([]);
    try {
      await mutateAsync(toSaveRequest(values, configuration?.itemId));
      toast({
        variant: "success",
        title: t("SMS_PROVIDER_PAGE.SMS_PROVIDER_SAVED"),
        description: values.apiKey ? t("SMS_PROVIDER_PAGE.THE_PROVIDER_KEY_WAS_STORED") : undefined,
      });
    } catch (saveError) {
      if (saveError instanceof SmsRequestError) {
        const unplaced = applyServerErrors(saveError.fieldErrors, FORM_FIELDS, form.setError);
        setBannerErrors(unplaced.length ? unplaced : Object.keys(saveError.fieldErrors).length ? [] : [saveError.message]);
      } else {
        setBannerErrors([t("SMS_PROVIDER_PAGE.CONFIGURATION_NOT_SAVED")]);
      }
    }
  };

  const idleSaveLabel = configuration ? t("common:SAVE_CHANGES") : t("SMS_PROVIDER_PAGE.SAVE_PROVIDER");

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SmsPageHeader
        title={t("SMS_PROVIDER_PAGE.SMS_PROVIDER")}
        description={t("SMS_PROVIDER_PAGE.CONNECT_TWILIO_OR_TELNYX_CHOOSE")}
        icon={<MessageSquare className="h-6 w-6" />}
        actions={
          configuration ? (
            <Badge variant={configuration.isEnabled ? "success" : "outline"}>
              {configuration.isEnabled ? t("common:ENABLED") : t("common:DISABLED")}
            </Badge>
          ) : (
            <Badge variant="info">{t("SMS_PROVIDER_PAGE.NOT_CONFIGURED")}</Badge>
          )
        }
      />

      <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card className="rounded-xl p-5 sm:p-6">
          <Form {...form}>
            <form className="space-y-8" onSubmit={form.handleSubmit(submit)} noValidate>
              <SmsFormSection title={t(SMS_PROVIDER_PAGE_PROVIDER)} description={t("SMS_PROVIDER_PAGE.THE_ACCOUNT_MESSAGES_ARE_SENT")}>
                <div className="grid gap-5 sm:grid-cols-2">
                  <FormField
                    control={form.control}
                    name="providerType"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>{t(SMS_PROVIDER_PAGE_PROVIDER)}</FormLabel>
                        <Select value={String(field.value)} onValueChange={(value) => field.onChange(Number(value))}>
                          <FormControl>
                            <SelectTrigger aria-label={t(SMS_PROVIDER_PAGE_PROVIDER)}>
                              <SelectValue />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            {SMS_PROVIDER_OPTIONS.map((option) => (
                              <SelectItem key={option.value} value={String(option.value)}>
                                {option.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <TextField control={form.control} name="name" label={t("SMS_PROVIDER_PAGE.CONFIGURATION_NAME")} />

                  {isTwilio ? (
                    <TextField
                      control={form.control}
                      name="accountId"
                      label={t("SMS_PROVIDER_PAGE.ACCOUNT_SID")}
                      placeholder="AC…"
                      className="sm:col-span-2"
                    />
                  ) : (
                    <>
                      <TextField
                        control={form.control}
                        name="messagingProfileId"
                        label={t("SMS_PROVIDER_PAGE.MESSAGING_PROFILE_ID")}
                        placeholder="00000000-0000-0000-0000-000000000000"
                      />
                      <TextField
                        control={form.control}
                        name="webhookPublicKey"
                        label={t("SMS_PROVIDER_PAGE.WEBHOOK_PUBLIC_KEY")}
                        description={t("SMS_PROVIDER_PAGE.FROM_THE_TELNYX_PORTAL_USED")}
                      />
                    </>
                  )}

                  <FormField
                    control={form.control}
                    name="apiKey"
                    render={({ field }) => (
                      <FormItem className="sm:col-span-2">
                        <FormLabel>{isTwilio ? t("SMS_PROVIDER_PAGE.AUTH_TOKEN") : t("SMS_PROVIDER_PAGE.API_KEY")}</FormLabel>
                        <FormControl>
                          <PasswordInput
                            {...field}
                            autoComplete="new-password"
                            placeholder={configuration?.hasApiKey ? t("SMS_PROVIDER_PAGE.STORED_LEAVE_EMPTY_TO_KEEP") : ""}
                          />
                        </FormControl>
                        <FormDescription>
                          {t("SMS_PROVIDER_PAGE.API_KEY_STORED_IN_SECRETS")} {configuration?.hasApiKey && t("SMS_PROVIDER_PAGE.ENTERING_A_VALUE_REPLACES_THE")}
                        </FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <SwitchField control={form.control} name="isEnabled" label={t("common:ENABLED")} description={t("SMS_PROVIDER_PAGE.DISABLED_CONFIGURATIONS_SEND")} />
                  <SwitchField control={form.control} name="isDefault" label={t("SMS_PROVIDER_PAGE.DEFAULT")} description={t("SMS_PROVIDER_PAGE.USED_WHEN_SEVERAL_ARE_ENABLED")} />
                </div>
              </SmsFormSection>

              <SmsFormSection
                title={t("SMS_PROVIDER_PAGE.SENDER")}
                description={t("SMS_PROVIDER_PAGE.WHO_RECIPIENTS_SEE_THE_MESSAGE")}
              >
                <div className="grid gap-5 sm:grid-cols-2">
                  <TextField control={form.control} name="senderNumber" label={t("SMS_PROVIDER_PAGE.SENDER_NUMBER")} placeholder="+41791234567" />
                  <TextField
                    control={form.control}
                    name="senderName"
                    label={t("SMS_PROVIDER_PAGE.SENDER_NAME")}
                    placeholder="ACME"
                    description={t("SMS_PROVIDER_PAGE.UP_TO_11_LETTERS_DIGITS")}
                  />
                  <FormField
                    control={form.control}
                    name="senderNameExcludedPrefixes"
                    render={({ field }) => (
                      <FormItem className="sm:col-span-2">
                        <FormLabel>{t("SMS_PROVIDER_PAGE.COUNTRIES_THAT_GET_THE_NUMBER")}</FormLabel>
                        <FormControl>
                          <ChipsInput
                            value={field.value}
                            onChange={field.onChange}
                            validatorRegex={COUNTRY_CODE_PATTERN}
                            validatorRegexErrorMessage={t("SMS_PROVIDER_PAGE.COUNTRY_CODES_LOOK_LIKE")}
                          >
                            <ChipsInputList />
                            <ChipsInputField />
                          </ChipsInput>
                        </FormControl>
                        <FormDescription>
                          {t("SMS_PROVIDER_PAGE.RECIPIENTS_WHOSE_NUMBER_STARTS")}
                        </FormDescription>
                        {nameWithoutFallback && (
                          <p className="text-sm text-amber-500" role="status">
                            {t("SMS_PROVIDER_PAGE.NO_SENDER_NUMBER_IS_SET")}
                          </p>
                        )}
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
              </SmsFormSection>

              <SmsFormSection title={t("SMS_PROVIDER_PAGE.DELIVERY")} description={t("SMS_PROVIDER_PAGE.RETRIES_AND_DELIVERY_CONFIRMATION")}>
                <div className="grid gap-5 sm:grid-cols-2">
                  <TextField
                    control={form.control}
                    name="statusCallbackBaseUrl"
                    label={t("SMS_PROVIDER_PAGE.CALLBACK_BASE_URL")}
                    placeholder="https://utilities.example.com"
                    className="sm:col-span-2"
                    description={t("SMS_PROVIDER_PAGE.PUBLIC_HTTPS_ADDRESS_OF_THIS")}
                    action={
                      <Button
                        type="button"
                        variant="link"
                        size="sm"
                        className="h-auto p-0"
                        onClick={() =>
                          form.setValue("statusCallbackBaseUrl", getRuntimeEnv("BLOCKS_UTILITIES_BASE_URL") || "", {
                            shouldValidate: true,
                            shouldDirty: true,
                          })
                        }
                      >
                        {t("SMS_PROVIDER_PAGE.USE_THIS_SERVICE_APOS_S")}
                      </Button>
                    }
                  />
                  <NumberField control={form.control} name="maxRetryAttempts" label={t("SMS_PROVIDER_PAGE.SEND_ATTEMPTS")} description={t("SMS_PROVIDER_PAGE.N_1_10_ROUNDS_BEFORE_A")} />
                  <NumberField control={form.control} name="deliveryCheckDelayMinutes" label={t("SMS_PROVIDER_PAGE.DELIVERY_CHECK_AFTER_MINUTES")} />
                </div>
              </SmsFormSection>

              <SmsFormSection title={t("SMS_PROVIDER_PAGE.RATE_LIMITS")} description={t("SMS_PROVIDER_PAGE.COUNTED_PER_SMS_ONE_PER")}>
                <div className="grid gap-5 sm:grid-cols-2">
                  <NumberField control={form.control} name="rateLimit.tenantMaxPerWindow" label={t("SMS_PROVIDER_PAGE.TENANT_SMS_PER_WINDOW")} />
                  <NumberField control={form.control} name="rateLimit.tenantWindowSeconds" label={t("SMS_PROVIDER_PAGE.TENANT_WINDOW_SECONDS")} />
                  <NumberField control={form.control} name="rateLimit.recipientMaxPerWindow" label={t("SMS_PROVIDER_PAGE.PER_RECIPIENT_SMS_PER_WINDOW")} />
                  <NumberField control={form.control} name="rateLimit.recipientWindowSeconds" label={t("SMS_PROVIDER_PAGE.PER_RECIPIENT_WINDOW_SECONDS")} />
                </div>
              </SmsFormSection>

              <SmsFormSection title={t("SMS_PROVIDER_PAGE.SPAM_FILTER")} description={t("SMS_PROVIDER_PAGE.CHECKS_EVERY_MESSAGE_BEFORE_IT")}>
                <div className="grid gap-5 sm:grid-cols-2">
                  <SwitchField control={form.control} name="spamFilter.enabled" label={t("SMS_PROVIDER_PAGE.FILTER_ENABLED")} className="sm:col-span-2" />
                  {spamEnabled && (
                    <>
                      <NumberField control={form.control} name="spamFilter.maxRecipients" label={t("SMS_PROVIDER_PAGE.MAXIMUM_RECIPIENTS")} description={t("SMS_PROVIDER_PAGE.MORE_BLOCKS_THE_MESSAGE")} />
                      <NumberField control={form.control} name="spamFilter.maxMessageLength" label={t("SMS_PROVIDER_PAGE.MAXIMUM_LENGTH")} description={t("SMS_PROVIDER_PAGE.LONGER_IS_FLAGGED_NOT_BLOCKED")} />
                      <FormField
                        control={form.control}
                        name="spamFilter.urlPolicy"
                        render={({ field }) => (
                          <FormItem className="sm:col-span-2">
                            <FormLabel>{t("SMS_PROVIDER_PAGE.MESSAGES_WITH_LINKS")}</FormLabel>
                            <Select value={String(field.value)} onValueChange={(value) => field.onChange(Number(value))}>
                              <FormControl>
                                <SelectTrigger aria-label={t("SMS_PROVIDER_PAGE.MESSAGES_WITH_LINKS")}>
                                  <SelectValue />
                                </SelectTrigger>
                              </FormControl>
                              <SelectContent>
                                {urlPolicyOptions.map((option) => (
                                  <SelectItem key={option.value} value={String(option.value)}>
                                    {option.label} — {option.hint}
                                  </SelectItem>
                                ))}
                              </SelectContent>
                            </Select>
                            <FormMessage />
                          </FormItem>
                        )}
                      />
                      <FormField
                        control={form.control}
                        name="spamFilter.blockedTerms"
                        render={({ field }) => (
                          <FormItem className="sm:col-span-2">
                            <FormLabel>{t("SMS_PROVIDER_PAGE.BLOCKED_TERMS")}</FormLabel>
                            <FormControl>
                              <ChipsInput value={field.value} onChange={field.onChange}>
                                <ChipsInputList />
                                <ChipsInputField />
                              </ChipsInput>
                            </FormControl>
                            <FormDescription>{t("SMS_PROVIDER_PAGE.A_MESSAGE_WITH_A_LINK")}</FormDescription>
                            <FormMessage />
                          </FormItem>
                        )}
                      />
                    </>
                  )}
                </div>
              </SmsFormSection>

              {bannerErrors.length > 0 && (
                <div role="alert" className="rounded-lg border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
                  {bannerErrors.map((message) => (
                    <p key={message}>{message}</p>
                  ))}
                </div>
              )}

              <div className="flex justify-end border-t pt-6">
                <Button type="submit" disabled={isPending}>
                  {isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}
                  {isPending ? t("SMS_PROVIDER_PAGE.SAVING") : idleSaveLabel}
                </Button>
              </div>
            </form>
          </Form>
        </Card>

        <aside className="space-y-5">
          <CallbackCard control={form.control} />
          <Card className="rounded-xl">
            <div className="flex items-start gap-3">
              <KeyRound className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
              <div>
                <h2 className="font-semibold">{t("SMS_PROVIDER_PAGE.KEY_HANDLING")}</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {configuration?.hasApiKey
                    ? t("SMS_PROVIDER_PAGE.A_KEY_IS_STORED_IT")
                    : t("SMS_PROVIDER_PAGE.NO_KEY_STORED_YET_IT")}
                </p>
              </div>
            </div>
          </Card>
          <Card className="rounded-xl">
            <div className="flex items-start gap-3">
              <ShieldCheck className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
              <div>
                <h2 className="font-semibold">{t("SMS_PROVIDER_PAGE.VERIFIED_CALLBACKS")}</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {t("SMS_PROVIDER_PAGE.DELIVERY_CALLBACKS_ARE_ACCEPTED")}
                </p>
              </div>
            </div>
          </Card>
        </aside>
      </div>
    </main>
  );
};

/** The URL providers call back on; built exactly as the server builds it (SmsCallbackUrls). */
const CallbackCard = ({ control }: { control: Control<SmsProviderFormValues> }) => {
  const { t } = useT(UTILITIES_SMS_MODULE);
  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const [baseUrl, providerType] = useWatch({ control, name: ["statusCallbackBaseUrl", "providerType"] });
  const provider = providerType === SmsProviderType.Twilio ? "twilio" : "telnyx";
  const url = baseUrl?.trim() ? `${baseUrl.trim().replace(/\/+$/, "")}/sms/${provider}/webhooks/${encodeURIComponent(tenantId)}` : null;

  return (
    <Card className="rounded-xl">
      <div className="flex items-start gap-3">
        <Webhook className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
        <div className="min-w-0">
          <h2 className="font-semibold">{t("SMS_PROVIDER_PAGE.DELIVERY_CALLBACK")}</h2>
          {url ? (
            <>
              <p className="mt-1 text-sm text-muted-foreground">{t("SMS_PROVIDER_PAGE.SENT_TO_THE_PROVIDER_WITH")}</p>
              <code className="mt-2 block break-all rounded-md bg-muted px-2 py-1.5 text-xs">{url}</code>
              <div className="mt-2">
                <CopyToClipboardButton textToCopy={url}>{t("SMS_PROVIDER_PAGE.CALLBACK_URL")}</CopyToClipboardButton>
              </div>
            </>
          ) : (
            <p className="mt-1 text-sm text-muted-foreground">
              {t("SMS_PROVIDER_PAGE.SET_A_CALLBACK_BASE_URL")}
            </p>
          )}
        </div>
      </div>
    </Card>
  );
};

type FieldName = Parameters<typeof FormField<SmsProviderFormValues>>[0]["name"];

interface FieldProps {
  control: Control<SmsProviderFormValues>;
  name: FieldName;
  label: string;
  description?: string;
  placeholder?: string;
  className?: string;
  action?: React.ReactNode;
}

const TextField = ({ control, name, label, description, placeholder, className, action }: FieldProps) => (
  <FormField
    control={control}
    name={name}
    render={({ field }) => (
      <FormItem className={className}>
        <div className="flex items-center justify-between gap-2">
          <FormLabel>{label}</FormLabel>
          {action}
        </div>
        <FormControl>
          <Input {...field} value={(field.value as string) ?? ""} placeholder={placeholder} />
        </FormControl>
        {description && <FormDescription>{description}</FormDescription>}
        <FormMessage />
      </FormItem>
    )}
  />
);

const NumberField = ({ control, name, label, description, className }: FieldProps) => (
  <FormField
    control={control}
    name={name}
    render={({ field }) => (
      <FormItem className={className}>
        <FormLabel>{label}</FormLabel>
        <FormControl>
          <Input {...field} value={(field.value as number | string) ?? ""} type="number" inputMode="numeric" min={1} />
        </FormControl>
        {description && <FormDescription>{description}</FormDescription>}
        <FormMessage />
      </FormItem>
    )}
  />
);

const SwitchField = ({ control, name, label, description, className }: FieldProps) => (
  <FormField
    control={control}
    name={name}
    render={({ field }) => (
      <FormItem className={`flex items-center justify-between gap-4 rounded-lg border p-3 ${className ?? ""}`}>
        <div>
          <FormLabel>{label}</FormLabel>
          {description && <FormDescription>{description}</FormDescription>}
        </div>
        <FormControl>
          <Switch checked={Boolean(field.value)} onCheckedChange={field.onChange} aria-label={label} />
        </FormControl>
      </FormItem>
    )}
  />
);
