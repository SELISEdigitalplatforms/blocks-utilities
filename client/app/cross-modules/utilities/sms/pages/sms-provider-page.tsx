import { useEffect, useMemo, useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useProjectStore } from "@seliseblocks/genesis-os";
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
  SMS_URL_POLICY_OPTIONS,
  SmsProviderType,
  SmsUrlPolicy,
} from "../constants/sms.constants";
import { useSaveSmsProviderConfiguration, useSmsProviderConfiguration } from "../hooks/use-sms";
import type { SaveSmsProviderConfigurationRequest, SmsProviderConfiguration } from "../models/sms.model";
import { createSmsProviderSchema, type SmsProviderFormValues } from "../schemas/sms.schema";
import { SmsRequestError } from "../services/sms.service";
import { applyServerErrors } from "../utils/sms.util";

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
  const { data: configuration, error, isError, isLoading, refetch } = useSmsProviderConfiguration();

  if (isLoading) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8" aria-label="Loading SMS provider">
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
          <h2 className="mt-4 text-lg font-semibold">SMS provider unavailable</h2>
          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            {error instanceof Error ? error.message : "Try again."}
          </p>
          <Button className="mt-5" variant="outline" onClick={() => refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            Try again
          </Button>
        </Card>
      </main>
    );
  }

  // Keyed on the stored version so a save elsewhere, or the first save here, re-seeds the form.
  return <SmsProviderForm key={configuration?.lastUpdatedDate ?? "new"} configuration={configuration ?? null} />;
};

