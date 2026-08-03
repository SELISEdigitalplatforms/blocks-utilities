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
import { PAYMENT_CURRENCY_OPTIONS } from "../constants/payment.constants";
import { useCreatePayment } from "../hooks/use-create-payment";
import type {
  CreatedPayment,
  CreatePaymentRequest,
} from "../models/create-payment.model";
import { createPaymentSchema } from "../schemas/create-payment.schema";

const createDefaultOrderId = () => `TEST-ORDER-${Date.now()}`;

const getSecureCheckoutUrl = (redirectUrl: string | null): string => {
  if (!redirectUrl) {
    throw new Error("The payment provider did not return a checkout URL.");
  }

  const parsedUrl = new URL(redirectUrl);

  if (parsedUrl.protocol !== "https:") {
    throw new Error("The payment provider returned an unsafe checkout URL.");
  }

  return parsedUrl.toString();
};

export const CreatePaymentPage = () => {
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
    resolver: zodResolver(createPaymentSchema),
    mode: "onBlur",
    defaultValues: {
      providerName: "ADYEN-ONLINE",
      amount: 10,
      currencyCode: "CHF",
      orderId: createDefaultOrderId(),
      rememberCard: false,
      isRecurring: false,
    },
  });

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
          : "The payment could not be created. Try again.",
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
              Back to payments
            </Link>
          </Button>
          <div className="flex items-start gap-4">
            <div className="rounded-xl bg-blocks-primary-600 p-3 text-white shadow-sm">
              <CreditCard className="h-6 w-6" />
            </div>
            <div>
              <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">
                Test hosted payment
              </h1>
              <p className="mt-1 max-w-2xl text-sm text-muted-foreground sm:text-base">
                Create a payment session and continue securely on the
                provider’s hosted checkout page.
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
                <h2 className="text-lg font-semibold">Payment details</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  These values are sent to the tenant-scoped payment API.
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
                        onValueChange={field.onChange}
                      >
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue placeholder="Select a provider" />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          <SelectItem value="ADYEN-ONLINE">Adyen</SelectItem>
                          <SelectItem value="STRIPE">Stripe</SelectItem>
                        </SelectContent>
                      </Select>
                      <FormDescription>
                        Select the configured payment provider for this
                        checkout.
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
                      <FormLabel>Order ID</FormLabel>
                      <FormControl>
                        <Input
                          {...field}
                          maxLength={80}
                          autoComplete="off"
                          placeholder="TEST-ORDER-001"
                        />
                      </FormControl>
                      <FormDescription>
                        A unique reference from the client application.
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
                      <FormLabel>Amount</FormLabel>
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
                        Enter the amount in the selected currency.
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
                      <FormLabel>Currency</FormLabel>
                      <Select
                        value={field.value}
                        onValueChange={field.onChange}
                      >
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue placeholder="Select a currency" />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          {PAYMENT_CURRENCY_OPTIONS.map((currency) => (
                            <SelectItem
                              key={currency.code}
                              value={currency.code}
                            >
                              {currency.code} — {currency.name}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                      <FormDescription>
                        Select a currency supported by the payment service.
                      </FormDescription>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </div>

              <div className="space-y-4 border-t pt-5">
                <h2 className="text-base font-semibold">
                  Checkout preferences
                </h2>

                <FormField
                  control={form.control}
                  name="rememberCard"
                  render={({ field }) => (
                    <FormItem className="flex items-start justify-between gap-4 rounded-xl border p-4">
                      <div>
                        <FormLabel>Offer to save payment method</FormLabel>
                        <FormDescription>
                          The hosted checkout asks the shopper for explicit
                          consent before storing the method.
                        </FormDescription>
                        <FormMessage />
                      </div>
                      <FormControl>
                        <Switch
                          checked={field.value}
                          onCheckedChange={field.onChange}
                          aria-label="Offer to save payment method"
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
                        <FormLabel>Recurring payment</FormLabel>
                        <FormDescription>
                          This create-payment endpoint supports hosted
                          shopper-present payments only.
                        </FormDescription>
                        <FormMessage />
                      </div>
                      <FormControl>
                        <Switch
                          checked={field.value}
                          disabled
                          aria-label="Recurring payment is disabled"
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
                  ? "Creating secure checkout…"
                  : "Create and open checkout"}
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
                <h2 className="font-semibold">Secure redirect flow</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  Checkout opens in a new tab. Card details stay on the
                  provider’s hosted page and are never entered in this
                  application.
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
                <h2 className="font-semibold">Before testing</h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  Configure the provider’s frontend result URL to this
                  client’s payment result route. The provider return flow
                  appends the payment ID and result status.
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
                    Payment session created
                  </h2>
                  <p className="mt-1 break-all font-mono text-xs text-green-800">
                    {createdPayment.paymentDetailId}
                  </p>
                  <p className="mt-2 text-sm text-green-800">
                    {checkoutOpened
                      ? "Checkout opened in a new tab."
                      : "Your browser blocked the automatic checkout tab."}
                  </p>
                  {manualCheckoutUrl && (
                    <Button asChild className="mt-4" size="sm">
                      <a
                        href={manualCheckoutUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        Open checkout
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
