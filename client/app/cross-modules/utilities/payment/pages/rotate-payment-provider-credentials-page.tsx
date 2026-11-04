import { useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  KeyRound,
  Loader2,
  ShieldAlert,
  ShieldCheck,
} from "lucide-react";
import { useForm } from "react-hook-form";
import { useNavigate, useParams } from "react-router";
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
import { useT } from "@seliseblocks/genesis-os";

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
  const { t } = useT("utilities.payment");
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
        title: t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.PROVIDER_CREDENTIALS_ROTATED"),
        description: t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.IS_NOW_AT_VERSION", { providerName: providerDisplayName(updated.providerName), version: updated.version }),
      });
      navigate(providersPath);
    } catch (error) {
      setSubmissionError(
        error instanceof Error
          ? error.message
          : t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.THE_PROVIDER_CREDENTIALS_COULD_NOT"),
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
                    {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.MERCHANT_ID", { merchantId: provider.merchantId })}
                  </p>
                </div>
                <Badge variant="info">{t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.VERSION", { version: provider.version })}</Badge>
              </div>
            </section>

            <section className="space-y-5">
              <div>
                <h2 className="text-lg font-semibold">
                  {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.NEW_CREDENTIAL_VALUES")}
                </h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.LEAVE_A_FIELD_EMPTY_TO")}
                </p>
              </div>

              <div className="grid gap-5 sm:grid-cols-2">
                <FormField
                  control={form.control}
                  name="apiKey"
                  render={({ field }) => (
                    <FormItem className="sm:col-span-2">
                      <FormLabel>{t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.NEW_API_KEY")}</FormLabel>
                      <FormControl>
                        <Input
                          {...field}
                          value={field.value ?? ""}
                          type="password"
                          autoComplete="new-password"
                          spellCheck={false}
                          placeholder={
                            isAdyen
                              ? t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.LEAVE_EMPTY_TO_KEEP_THE")
                              : "sk_… or rk_…"
                          }
                        />
                      </FormControl>
                      <FormDescription>
                        {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.API_AUTHENTICATION_KEYS_ARE")}
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
                          ? t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.NEW_STANDARD_WEBHOOK_HMAC")
                          : t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.NEW_WEBHOOK_ENDPOINT_SECRET")}
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
                              ? t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.N_64_HEXADECIMAL_CHARACTERS")
                              : "whsec_…"
                          }
                        />
                      </FormControl>
                      <FormDescription>
                        {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.THE_CURRENT_ACTIVE_SECRET_MOVES")}
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
                          {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.NEW_TOKEN_WEBHOOK_HMAC")}
                        </FormLabel>
                        <FormControl>
                          <Input
                            {...field}
                            value={field.value ?? ""}
                            type="password"
                            autoComplete="new-password"
                            spellCheck={false}
                            placeholder={t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.N_64_HEXADECIMAL_CHARACTERS")}
                          />
                        </FormControl>
                        <FormDescription>
                          {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.ROTATES_STORED_PAYMENT_METHOD")}
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
                {t("common:CANCEL")}
              </Button>
              <Button type="submit" disabled={isPending}>
                {isPending ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <KeyRound className="mr-2 h-4 w-4" />
                )}
                {isPending ? t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.ROTATING_CREDENTIALS") : t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.ROTATE_CREDENTIALS")}
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
                {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.WEBHOOK_OVERLAP")}
              </h2>
              <p className="mt-1 text-sm leading-6 text-amber-900">
                {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.THE_OLD_ACTIVE_WEBHOOK_SECRET")}
              </p>
            </div>
          </div>
        </Card>

        <Card className="rounded-xl">
          <div className="flex items-start gap-3">
            <ShieldCheck className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
            <div>
              <h2 className="font-semibold">{t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.PROTECTED_OPERATION")}</h2>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                {t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.VERSION_PREVENTS_CONCURRENT_ROTATIONS", { version: provider.version })}
              </p>
            </div>
          </div>
        </Card>
      </div>
    </div>
  );
};

export const RotatePaymentProviderCredentialsPage = () => {
  const { t } = useT("utilities.payment");
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
        title={t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.ROTATE_PROVIDER_CREDENTIALS")}
        description={t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.CHANGE_API_OR_WEBHOOK_CREDENTIALS")}
        backTo={providersPath}
        icon={<KeyRound className="h-6 w-6" />}
      />

      {isError ? (
        <PaymentProviderLoadError
          message={
            error instanceof Error
              ? error.message
              : t("ROTATE_PAYMENT_PROVIDER_CREDENTIALS_PAGE.THE_PAYMENT_PROVIDER_COULD_NOT")
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