const SmsProviderForm = ({ configuration }: { configuration: SmsProviderConfiguration | null }) => {
  const isCreate = !configuration?.hasApiKey;
  const schema = useMemo(() => createSmsProviderSchema(isCreate), [isCreate]);
  const [bannerErrors, setBannerErrors] = useState<string[]>([]);
  const { mutateAsync, isPending } = useSaveSmsProviderConfiguration();

  const form = useForm<SmsProviderFormValues>({
    resolver: zodResolver(schema),
    mode: "onBlur",
    defaultValues: configuration ? toFormValues(configuration) : DEFAULTS,
  });
  const providerType = useWatch({ control: form.control, name: "providerType" });
  const spamEnabled = useWatch({ control: form.control, name: "spamFilter.enabled" });
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
        title: "SMS provider saved",
        description: values.apiKey ? "The provider key was stored in Blocks Secrets." : undefined,
      });
    } catch (saveError) {
      if (saveError instanceof SmsRequestError) {
        const unplaced = applyServerErrors(saveError.fieldErrors, FORM_FIELDS, form.setError);
        setBannerErrors(unplaced.length ? unplaced : Object.keys(saveError.fieldErrors).length ? [] : [saveError.message]);
      } else {
        setBannerErrors(["The SMS provider configuration could not be saved."]);
      }
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SmsPageHeader
        title="SMS provider"
        description="Connect Twilio or Telnyx, choose who messages come from, and set the limits that protect this tenant."
        icon={<MessageSquare className="h-6 w-6" />}
        actions={
          configuration ? (
            <Badge variant={configuration.isEnabled ? "success" : "outline"}>
              {configuration.isEnabled ? "Enabled" : "Disabled"}
            </Badge>
          ) : (
            <Badge variant="info">Not configured</Badge>
          )
        }
      />

      <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card className="rounded-xl p-5 sm:p-6">
          <Form {...form}>
            <form className="space-y-8" onSubmit={form.handleSubmit(submit)} noValidate>
              <SmsFormSection title="Provider" description="The account messages are sent through.">
                <div className="grid gap-5 sm:grid-cols-2">
                  <FormField
                    control={form.control}
                    name="providerType"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Provider</FormLabel>
                        <Select value={String(field.value)} onValueChange={(value) => field.onChange(Number(value))}>
                          <FormControl>
                            <SelectTrigger aria-label="Provider">
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
                  <TextField control={form.control} name="name" label="Configuration name" />

                  {isTwilio ? (
                    <TextField
                      control={form.control}
                      name="accountId"
                      label="Account SID"
                      placeholder="AC…"
                      className="sm:col-span-2"
                    />
                  ) : (
                    <>
                      <TextField
                        control={form.control}
                        name="messagingProfileId"
                        label="Messaging profile ID"
                        placeholder="00000000-0000-0000-0000-000000000000"
                      />
                      <TextField
                        control={form.control}
                        name="webhookPublicKey"
                        label="Webhook public key"
                        description="From the Telnyx portal. Used to check that delivery callbacks really come from Telnyx."
                      />
                    </>
                  )}

                  <FormField
                    control={form.control}
                    name="apiKey"
                    render={({ field }) => (
                      <FormItem className="sm:col-span-2">
                        <FormLabel>{isTwilio ? "Auth token" : "API key"}</FormLabel>
                        <FormControl>
                          <PasswordInput
                            {...field}
                            autoComplete="new-password"
                            placeholder={configuration?.hasApiKey ? "•••••••• stored — leave empty to keep" : ""}
                          />
                        </FormControl>
                        <FormDescription>
                          Stored in Blocks Secrets; only its id is kept with this configuration, and it is never shown
                          again. {configuration?.hasApiKey && "Entering a value replaces the stored key."}
                        </FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <SwitchField control={form.control} name="isEnabled" label="Enabled" description="Disabled configurations send nothing." />
                  <SwitchField control={form.control} name="isDefault" label="Default" description="Used when several are enabled." />
                </div>
              </SmsFormSection>

              <SmsFormSection
                title="Sender"
                description="Who recipients see the message from. The name is used where carriers accept it, the number everywhere else."
              >
                <div className="grid gap-5 sm:grid-cols-2">
                  <TextField control={form.control} name="senderNumber" label="Sender number" placeholder="+41791234567" />
                  <TextField
                    control={form.control}
                    name="senderName"
                    label="Sender name"
                    placeholder="ACME"
                    description="Up to 11 letters, digits or spaces. Recipients cannot reply to a name."
                  />
                  <FormField
                    control={form.control}
                    name="senderNameExcludedPrefixes"
                    render={({ field }) => (
                      <FormItem className="sm:col-span-2">
                        <FormLabel>Countries that get the number</FormLabel>
                        <FormControl>
                          <ChipsInput
                            value={field.value}
                            onChange={field.onChange}
                            validatorRegex={COUNTRY_CODE_PATTERN}
                            validatorRegexErrorMessage="Country codes look like +1 or +86."
                          >
                            <ChipsInputList />
                            <ChipsInputField />
                          </ChipsInput>
                        </FormControl>
                        <FormDescription>
                          Recipients whose number starts with one of these codes see the sender number, because their
                          carriers reject names. +1 covers the US and Canada.
                        </FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
              </SmsFormSection>

              <SmsFormSection title="Delivery" description="Retries and delivery confirmation.">
                <div className="grid gap-5 sm:grid-cols-2">
                  <TextField
                    control={form.control}
                    name="statusCallbackBaseUrl"
                    label="Callback base URL"
                    placeholder="https://utilities.example.com"
                    className="sm:col-span-2"
                    description="Public https address of this service. Without it, delivery is confirmed only by polling the provider."
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
                        Use this service&apos;s URL
                      </Button>
                    }
                  />
                  <NumberField control={form.control} name="maxRetryAttempts" label="Send attempts" description="1–10 rounds before a recipient fails." />
                  <NumberField control={form.control} name="deliveryCheckDelayMinutes" label="Delivery check after (minutes)" />
                </div>
              </SmsFormSection>

              <SmsFormSection title="Rate limits" description="Counted per SMS, one per recipient. Requests over a limit are refused.">
                <div className="grid gap-5 sm:grid-cols-2">
                  <NumberField control={form.control} name="rateLimit.tenantMaxPerWindow" label="Tenant: SMS per window" />
                  <NumberField control={form.control} name="rateLimit.tenantWindowSeconds" label="Tenant: window (seconds)" />
                  <NumberField control={form.control} name="rateLimit.recipientMaxPerWindow" label="Per recipient: SMS per window" />
                  <NumberField control={form.control} name="rateLimit.recipientWindowSeconds" label="Per recipient: window (seconds)" />
                </div>
              </SmsFormSection>

              <SmsFormSection title="Spam filter" description="Checks every message before it is queued. Blocked messages are quarantined.">
                <div className="grid gap-5 sm:grid-cols-2">
                  <SwitchField control={form.control} name="spamFilter.enabled" label="Filter enabled" className="sm:col-span-2" />
                  {spamEnabled && (
                    <>
                      <NumberField control={form.control} name="spamFilter.maxRecipients" label="Maximum recipients" description="More blocks the message." />
                      <NumberField control={form.control} name="spamFilter.maxMessageLength" label="Maximum length" description="Longer is flagged, not blocked." />
                      <FormField
                        control={form.control}
                        name="spamFilter.urlPolicy"
                        render={({ field }) => (
                          <FormItem className="sm:col-span-2">
                            <FormLabel>Messages with links</FormLabel>
                            <Select value={String(field.value)} onValueChange={(value) => field.onChange(Number(value))}>
                              <FormControl>
                                <SelectTrigger aria-label="Messages with links">
                                  <SelectValue />
                                </SelectTrigger>
                              </FormControl>
                              <SelectContent>
                                {SMS_URL_POLICY_OPTIONS.map((option) => (
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
                            <FormLabel>Blocked terms</FormLabel>
                            <FormControl>
                              <ChipsInput value={field.value} onChange={field.onChange}>
                                <ChipsInputList />
                                <ChipsInputField />
                              </ChipsInput>
                            </FormControl>
                            <FormDescription>A message with a link and any of these words is blocked, whatever the link policy.</FormDescription>
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
                  {isPending ? "Saving…" : configuration ? "Save changes" : "Save provider"}
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
                <h2 className="font-semibold">Key handling</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {configuration?.hasApiKey
                    ? "A key is stored. It is never sent back to the browser; saving without one keeps it."
                    : "No key stored yet. It is required for the first save."}
                </p>
              </div>
            </div>
          </Card>
          <Card className="rounded-xl">
            <div className="flex items-start gap-3">
              <ShieldCheck className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
              <div>
                <h2 className="font-semibold">Verified callbacks</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  Delivery callbacks are accepted only with a valid provider signature — Twilio&apos;s signed with your auth
                  token, Telnyx&apos;s with the account key above.
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
  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const [baseUrl, providerType] = useWatch({ control, name: ["statusCallbackBaseUrl", "providerType"] });
  const provider = providerType === SmsProviderType.Twilio ? "twilio" : "telnyx";
  const url = baseUrl?.trim() ? `${baseUrl.trim().replace(/\/+$/, "")}/sms/${provider}/webhooks/${encodeURIComponent(tenantId)}` : null;

  return (
    <Card className="rounded-xl">
      <div className="flex items-start gap-3">
        <Webhook className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
        <div className="min-w-0">
          <h2 className="font-semibold">Delivery callback</h2>
          {url ? (
            <>
              <p className="mt-1 text-sm text-muted-foreground">Sent to the provider with every message:</p>
              <code className="mt-2 block break-all rounded-md bg-muted px-2 py-1.5 text-xs">{url}</code>
              <div className="mt-2">
                <CopyToClipboardButton textToCopy={url}>Callback URL</CopyToClipboardButton>
              </div>
            </>
          ) : (
            <p className="mt-1 text-sm text-muted-foreground">
              Set a callback base URL to receive delivery reports as they happen.
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
