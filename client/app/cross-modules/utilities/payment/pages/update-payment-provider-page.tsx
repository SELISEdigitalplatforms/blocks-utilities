import { useEffect, useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Pencil, ShieldCheck } from "lucide-react";
import { useForm } from "react-hook-form";
import { useNavigate, useParams } from "react-router-dom";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Form } from "@/components/ui-kits/form/form";
import { toast } from "@/hooks/use-toast";
import { PaymentProviderConfigurationFields } from "../components/payment-provider-configuration-fields";
import { PaymentProviderPageHeader } from "../components/payment-provider-page-header";
import {
  PaymentProviderLoadError,
  PaymentProviderNotFound,
  PaymentProviderPageSkeleton,
} from "../components/payment-provider-state";
import { usePaymentProviders } from "../hooks/use-payment-providers";
import { useUpdatePaymentProvider } from "../hooks/use-update-payment-provider";
import type { UpdatePaymentProviderRequest } from "../models/payment-provider.model";
import {
  providerDisplayName,
  updatePaymentProviderSchema,
  type UpdatePaymentProviderFormValues,
} from "../schemas/payment-provider.schema";

const normalizeOptional = (value?: string): string | undefined => {
  const normalized = value?.trim();
  return normalized || undefined;
};

export const UpdatePaymentProviderPage = () => {
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
    resolver: zodResolver(updatePaymentProviderSchema),
    mode: "onBlur",
    defaultValues: {
      frontendResultUrl: "",
      countryCode: "",
      manualCapture: false,
      maxRefundDays: 0,
      storeId: "",
      isEnabled: true,
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
      maxRefundDays: provider.maxRefundDays,
      storeId: provider.storeId ?? "",
      isEnabled: provider.isEnabled,
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
          title="Update payment provider"
          description="Edit non-secret provider configuration."
          backTo={providersPath}
        />
        <PaymentProviderLoadError
          message={
            error instanceof Error
              ? error.message
              : "The payment provider could not be loaded."
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
          title="Update payment provider"
          description="Edit non-secret provider configuration."
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
      maxRefundDays: values.maxRefundDays,
      storeId: normalizeOptional(values.storeId),
      isEnabled: values.isEnabled,
    };

    try {
      const updated = await mutateAsync({
        paymentProviderId: provider.paymentProviderId,
        request,
      });

      toast({
        variant: "success",
        title: "Provider configuration updated",
        description: `${providerDisplayName(updated.providerName)} is now at version ${updated.version}.`,
      });
      navigate(providersPath);
    } catch (mutationError) {
      setSubmissionError(
        mutationError instanceof Error
          ? mutationError.message
          : "The provider configuration could not be updated.",
      );
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <PaymentProviderPageHeader
        title="Update payment provider"
        description="Change runtime configuration without touching provider identity or credentials."
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
                      Merchant: {provider.merchantId}
                    </p>
                  </div>
                  <Badge variant="info">Version {provider.version}</Badge>
                </div>
                <p className="mt-4 text-xs text-muted-foreground">
                  Provider name and merchant ID are immutable. Register a new
                  provider to change either value.
                </p>
              </section>

              <section className="space-y-5">
                <div>
                  <h2 className="text-lg font-semibold">
                    Runtime configuration
                  </h2>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Saving replaces only the fields shown below.
                  </p>
                </div>
                <PaymentProviderConfigurationFields includeEnabled />
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
                    <Pencil className="mr-2 h-4 w-4" />
                  )}
                  {isPending ? "Saving changes…" : "Save changes"}
                </Button>
              </div>
            </form>
          </Form>
        </Card>

        <Card className="rounded-xl">
          <div className="flex items-start gap-3">
            <ShieldCheck className="mt-0.5 h-5 w-5 text-blocks-primary-600" />
            <div>
              <h2 className="font-semibold">Concurrency protected</h2>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                Version {provider.version} is sent with this update. If
                another administrator saves first, this request is rejected
                instead of overwriting their change.
              </p>
            </div>
          </div>
        </Card>
      </div>
    </main>
  );
};
