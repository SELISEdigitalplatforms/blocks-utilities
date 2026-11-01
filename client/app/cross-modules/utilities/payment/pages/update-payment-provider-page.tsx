import { useEffect, useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Pencil, ShieldCheck } from "lucide-react";
import { useForm } from "react-hook-form";
import { useNavigate, useParams } from "react-router";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Form } from "@/components/ui-kits/form/form";
import { toast } from "@/hooks/use-toast";
import { PaymentProviderConfigurationFields } from "../components/payment-provider-configuration-fields";
import { PaymentProviderPageHeader } from "../components/payment-provider-page-header";
import { PaymentWebhookEndpointsCard } from "../components/payment-webhook-endpoints-card";
import {
  PaymentProviderLoadError,
  PaymentProviderNotFound,
  PaymentProviderPageSkeleton,
} from "../components/payment-provider-state";
import { usePaymentProviders } from "../hooks/use-payment-providers";
import { useUpdatePaymentProvider } from "../hooks/use-update-payment-provider";
import type { UpdatePaymentProviderRequest } from "../models/payment-provider.model";
import {
  parseCaptureDelayHours,
  providerDisplayName,
  createUpdatePaymentProviderSchema,
  type UpdatePaymentProviderFormValues,
} from "../schemas/payment-provider.schema";
import { useT } from "@seliseblocks/genesis-os";

const normalizeOptional = (value?: string): string | undefined => {
  const normalized = value?.trim();
  return normalized || undefined;
};

