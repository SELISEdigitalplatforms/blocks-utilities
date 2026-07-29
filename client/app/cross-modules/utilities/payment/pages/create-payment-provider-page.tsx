import { useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Info,
  KeyRound,
  Loader2,
  LockKeyhole,
  Plus,
} from "lucide-react";
import { useForm, useWatch } from "react-hook-form";
import { useNavigate, useParams } from "react-router";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { toast } from "@/hooks/use-toast";
import { PaymentProviderConfigurationFields } from "../components/payment-provider-configuration-fields";
import { PaymentProviderPageHeader } from "../components/payment-provider-page-header";
import { useRegisterPaymentProvider } from "../hooks/use-register-payment-provider";
import type { RegisterPaymentProviderRequest } from "../models/payment-provider.model";
import {
  providerDisplayName,
  registerPaymentProviderSchema,
  type RegisterPaymentProviderFormValues,
} from "../schemas/payment-provider.schema";

const ADYEN_TEST_API_BASE_URL =
  "https://checkout-test.adyen.com/v72";

const normalizeOptional = (value?: string): string | undefined => {
  const normalized = value?.trim();
  return normalized || undefined;
};

export const CreatePaymentProviderPage = () => {
  const { itemId } = useParams();
  const navigate = useNavigate();
  const { mutateAsync, isPending } = useRegisterPaymentProvider();
  const [submissionError, setSubmissionError] = useState<string | null>(
    null,
  );
  const providersPath = `/app/${itemId ?? ""}/payment/providers`;
  const defaultResultUrl =
    typeof window === "undefined"
      ? ""
      : `${window.location.origin}/app/${itemId ?? ""}/payment/result`;

  const form = useForm<RegisterPaymentProviderFormValues>({
    resolver: zodResolver(registerPaymentProviderSchema),
    mode: "onBlur",
    defaultValues: {
      providerName: "ADYEN-ONLINE",
      merchantId: "",
      frontendResultUrl: defaultResultUrl,
      apiBaseUrl: ADYEN_TEST_API_BASE_URL,
      countryCode: "",
      manualCapture: false,
      maxRefundDays: 365,
      storeId: "",
      apiKey: "",
      webhookHmacKey: "",
      tokenHmacKey: "",
    },
  });

  const providerName = useWatch({
    control: form.control,
    name: "providerName",
  });

  const submit = async (
    values: RegisterPaymentProviderFormValues,
  ) => {
    setSubmissionError(null);

    const request: RegisterPaymentProviderRequest = {
      providerName: values.providerName,
      merchantId: values.merchantId.trim(),
      frontendResultUrl: values.frontendResultUrl.trim(),
      countryCode: normalizeOptional(values.countryCode)?.toUpperCase(),
      manualCapture: values.manualCapture,
      maxRefundDays: values.maxRefundDays,
      storeId: normalizeOptional(values.storeId),
      apiKey: values.apiKey.trim(),
      webhookHmacKey: values.webhookHmacKey.trim(),
      apiBaseUrl:
        values.providerName === "ADYEN-ONLINE"
          ? normalizeOptional(values.apiBaseUrl)
          : undefined,
      tokenHmacKey:
        values.providerName === "ADYEN-ONLINE"
          ? normalizeOptional(values.tokenHmacKey)
          : undefined,
    };

    try {
      const provider = await mutateAsync(request);

      toast({
        variant: "success",
        title: "Payment provider created",
        description: `${providerDisplayName(provider.providerName)} is ready to configure payments.`,
      });
      navigate(providersPath);
    } catch (error) {
      setSubmissionError(
        error instanceof Error
          ? error.message
          : "The payment provider could not be created.",
      );
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <PaymentProviderPageHeader
        title="Create payment provider"
        description="Register a provider for the current tenant. Credentials are encrypted and never returned."
        backTo={providersPath}
        icon={<Plus className="h-6 w-6" />}
      />

      <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card className="rounded-xl p-5 sm:p-6">
          <Form {...form}>
            <form
              className="space-y-7"
              onSubmit={form.handleSubmit(submit)}
              noValidate
            >
              <section className="space-y-5">
                <div>
                  <h2 className="text-lg font-semibold">
                    Provider identity
                  </h2>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Provider and merchant form the immutable registration
                    identity.
                  </p>
                </div>

                <div className="grid gap-5 sm:grid-cols-2">
                  <FormField
                    control={form.control}
                    name="providerName"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Provider</FormLabel>
                        <Select
                          value={field.value}
                          onValueChange={(value) => {
                            field.onChange(value);
                            form.setValue(
                              "apiBaseUrl",
                              value === "ADYEN-ONLINE"
                                ? ADYEN_TEST_API_BASE_URL
                                : "",
                            );
                            form.setValue("tokenHmacKey", "");
                            form.clearErrors();
                          }}
                        >
                          <FormControl>
                            <SelectTrigger>
                              <SelectValue />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            <SelectItem value="ADYEN-ONLINE">
                              Adyen Hosted Checkout
                            </SelectItem>
                            <SelectItem value="STRIPE">
                              Stripe Checkout
                            </SelectItem>
                          </SelectContent>
                        </Select>
                        <FormDescription>
                          Changing this later requires a new registration.
                        </FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  <FormField
                    control={form.control}
                    name="merchantId"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Merchant ID</FormLabel>
                        <FormControl>
                          <Input
                            {...field}
                            maxLength={200}
                            placeholder={
                              providerName === "ADYEN-ONLINE"
                                ? "YourAdyenMerchant"
                                : "acct_..."
                            }
                            autoComplete="off"
                          />
                        </FormControl>
                        <FormDescription>
                          The provider merchant or account identifier.
                        </FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  {providerName === "ADYEN-ONLINE" && (
                    <FormField
                      control={form.control}
                      name="apiBaseUrl"
                      render={({ field }) => (
                        <FormItem className="sm:col-span-2">
                          <FormLabel>Checkout API base URL</FormLabel>
                          <FormControl>
                            <Input
                              {...field}
                              value={field.value ?? ""}
                              type="url"
                              placeholder={ADYEN_TEST_API_BASE_URL}
                              autoComplete="url"
                            />
                          </FormControl>
                          <FormDescription>
                            Use the approved Adyen Checkout host and API
                            version for this environment.
                          </FormDescription>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  )}
                </div>
              </section>

              <section className="space-y-5 border-t pt-6">
                <div>
                  <h2 className="text-lg font-semibold">
                    Payment configuration
                  </h2>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Runtime behavior that can be edited later.
                  </p>
                </div>
                <PaymentProviderConfigurationFields />
              </section>

              <section className="space-y-5 border-t pt-6">
                <div className="flex items-start gap-3">
                  <span className="rounded-lg bg-amber-50 p-2 text-amber-700">
                    <KeyRound className="h-5 w-5" />
                  </span>
                  <div>
                    <h2 className="text-lg font-semibold">Credentials</h2>
                    <p className="mt-1 text-sm text-muted-foreground">
                      Values are sent once over TLS, encrypted at rest, and
                      never displayed again.
                    </p>
                  </div>
                </div>

                <div className="grid gap-5 sm:grid-cols-2">
                  <FormField
                    control={form.control}
                    name="apiKey"
                    render={({ field }) => (
                      <FormItem className="sm:col-span-2">
                        <FormLabel>API key</FormLabel>
                        <FormControl>
                          <Input
                            {...field}
                            type="password"
                            autoComplete="new-password"
                            spellCheck={false}
                            placeholder={
                              providerName === "STRIPE"
                                ? "sk_… or rk_…"
                                : "Adyen Checkout API key"
                            }
                          />
                        </FormControl>
                        <FormDescription>
                          Used only for authenticated calls to the provider.
                        </FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  <FormField
                    control={form.control}
                    name="webhookHmacKey"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>
                          {providerName === "STRIPE"
                            ? "Webhook endpoint secret"
                            : "Standard webhook HMAC"}
                        </FormLabel>
                        <FormControl>
                          <Input
                            {...field}
                            type="password"
                            autoComplete="new-password"
                            spellCheck={false}
                            placeholder={
                              providerName === "STRIPE"
                                ? "whsec_…"
                                : "64 hexadecimal characters"
                            }
                          />
                        </FormControl>
                        <FormDescription>
                          Verifies payment lifecycle webhooks.
                        </FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  {providerName === "ADYEN-ONLINE" && (
                    <FormField
                      control={form.control}
                      name="tokenHmacKey"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel>Token webhook HMAC</FormLabel>
                          <FormControl>
                            <Input
                              {...field}
                              value={field.value ?? ""}
                              type="password"
                              autoComplete="new-password"
                              spellCheck={false}
                              placeholder="64 hexadecimal characters"
                            />
                          </FormControl>
                          <FormDescription>
                            Verifies stored-payment-method lifecycle events.
                          </FormDescription>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  )}
                </div>
              </section>

              {submissionError && (
                <div
                  role="alert"
                  className="rounded-lg border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive"
                >
                  {submissionError}
                </div>
              )}

              <div className="flex flex-col-reverse gap-3 border-t pt-6 sm:flex-row sm:justify-end">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => navigate(providersPath)}
                  disabled={isPending}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={isPending}>
                  {isPending ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  ) : (
                    <Plus className="mr-2 h-4 w-4" />
                  )}
                  {isPending ? "Creating provider…" : "Create provider"}
                </Button>
              </div>
            </form>
          </Form>
        </Card>

        <div className="space-y-5">
          <Card className="rounded-xl">
            <div className="flex items-start gap-3">
              <LockKeyhole className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
              <div>
                <h2 className="font-semibold">Identity keys</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  Return-state and shopper-reference keys are generated by
                  the server. They are intentionally absent from this form.
                </p>
              </div>
            </div>
          </Card>

          <Card className="rounded-xl">
            <div className="flex items-start gap-3">
              <Info className="mt-0.5 h-5 w-5 text-blue-700" />
              <div>
                <h2 className="font-semibold">Before creating</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  Configure the provider account and webhook endpoints first.
                  The backend validates credential shape before writing.
                </p>
              </div>
            </div>
          </Card>
        </div>
      </div>
    </main>
  );
};
