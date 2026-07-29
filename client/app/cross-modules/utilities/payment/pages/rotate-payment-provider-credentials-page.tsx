import { useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  KeyRound,
  Loader2,
  ShieldAlert,
  ShieldCheck,
} from "lucide-react";
import { useForm } from "react-hook-form";
import { useNavigate, useParams } from "react-router-dom";
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
import { toast } from "@/hooks/use-toast";
import { PaymentProviderPageHeader } from "../components/payment-provider-page-header";
import {
  PaymentProviderLoadError,
  PaymentProviderNotFound,
  PaymentProviderPageSkeleton,
} from "../components/payment-provider-state";
import { usePaymentProviders } from "../hooks/use-payment-providers";
import { useRotatePaymentProviderCredentials } from "../hooks/use-rotate-payment-provider-credentials";
import type {
  PaymentProvider,
  RotatePaymentProviderCredentialsRequest,
} from "../models/payment-provider.model";
import {
  createRotatePaymentProviderSchema,
  providerDisplayName,
  type RotatePaymentProviderFormValues,
} from "../schemas/payment-provider.schema";

const normalizeOptional = (value?: string): string | undefined => {
  const normalized = value?.trim();
  return normalized || undefined;
};

interface RotationFormProps {
  provider: PaymentProvider;
  providersPath: string;
}

const RotationForm = ({
  provider,
  providersPath,
}: RotationFormProps) => {
  const navigate = useNavigate();
  const { mutateAsync, isPending } =
    useRotatePaymentProviderCredentials();
  const [submissionError, setSubmissionError] = useState<string | null>(
    null,
  );
  const form = useForm<RotatePaymentProviderFormValues>({
    resolver: zodResolver(
      createRotatePaymentProviderSchema(provider.providerName),
    ),
    mode: "onBlur",
    defaultValues: {
      apiKey: "",
      webhookHmacKey: "",
      tokenHmacKey: "",
    },
  });
  const isAdyen = provider.providerName === "ADYEN-ONLINE";

  const submit = async (
    values: RotatePaymentProviderFormValues,
  ) => {
    setSubmissionError(null);

    const request: RotatePaymentProviderCredentialsRequest = {
      version: provider.version,
      apiKey: normalizeOptional(values.apiKey),
      webhookHmacKey: normalizeOptional(values.webhookHmacKey),
      tokenHmacKey: isAdyen
        ? normalizeOptional(values.tokenHmacKey)
        : undefined,
    };

    try {
      const updated = await mutateAsync({
        paymentProviderId: provider.paymentProviderId,
        request,
      });

      toast({
        variant: "success",
        title: "Provider credentials rotated",
        description: `${providerDisplayName(updated.providerName)} is now at version ${updated.version}.`,
      });
      navigate(providersPath);
    } catch (error) {
      setSubmissionError(
        error instanceof Error
          ? error.message
          : "The provider credentials could not be rotated.",
      );
    }
  };

  return (
    <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
      <Card className="rounded-xl p-5 sm:p-6">
        <Form {...form}>
          <form
            className="space-y-7"
            onSubmit={form.handleSubmit(submit)}
            noValidate
          >
            <section className="rounded-xl border bg-muted/20 p-4 sm:p-5">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <h2 className="font-semibold">
                    {providerDisplayName(provider.providerName)}
                  </h2>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Merchant: {provider.merchantId}
                  </p>
                </div>
                <Badge variant="info">Version {provider.version}</Badge>
              </div>
            </section>

            <section className="space-y-5">
              <div>
                <h2 className="text-lg font-semibold">
                  New credential values
                </h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  Leave a field empty to keep its current value. Existing
                  values are intentionally never loaded into this form.
                </p>
              </div>

              <div className="grid gap-5 sm:grid-cols-2">
                <FormField
                  control={form.control}
                  name="apiKey"
                  render={({ field }) => (
                    <FormItem className="sm:col-span-2">
                      <FormLabel>New API key</FormLabel>
                      <FormControl>
                        <Input
                          {...field}
                          value={field.value ?? ""}
                          type="password"
                          autoComplete="new-password"
                          spellCheck={false}
                          placeholder={
                            isAdyen
                              ? "Leave empty to keep the current API key"
                              : "sk_… or rk_…"
                          }
                        />
                      </FormControl>
                      <FormDescription>
                        API authentication keys are replaced; providers do
                        not verify requests against two API keys.
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
                        {isAdyen
                          ? "New standard webhook HMAC"
                          : "New webhook endpoint secret"}
                      </FormLabel>
                      <FormControl>
                        <Input
                          {...field}
                          value={field.value ?? ""}
                          type="password"
                          autoComplete="new-password"
                          spellCheck={false}
                          placeholder={
                            isAdyen
                              ? "64 hexadecimal characters"
                              : "whsec_…"
                          }
                        />
                      </FormControl>
                      <FormDescription>
                        The current active secret moves to the previous slot.
                      </FormDescription>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                {isAdyen && (
                  <FormField
                    control={form.control}
                    name="tokenHmacKey"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>
                          New token webhook HMAC
                        </FormLabel>
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
                          Rotates stored-payment-method webhook verification.
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
                  <KeyRound className="mr-2 h-4 w-4" />
                )}
                {isPending ? "Rotating credentials…" : "Rotate credentials"}
              </Button>
            </div>
          </form>
        </Form>
      </Card>

      <div className="space-y-5">
        <Card className="rounded-xl border-amber-200 bg-amber-50/60">
          <div className="flex items-start gap-3">
            <ShieldAlert className="mt-0.5 h-5 w-5 text-amber-700" />
            <div>
              <h2 className="font-semibold text-amber-950">
                Webhook overlap
              </h2>
              <p className="mt-1 text-sm leading-6 text-amber-900">
                The old active webhook secret remains valid as the previous
                secret. A later rotation replaces that previous slot.
              </p>
            </div>
          </div>
        </Card>

        <Card className="rounded-xl">
          <div className="flex items-start gap-3">
            <ShieldCheck className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
            <div>
              <h2 className="font-semibold">Protected operation</h2>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                Version {provider.version} prevents concurrent rotations from
                silently replacing each other. The provider cache refreshes
                immediately after a successful write.
              </p>
            </div>
          </div>
        </Card>
      </div>
    </div>
  );
};

export const RotatePaymentProviderCredentialsPage = () => {
  const { itemId, paymentProviderId = "" } = useParams();
  const providersPath = `/app/${itemId ?? ""}/payment/providers`;
  const {
    data: providers,
    error,
    isError,
    isLoading,
    refetch,
  } = usePaymentProviders();
  const provider = providers?.find(
    (candidate) =>
      candidate.paymentProviderId === paymentProviderId,
  );

  if (isLoading) {
    return (
      <main className="min-w-0 p-4 sm:p-6 lg:p-8">
        <PaymentProviderPageSkeleton />
      </main>
    );
  }

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <PaymentProviderPageHeader
        title="Rotate provider credentials"
        description="Change API or webhook credentials without exposing their current values."
        backTo={providersPath}
        icon={<KeyRound className="h-6 w-6" />}
      />

      {isError ? (
        <PaymentProviderLoadError
          message={
            error instanceof Error
              ? error.message
              : "The payment provider could not be loaded."
          }
          onRetry={() => refetch()}
        />
      ) : !provider ? (
        <PaymentProviderNotFound />
      ) : (
        <RotationForm
          provider={provider}
          providersPath={providersPath}
        />
      )}
    </main>
  );
};
