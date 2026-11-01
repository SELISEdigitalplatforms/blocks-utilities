import { useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  ArrowLeft,
  CheckCircle2,
  CreditCard,
  ExternalLink,
  Info,
  Loader2,
  LockKeyhole,
} from "lucide-react";
import { useForm } from "react-hook-form";
import { Link, useParams } from "react-router";
import { v4 as createUuid } from "uuid";
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
import { Switch } from "@/components/ui-kits/switch/switch";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore, useT, tStatic } from "@seliseblocks/genesis-os";
import { getCurrencyName, PAYMENT_CURRENCY_OPTIONS } from "../constants/payment.constants";
import { useCreatePayment } from "../hooks/use-create-payment";
import type {
  CreatedPayment,
  CreatePaymentRequest,
} from "../models/create-payment.model";
import { createPaymentSchema } from "../schemas/create-payment.schema";

const createDefaultOrderId = () => `TEST-ORDER-${Date.now()}`;

/** Radix rejects an empty string as a SelectItem value, so "use my context" needs a sentinel. */
const CONTEXT_ORGANIZATION = "__context__";

const ORGANIZATION_PAGE_SIZE = 200;

const getSecureCheckoutUrl = (redirectUrl: string | null): string => {
  if (!redirectUrl) {
    throw new Error(tStatic("utilities.payment", "CREATE_PAYMENT_PAGE.THE_PAYMENT_PROVIDER_DID_NOT"));
  }

  const parsedUrl = new URL(redirectUrl);

  if (parsedUrl.protocol !== "https:") {
    throw new Error(tStatic("utilities.payment", "CREATE_PAYMENT_PAGE.THE_PAYMENT_PROVIDER_RETURNED_AN"));
  }

  return parsedUrl.toString();
};