export const UpdatePaymentProviderPage = () => {
  const { t } = useT("utilities.payment");
  const { itemId, paymentProviderId = "" } = useParams();
  const navigate = useNavigate();
  const providersPath = `/app/${itemId ?? ""}/payment/providers`;
  const {
    data: providers,
    error,
    isError,
    isLoading,
    refetch,
  } = usePaymentProviders();
  const { mutateAsync, isPending } = useUpdatePaymentProvider();
  const [submissionError, setSubmissionError] = useState<string | null>(
    null,
  );
  const provider = providers?.find(
    (candidate) =>
      candidate.paymentProviderId === paymentProviderId,
  );

  const form = useForm<UpdatePaymentProviderFormValues>({
    resolver: zodResolver(createUpdatePaymentProviderSchema()),
    mode: "onBlur",
    defaultValues: {
      frontendResultUrl: "",
      countryCode: "",
      manualCapture: false,
      captureDelayHours: "",
      maxRefundDays: 0,
      storeId: "",
      isEnabled: true,
      checkoutPaymentMethodTypes: [],
      paymentMethodConfigurationId: "",
    },
  });

  useEffect(() => {
    if (!provider) {
      return;
    }

    form.reset({
      frontendResultUrl: provider.frontendResultUrl ?? "",
      countryCode: provider.countryCode ?? "",
      manualCapture: provider.manualCapture,
      captureDelayHours: provider.captureDelayHours?.toString() ?? "",
      maxRefundDays: provider.maxRefundDays,
      storeId: provider.storeId ?? "",
      isEnabled: provider.isEnabled,
      checkoutPaymentMethodTypes: provider.checkoutPaymentMethodTypes ?? [],
      paymentMethodConfigurationId:
        provider.paymentMethodConfigurationId ?? "",
    });
  }, [form, provider]);

  if (isLoading) {
    return (
      <main className="min-w-0 p-4 sm:p-6 lg:p-8">
        <PaymentProviderPageSkeleton />
      </main>
    );
  }

  if (isError) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <PaymentProviderPageHeader
          title={t("UPDATE_PAYMENT_PROVIDER_PAGE.UPDATE_PAYMENT_PROVIDER")}
          description={t("UPDATE_PAYMENT_PROVIDER_PAGE.EDIT_NON_SECRET_PROVIDER")}
          backTo={providersPath}
        />
        <PaymentProviderLoadError
          message={
            error instanceof Error
              ? error.message
              : t("UPDATE_PAYMENT_PROVIDER_PAGE.THE_PAYMENT_PROVIDER_COULD_NOT")
          }
          onRetry={() => refetch()}
        />
      </main>
    );
  }

  if (!provider) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <PaymentProviderPageHeader
          title={t("UPDATE_PAYMENT_PROVIDER_PAGE.UPDATE_PAYMENT_PROVIDER")}
          description={t("UPDATE_PAYMENT_PROVIDER_PAGE.EDIT_NON_SECRET_PROVIDER")}
          backTo={providersPath}
        />
        <PaymentProviderNotFound />
      </main>
    );
  }

  const submit = async (
    values: UpdatePaymentProviderFormValues,
  ) => {
    setSubmissionError(null);

    const request: UpdatePaymentProviderRequest = {
      version: provider.version,
      frontendResultUrl: values.frontendResultUrl.trim(),
      countryCode: normalizeOptional(values.countryCode)?.toUpperCase(),
      manualCapture: values.manualCapture,
      // Always sent, hidden or not: the update replaces the whole configuration, so leaving it
      // out would clear a delay this form is not showing (it is Adyen-only).
      captureDelayHours: parseCaptureDelayHours(values.captureDelayHours),
      maxRefundDays: values.maxRefundDays,
      storeId: normalizeOptional(values.storeId),
      isEnabled: values.isEnabled,
      // Omitted when empty: the server reads an absent list and an empty one the same way, as
      // "defer to the account's configuration", so unticking everything clears the selection.
      //
      // Sent whatever the provider is, rather than only for Stripe. The fields are hidden for
      // anything else, but they are hydrated from the stored provider either way — so sending
      // them back preserves what is there, where stripping them would quietly clear a value this
      // form never showed.
      checkoutPaymentMethodTypes:
        values.checkoutPaymentMethodTypes.length > 0
          ? values.checkoutPaymentMethodTypes
          : undefined,
      paymentMethodConfigurationId: normalizeOptional(
        values.paymentMethodConfigurationId,
      ),
    };

    try {
      const updated = await mutateAsync({
        paymentProviderId: provider.paymentProviderId,
        request,
      });

      toast({
        variant: "success",
        title: t("UPDATE_PAYMENT_PROVIDER_PAGE.PROVIDER_CONFIGURATION_UPDATED"),
        description: t("UPDATE_PAYMENT_PROVIDER_PAGE.IS_NOW_AT_VERSION", { providerName: providerDisplayName(updated.providerName), version: updated.version }),
      });
      navigate(providersPath);
    } catch (mutationError) {
      setSubmissionError(
        mutationError instanceof Error
          ? mutationError.message
          : t("UPDATE_PAYMENT_PROVIDER_PAGE.THE_PROVIDER_CONFIGURATION_COULD"),
      );
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <PaymentProviderPageHeader
        title={t("UPDATE_PAYMENT_PROVIDER_PAGE.UPDATE_PAYMENT_PROVIDER")}
        description={t("UPDATE_PAYMENT_PROVIDER_PAGE.CHANGE_RUNTIME_CONFIGURATION")}
        backTo={providersPath}
        icon={<Pencil className="h-6 w-6" />}
      />

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
                      {t("UPDATE_PAYMENT_PROVIDER_PAGE.MERCHANT_ID", { merchantId: provider.merchantId })}
                    </p>
                  </div>
                  <Badge variant="info">{t("UPDATE_PAYMENT_PROVIDER_PAGE.VERSION", { version: provider.version })}</Badge>
                </div>
                <p className="mt-4 text-xs text-muted-foreground">
                  {t("UPDATE_PAYMENT_PROVIDER_PAGE.PROVIDER_NAME_AND_MERCHANT_ID")}
                </p>
              </section>

              <section className="space-y-5">
                <div>
                  <h2 className="text-lg font-semibold">
                    {t("UPDATE_PAYMENT_PROVIDER_PAGE.RUNTIME_CONFIGURATION")}
                  </h2>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("UPDATE_PAYMENT_PROVIDER_PAGE.SAVING_REPLACES_ONLY_THE_FIELDS")}
                  </p>
                </div>
                <PaymentProviderConfigurationFields
                  includeEnabled
                  providerName={provider.providerName}
                  additionalPaymentMethods={
                    provider.checkoutPaymentMethodTypes ?? []
                  }
                />
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
                    <Pencil className="mr-2 h-4 w-4" />
                  )}
                  {isPending ? t("UPDATE_PAYMENT_PROVIDER_PAGE.SAVING_CHANGES") : t("common:SAVE_CHANGES")}
                </Button>
              </div>
            </form>
          </Form>
        </Card>

        {/* Repeated from the create page on purpose: this is where an administrator returns
            when the provider is registered but its events are not arriving. */}
        <PaymentWebhookEndpointsCard
          providerName={provider.providerName}
        />

        <Card className="rounded-xl">
          <div className="flex items-start gap-3">
            <ShieldCheck className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
            <div>
              <h2 className="font-semibold">{t("UPDATE_PAYMENT_PROVIDER_PAGE.CONCURRENCY_PROTECTED")}</h2>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                {t("UPDATE_PAYMENT_PROVIDER_PAGE.VERSION_SENT_WITH_UPDATE", { version: provider.version })}
              </p>
            </div>
          </div>
        </Card>
      </div>
    </main>
  );
};
