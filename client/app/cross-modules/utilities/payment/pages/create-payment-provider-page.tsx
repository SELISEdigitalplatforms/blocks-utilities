import { useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Info,
  KeyRound,
  Loader2,
  LockKeyhole,
  Plus,
} from "lucide-react";
import { useForm, useWatch, type Resolver } from "react-hook-form";
import { useNavigate, useParams } from "react-router";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
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
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";
import { PaymentProviderConfigurationFields } from "../components/payment-provider-configuration-fields";
import { PaymentProviderPageHeader } from "../components/payment-provider-page-header";
import { PaymentWebhookEndpointsCard } from "../components/payment-webhook-endpoints-card";
import { useRegisterPaymentProvider } from "../hooks/use-register-payment-provider";
import type { RegisterPaymentProviderRequest } from "../models/payment-provider.model";
import {
  parseCaptureDelayHours,
  providerDisplayName,
  createRegisterPaymentProviderSchema,
  type RegisterPaymentProviderFormValues,
} from "../schemas/payment-provider.schema";

const ADYEN_TEST_API_BASE_URL =
  "https://checkout-test.adyen.com/v72";

/**
 * Radix rejects an empty string as a SelectItem value, so "leave it to my context" needs a
 * sentinel. It never leaves this file: submit maps it back to an omitted field.
 */
const CONTEXT_ORGANIZATION = "__context__";

const ORGANIZATION_PAGE_SIZE = 200;

const normalizeOptional = (value?: string): string | undefined => {
  const normalized = value?.trim();
  return normalized || undefined;
};