export const CreatePaymentPage = () => {
  const { t } = useT("utilities.payment");
  const { itemId } = useParams();
  const { mutateAsync, isPending } = useCreatePayment();
  const [createdPayment, setCreatedPayment] =
    useState<CreatedPayment | null>(null);
  const [manualCheckoutUrl, setManualCheckoutUrl] = useState<string | null>(
    null,
  );
  const [submissionError, setSubmissionError] = useState<string | null>(null);
  const [checkoutOpened, setCheckoutOpened] = useState(false);

  const form = useForm<CreatePaymentRequest>({
    resolver: zodResolver(createPaymentSchema()),
    mode: "onBlur",
    defaultValues: {
      providerName: "ADYEN-ONLINE",
      amount: 10,
      currencyCode: "CHF",
      orderId: createDefaultOrderId(),
      rememberCard: false,
      isRecurring: false,
      organizationId: "",
    },
  });

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData, isError: organizationsFailed } =
    useGetOrganizations({
      projectKey: tenantId,
      page: 0,
      pageSize: ORGANIZATION_PAGE_SIZE,
    });
  const organizations = organizationsData?.organizations ?? [];

  const submit = async (request: CreatePaymentRequest) => {
    setSubmissionError(null);
    setCreatedPayment(null);
    setManualCheckoutUrl(null);
    setCheckoutOpened(false);

    try {
      const payment = await mutateAsync({
        request: {
          ...request,
          currencyCode: request.currencyCode.toUpperCase(),
          orderId: request.orderId.trim(),
          // Omitted rather than sent blank: an empty string is a real organization
          // identifier as far as the server is concerned.
          organizationId: request.organizationId?.trim() || undefined,
        },
        idempotencyKey: createUuid(),
      });
      const checkoutUrl = getSecureCheckoutUrl(payment.redirectUrl);
      const checkoutWindow = window.open(checkoutUrl, "_blank");

      setCreatedPayment(payment);

      if (checkoutWindow) {
        checkoutWindow.opener = null;
        setCheckoutOpened(true);
      } else {
        setManualCheckoutUrl(checkoutUrl);
      }
    } catch (error) {
      setSubmissionError(
        error instanceof Error
          ? error.message
          : t("CREATE_PAYMENT_PAGE.THE_PAYMENT_COULD_NOT_BE"),
      );
    }
  };

  const paymentListPath = `/app/${itemId ?? ""}/payment/list`;

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <section className="relative overflow-hidden rounded-2xl border bg-gradient-to-br from-blocks-primary-shades-100 via-card to-blocks-secondary-50 p-5 shadow-sm sm:p-7">
        <div className="absolute -right-16 -top-20 h-52 w-52 rounded-full bg-blocks-primary-100/30 blur-3xl" />
        <div className="relative">
          <Button asChild variant="ghost" size="sm" className="-ml-3 mb-3">
            <Link to={paymentListPath}>
              <ArrowLeft className="mr-2 h-4 w-4" />
              {t("CREATE_PAYMENT_PAGE.BACK_TO_PAYMENTS")}
            </Link>
          </Button>
          <div className="flex items-start gap-4">
            <div className="rounded-xl bg-blocks-primary-600 p-3 text-white shadow-sm">
              <CreditCard className="h-6 w-6" />
            </div>
            <div>
              <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">
                {t("CREATE_PAYMENT_PAGE.TEST_HOSTED_PAYMENT")}
              </h1>
              <p className="mt-1 max-w-2xl text-sm text-muted-foreground sm:text-base">
                {t("CREATE_PAYMENT_PAGE.CREATE_A_PAYMENT_SESSION_AND")}
              </p>
            </div>
          </div>
        </div>
      </section>

      <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card className="rounded-xl p-5 sm:p-6">
          <Form {...form}>
            <form
              className="space-y-6"
              onSubmit={form.handleSubmit(submit)}
              noValidate
            >
              <div>
                <h2 className="text-lg font-semibold">{t("CREATE_PAYMENT_PAGE.PAYMENT_DETAILS")}</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t("CREATE_PAYMENT_PAGE.THESE_VALUES_ARE_SENT_TO")}
                </p>
              </div>

              <div className="grid gap-5 sm:grid-cols-2">
                <FormField
                  control={form.control}
                  name="providerName"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>{t("CREATE_PAYMENT_PAGE.PROVIDER")}</FormLabel>
                      <Select
                        value={field.value}
                        onValueChange={field.onChange}
                      >
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue placeholder={t("CREATE_PAYMENT_PAGE.SELECT_A_PROVIDER")} />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          <SelectItem value="ADYEN-ONLINE">Adyen</SelectItem>
                          <SelectItem value="STRIPE">Stripe</SelectItem>
                        </SelectContent>
                      </Select>
                      <FormDescription>
                        {t("CREATE_PAYMENT_PAGE.SELECT_THE_CONFIGURED_PAYMENT")}
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
                      <FormLabel>{t("CREATE_PAYMENT_PAGE.ORGANIZATION")}</FormLabel>
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
                            {t("CREATE_PAYMENT_PAGE.USE_MY_CURRENT_ORGANIZATION")}
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
                          ? t("CREATE_PAYMENT_PAGE.ORGANIZATIONS_COULD_NOT_BE_LOADED")
                          : t("CREATE_PAYMENT_PAGE.DECIDES_WHICH_ORGANIZATION_S")}
                      </FormDescription>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                <FormField
                  control={form.control}
                  name="orderId"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>{t("CREATE_PAYMENT_PAGE.ORDER_ID")}</FormLabel>
                      <FormControl>
                        <Input
                          {...field}
                          maxLength={80}
                          autoComplete="off"
                          placeholder="TEST-ORDER-001"
                        />
                      </FormControl>
                      <FormDescription>
                        {t("CREATE_PAYMENT_PAGE.A_UNIQUE_REFERENCE_FROM_THE")}
                      </FormDescription>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                <FormField
                  control={form.control}
                  name="amount"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>{t("CREATE_PAYMENT_PAGE.AMOUNT")}</FormLabel>
                      <FormControl>
                        <Input
                          name={field.name}
                          ref={field.ref}
                          value={
                            Number.isNaN(field.value) ? "" : field.value
                          }
                          type="number"
                          min="0.01"
                          max="999999999"
                          step="0.01"
                          inputMode="decimal"
                          placeholder="10.00"
                          onBlur={field.onBlur}
                          onChange={(event) =>
                            field.onChange(event.target.valueAsNumber)
                          }
                        />
                      </FormControl>
                      <FormDescription>
                        {t("CREATE_PAYMENT_PAGE.ENTER_THE_AMOUNT_IN_THE")}
                      </FormDescription>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                <FormField
                  control={form.control}
                  name="currencyCode"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>{t("CREATE_PAYMENT_PAGE.CURRENCY")}</FormLabel>
                      <Select
                        value={field.value}
                        onValueChange={field.onChange}
                      >
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue placeholder={t("CREATE_PAYMENT_PAGE.SELECT_A_CURRENCY")} />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          {PAYMENT_CURRENCY_OPTIONS.map((currency) => (
                            <SelectItem
                              key={currency.code}
                              value={currency.code}
                            >
                              {currency.code} — {getCurrencyName(currency.code, currency.name)}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                      <FormDescription>
                        {t("CREATE_PAYMENT_PAGE.SELECT_A_CURRENCY_SUPPORTED_BY")}
                      </FormDescription>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </div>

              <div className="space-y-4 border-t pt-5">
                <h2 className="text-base font-semibold">
                  {t("CREATE_PAYMENT_PAGE.CHECKOUT_PREFERENCES")}
                </h2>

                <FormField
                  control={form.control}
                  name="rememberCard"
                  render={({ field }) => (
                    <FormItem className="flex items-start justify-between gap-4 rounded-xl border p-4">
                      <div>
                        <FormLabel>{t("CREATE_PAYMENT_PAGE.OFFER_TO_SAVE_PAYMENT_METHOD")}</FormLabel>
                        <FormDescription>
                          {t("CREATE_PAYMENT_PAGE.THE_HOSTED_CHECKOUT_ASKS_THE")}
                        </FormDescription>
                        <FormMessage />
                      </div>
                      <FormControl>
                        <Switch
                          checked={field.value}
                          onCheckedChange={field.onChange}
                          aria-label={t("CREATE_PAYMENT_PAGE.OFFER_TO_SAVE_PAYMENT_METHOD")}
                        />
                      </FormControl>
                    </FormItem>
                  )}
                />

                <FormField
                  control={form.control}
                  name="isRecurring"
                  render={({ field }) => (
                    <FormItem className="flex items-start justify-between gap-4 rounded-xl border bg-muted/30 p-4">
                      <div>
                        <FormLabel>{t("CREATE_PAYMENT_PAGE.RECURRING_PAYMENT")}</FormLabel>
                        <FormDescription>
                          {t("CREATE_PAYMENT_PAGE.THIS_CREATE_PAYMENT_ENDPOINT")}
                        </FormDescription>
                        <FormMessage />
                      </div>
                      <FormControl>
                        <Switch
                          checked={field.value}
                          disabled
                          aria-label={t("CREATE_PAYMENT_PAGE.RECURRING_PAYMENT_IS_DISABLED")}
                        />
                      </FormControl>
                    </FormItem>
                  )}
                />
              </div>

              {submissionError && (
                <div
                  role="alert"
                  className="rounded-lg border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive"
                >
                  {submissionError}
                </div>
              )}

              <Button
                type="submit"
                size="lg"
                className="w-full sm:w-auto"
                disabled={isPending}
              >
                {isPending ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <ExternalLink className="mr-2 h-4 w-4" />
                )}
                {isPending
                  ? t("CREATE_PAYMENT_PAGE.CREATING_SECURE_CHECKOUT")
                  : t("CREATE_PAYMENT_PAGE.CREATE_AND_OPEN_CHECKOUT")}
              </Button>
            </form>
          </Form>
        </Card>

        <div className="space-y-5">
          <Card className="rounded-xl">
            <div className="flex items-start gap-3">
              <span className="rounded-lg bg-blocks-primary-shades-200 p-2 text-blocks-primary-600">
                <LockKeyhole className="h-5 w-5" />
              </span>
              <div>
                <h2 className="font-semibold">{t("CREATE_PAYMENT_PAGE.SECURE_REDIRECT_FLOW")}</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {t("CREATE_PAYMENT_PAGE.CHECKOUT_OPENS_IN_A_NEW")}
                </p>
              </div>
            </div>
          </Card>

          <Card className="rounded-xl">
            <div className="flex items-start gap-3">
              <span className="rounded-lg bg-blue-50 p-2 text-blue-700">
                <Info className="h-5 w-5" />
              </span>
              <div>
                <h2 className="font-semibold">{t("CREATE_PAYMENT_PAGE.BEFORE_TESTING")}</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {t("CREATE_PAYMENT_PAGE.CONFIGURE_THE_PROVIDER_S_FRONTEND")}
                </p>
              </div>
            </div>
          </Card>

          {createdPayment && (
            <Card
              className="rounded-xl border-green-200 bg-green-50/60"
              aria-live="polite"
            >
              <div className="flex items-start gap-3">
                <CheckCircle2 className="mt-0.5 h-5 w-5 text-green-700" />
                <div className="min-w-0">
                  <h2 className="font-semibold text-green-900">
                    {t("CREATE_PAYMENT_PAGE.PAYMENT_SESSION_CREATED")}
                  </h2>
                  <p className="mt-1 break-all font-mono text-xs text-green-800">
                    {createdPayment.paymentDetailId}
                  </p>
                  <p className="mt-2 text-sm text-green-800">
                    {checkoutOpened
                      ? t("CREATE_PAYMENT_PAGE.CHECKOUT_OPENED_IN_A_NEW")
                      : t("CREATE_PAYMENT_PAGE.YOUR_BROWSER_BLOCKED_THE_AUTOMATIC")}
                  </p>
                  {manualCheckoutUrl && (
                    <Button asChild className="mt-4" size="sm">
                      <a
                        href={manualCheckoutUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        {t("CREATE_PAYMENT_PAGE.OPEN_CHECKOUT")}
                        <ExternalLink className="ml-2 h-4 w-4" />
                      </a>
                    </Button>
                  )}
                </div>
              </div>
            </Card>
          )}
        </div>
      </div>
    </main>
  );
};