export const CreatePaymentProviderPage = () => {
  const { t } = useT("utilities.payment");
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

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData, isError: organizationsFailed } =
    useGetOrganizations({
      projectKey: tenantId,
      page: 0,
      pageSize: ORGANIZATION_PAGE_SIZE,
    });
  const organizations = organizationsData?.organizations ?? [];

  const form = useForm<RegisterPaymentProviderFormValues>({
    resolver: zodResolver(createRegisterPaymentProviderSchema()) as Resolver<RegisterPaymentProviderFormValues>,
    mode: "onBlur",
    defaultValues: {
      providerName: "ADYEN-ONLINE",
      merchantId: "",
      organizationId: "",
      organizationIds: [],
      frontendResultUrl: defaultResultUrl,
      apiBaseUrl: ADYEN_TEST_API_BASE_URL,
      countryCode: "",
      manualCapture: false,
      captureDelayHours: "",
      maxRefundDays: 365,
      storeId: "",
      checkoutPaymentMethodTypes: [],
      paymentMethodConfigurationId: "",
      apiKey: "",
      webhookHmacKey: "",
      tokenHmacKey: "",
    },
  });

  const providerName = useWatch({
    control: form.control,
    name: "providerName",
  });
  const primaryOrganizationId = useWatch({
    control: form.control,
    name: "organizationId",
  });

  // The organization chosen above is already being configured, so offering it again as an
  // extra would show it twice and invite a selection the server would just de-duplicate.
  const additionalOrganizations = organizations.filter(
    (organization) => organization.itemId !== primaryOrganizationId,
  );

  const submit = async (
    values: RegisterPaymentProviderFormValues,
  ) => {
    setSubmissionError(null);

    const request: RegisterPaymentProviderRequest = {
      providerName: values.providerName,
      merchantId: values.merchantId.trim(),
      organizationId: normalizeOptional(values.organizationId),
      organizationIds:
        values.organizationIds.length > 0
          ? values.organizationIds
          : undefined,
      frontendResultUrl: values.frontendResultUrl.trim(),
      countryCode: normalizeOptional(values.countryCode)?.toUpperCase(),
      manualCapture: values.manualCapture,
      captureDelayHours: parseCaptureDelayHours(values.captureDelayHours),
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
      // Stripe-only, and omitted when nothing was ticked: the server reads an absent list and an
      // empty one the same way, as "defer to the account's configuration". Sending [] instead
      // would be a checkout offering nothing, which Stripe rejects.
      checkoutPaymentMethodTypes:
        values.providerName === "STRIPE" &&
        values.checkoutPaymentMethodTypes.length > 0
          ? values.checkoutPaymentMethodTypes
          : undefined,
      paymentMethodConfigurationId:
        values.providerName === "STRIPE"
          ? normalizeOptional(values.paymentMethodConfigurationId)
          : undefined,
    };

    try {
      const result = await mutateAsync(request);
      const name = providerDisplayName(result.providerName);
      const failed = result.organizations.filter(
        (outcome) => !outcome.isSuccess,
      );

      // Partial success is a real outcome, not an error: the organizations that succeeded are
      // configured and staying. Reporting it as a failure would invite a retry that then
      // conflicts on every one that already worked.
      if (failed.length > 0) {
        const configured = result.organizations.length - failed.length;

        toast({
          variant: "warning",
          title: t("CREATE_PAYMENT_PROVIDER_PAGE.CONFIGURED_FOR_OF_ORGANIZATIONS", { name, configured, organizationsCount: result.organizations.length }),
          description: t("CREATE_PAYMENT_PROVIDER_PAGE.ORGANIZATIONS_FAILED", {
            organizations: failed.map((outcome) => outcome.organizationId).join(", "),
            errorMessage: failed[0].errorMessage ?? t("CREATE_PAYMENT_PROVIDER_PAGE.UNKNOWN_ERROR"),
          }),
        });
      } else {
        toast({
          variant: "success",
          title: t("CREATE_PAYMENT_PROVIDER_PAGE.PAYMENT_PROVIDER_CREATED"),
          description:
            result.organizations.length > 1
              ? t("CREATE_PAYMENT_PROVIDER_PAGE.IS_READY_FOR_ORGANIZATIONS", { name, organizationsCount: result.organizations.length })
              : t("CREATE_PAYMENT_PROVIDER_PAGE.IS_READY_TO_CONFIGURE_PAYMENTS", { name }),
        });
      }

      navigate(providersPath);
    } catch (error) {
      setSubmissionError(
        error instanceof Error
          ? error.message
          : t("CREATE_PAYMENT_PROVIDER_PAGE.THE_PAYMENT_PROVIDER_COULD_NOT"),
      );
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <PaymentProviderPageHeader
        title={t("CREATE_PAYMENT_PROVIDER_PAGE.CREATE_PAYMENT_PROVIDER")}
        description={t("CREATE_PAYMENT_PROVIDER_PAGE.REGISTER_A_PROVIDER_FOR_THE")}
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
                    {t("CREATE_PAYMENT_PROVIDER_PAGE.PROVIDER_IDENTITY")}
                  </h2>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("CREATE_PAYMENT_PROVIDER_PAGE.PROVIDER_AND_MERCHANT_FORM_THE")}
                  </p>
                </div>

                <div className="grid gap-5 sm:grid-cols-2">
                  <FormField
                    control={form.control}
                    name="providerName"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>{t("CREATE_PAYMENT_PROVIDER_PAGE.PROVIDER")}</FormLabel>
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

                            // Stripe's own concept, and the fields are hidden for anything
                            // else. Cleared rather than left in state so what was ticked for
                            // one provider cannot be carried into another silently.
                            if (value !== "STRIPE") {
                              form.setValue(
                                "checkoutPaymentMethodTypes",
                                [],
                              );
                              form.setValue(
                                "paymentMethodConfigurationId",
                                "",
                              );
                            }

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
                          {t("CREATE_PAYMENT_PROVIDER_PAGE.CHANGING_THIS_LATER_REQUIRES_A")}
                        </FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  <FormField
                    control={form.control}
                    name="organizationId"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>{t("CREATE_PAYMENT_PROVIDER_PAGE.ORGANIZATION")}</FormLabel>
                        <Select
                          value={field.value || CONTEXT_ORGANIZATION}
                          onValueChange={(value) =>
                            field.onChange(
                              value === CONTEXT_ORGANIZATION ? "" : value,
                            )
                          }
                        >
                          <FormControl>
                            <SelectTrigger>
                              <SelectValue />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            <SelectItem value={CONTEXT_ORGANIZATION}>
                              {t("CREATE_PAYMENT_PROVIDER_PAGE.EVERY_ORGANIZATION_IN_THIS_TENANT")}
                            </SelectItem>
                            {organizations.map((organization) => (
                              <SelectItem
                                key={organization.itemId}
                                value={organization.itemId}
                              >
                                {organization.name}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        <FormDescription>
                          {organizationsFailed
                            ? t("CREATE_PAYMENT_PROVIDER_PAGE.ORGANIZATIONS_COULD_NOT_BE_LOADED")
                            : t("CREATE_PAYMENT_PROVIDER_PAGE.WHICH_ORGANIZATION_THIS")}
                        </FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  {additionalOrganizations.length > 0 && (
                    <FormField
                      control={form.control}
                      name="organizationIds"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel>
                            {t("CREATE_PAYMENT_PROVIDER_PAGE.ALSO_CONFIGURE_THESE_ORGANIZATIONS")}
                          </FormLabel>
                          <div className="max-h-56 space-y-2 overflow-y-auto rounded-md border p-3">
                            {additionalOrganizations.map((organization) => (
                              <label
                                key={organization.itemId}
                                className="flex cursor-pointer items-center gap-2 text-sm"
                              >
                                <Checkbox
                                  checked={field.value?.includes(
                                    organization.itemId,
                                  )}
                                  onCheckedChange={(checked) =>
                                    field.onChange(
                                      checked
                                        ? [
                                            ...(field.value ?? []),
                                            organization.itemId,
                                          ]
                                        : (field.value ?? []).filter(
                                            (id) => id !== organization.itemId,
                                          ),
                                    )
                                  }
                                />
                                {organization.name}
                              </label>
                            ))}
                          </div>
                          <FormDescription>
                            {t("CREATE_PAYMENT_PROVIDER_PAGE.EACH_SELECTED_ORGANIZATION_GETS")}
                          </FormDescription>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  )}

                  <FormField
                    control={form.control}
                    name="merchantId"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>{t("CREATE_PAYMENT_PROVIDER_PAGE.MERCHANT_ID")}</FormLabel>
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
                          {t("CREATE_PAYMENT_PROVIDER_PAGE.THE_PROVIDER_MERCHANT_OR_ACCOUNT")}
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
                          <FormLabel>{t("CREATE_PAYMENT_PROVIDER_PAGE.CHECKOUT_API_BASE_URL")}</FormLabel>
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
                            {t("CREATE_PAYMENT_PROVIDER_PAGE.USE_THE_APPROVED_ADYEN_CHECKOUT")}
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
                    {t("CREATE_PAYMENT_PROVIDER_PAGE.PAYMENT_CONFIGURATION")}
                  </h2>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("CREATE_PAYMENT_PROVIDER_PAGE.RUNTIME_BEHAVIOR_THAT_CAN_BE")}
                  </p>
                </div>
                <PaymentProviderConfigurationFields
                  providerName={providerName}
                />
              </section>

              <section className="space-y-5 border-t pt-6">
                <div className="flex items-start gap-3">
                  <span className="rounded-lg bg-amber-50 p-2 text-amber-700">
                    <KeyRound className="h-5 w-5" />
                  </span>
                  <div>
                    <h2 className="text-lg font-semibold">{t("CREATE_PAYMENT_PROVIDER_PAGE.CREDENTIALS")}</h2>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {t("CREATE_PAYMENT_PROVIDER_PAGE.VALUES_ARE_SENT_ONCE_OVER")}
                    </p>
                  </div>
                </div>

                <div className="grid gap-5 sm:grid-cols-2">
                  <FormField
                    control={form.control}
                    name="apiKey"
                    render={({ field }) => (
                      <FormItem className="sm:col-span-2">
                        <FormLabel>{t("CREATE_PAYMENT_PROVIDER_PAGE.API_KEY")}</FormLabel>
                        <FormControl>
                          <Input
                            {...field}
                            type="password"
                            autoComplete="new-password"
                            spellCheck={false}
                            placeholder={
                              providerName === "STRIPE"
                                ? "sk_… or rk_…"
                                : t("CREATE_PAYMENT_PROVIDER_PAGE.ADYEN_CHECKOUT_API_KEY")
                            }
                          />
                        </FormControl>
                        <FormDescription>
                          {t("CREATE_PAYMENT_PROVIDER_PAGE.USED_ONLY_FOR_AUTHENTICATED_CALLS")}
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
                            ? t("CREATE_PAYMENT_PROVIDER_PAGE.WEBHOOK_ENDPOINT_SECRET")
                            : t("CREATE_PAYMENT_PROVIDER_PAGE.STANDARD_WEBHOOK_HMAC")}
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
                                : t("CREATE_PAYMENT_PROVIDER_PAGE.N_64_HEXADECIMAL_CHARACTERS")
                            }
                          />
                        </FormControl>
                        <FormDescription>
                          {t("CREATE_PAYMENT_PROVIDER_PAGE.VERIFIES_PAYMENT_LIFECYCLE")}
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
                          <FormLabel>{t("CREATE_PAYMENT_PROVIDER_PAGE.TOKEN_WEBHOOK_HMAC")}</FormLabel>
                          <FormControl>
                            <Input
                              {...field}
                              value={field.value ?? ""}
                              type="password"
                              autoComplete="new-password"
                              spellCheck={false}
                              placeholder={t("CREATE_PAYMENT_PROVIDER_PAGE.N_64_HEXADECIMAL_CHARACTERS")}
                            />
                          </FormControl>
                          <FormDescription>
                            {t("CREATE_PAYMENT_PROVIDER_PAGE.VERIFIES_STORED_PAYMENT_METHOD")}
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
                    <Plus className="mr-2 h-4 w-4" />
                  )}
                  {isPending ? t("CREATE_PAYMENT_PROVIDER_PAGE.CREATING_PROVIDER") : t("CREATE_PAYMENT_PROVIDER_PAGE.CREATE_PROVIDER")}
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
                <h2 className="font-semibold">{t("CREATE_PAYMENT_PROVIDER_PAGE.IDENTITY_KEYS")}</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {t("CREATE_PAYMENT_PROVIDER_PAGE.RETURN_STATE_AND_SHOPPER_REFERENCE")}
                </p>
              </div>
            </div>
          </Card>

          <PaymentWebhookEndpointsCard providerName={providerName} />

          <Card className="rounded-xl">
            <div className="flex items-start gap-3">
              <Info className="mt-0.5 h-5 w-5 text-blue-700" />
              <div>
                <h2 className="font-semibold">{t("CREATE_PAYMENT_PROVIDER_PAGE.BEFORE_CREATING")}</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {t("CREATE_PAYMENT_PROVIDER_PAGE.REGISTER_THE_ENDPOINTS_ABOVE_IN")}
                </p>
              </div>
            </div>
          </Card>
        </div>
      </div>
    </main>
  );
};
