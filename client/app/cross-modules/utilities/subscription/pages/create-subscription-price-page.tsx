import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Plus } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
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
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { toast } from "@/hooks/use-toast";
import {
  BILLING_INTERVAL_OPTIONS,
  SUBSCRIPTION_CURRENCY_OPTIONS,
} from "../constants/subscription.constants";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useCreateSubscriptionPrice } from "../hooks/use-create-subscription-price";
import { useOrganizationScope, withOrganizationScope } from "../hooks/use-organization-scope";
import { useSubscriptionPlan } from "../hooks/use-subscription-plan";
import type { CreateSubscriptionPriceRequest } from "../models/subscription-plan.model";
import {
  createSubscriptionPriceSchema,
  defaultSubscriptionPriceFormValues,
  FLAT_FEE,
  type CreateSubscriptionPriceFormValues,
} from "../schemas/subscription-price.schema";
import { toMinorUnits } from "../utilities/subscription-format";

export const CreateSubscriptionPricePage = () => {
  const { itemId, planId } = useParams();
  const navigate = useNavigate();
  const organizationScope = useOrganizationScope();
  const detailPath = withOrganizationScope(
    `/app/${itemId ?? ""}/subscription/plans/${encodeURIComponent(planId ?? "")}`,
    organizationScope,
  );

  const { data: plan, isLoading } = useSubscriptionPlan(planId, organizationScope);
  const { mutateAsync, isPending } = useCreateSubscriptionPrice();
  const [submissionError, setSubmissionError] = useState<string | null>(null);

  const form = useForm<CreateSubscriptionPriceFormValues>({
    resolver: zodResolver(createSubscriptionPriceSchema),
    mode: "onBlur",
    defaultValues: defaultSubscriptionPriceFormValues,
  });

  const submit = async (values: CreateSubscriptionPriceFormValues) => {
    if (!planId) {
      return;
    }

    setSubmissionError(null);

    const request: CreateSubscriptionPriceRequest = {
      planId,
      // The plan may belong to an organization the console is not itself in, and the server
      // resolves this request on its own — without naming it, the plan reads as missing.
      organizationId: organizationScope,
      currencyCode: values.currencyCode,
      unitAmountMinor: toMinorUnits(values.amount, values.currencyCode),
      interval: values.interval,
      intervalCount: values.intervalCount,
      quantityItemKey:
        values.quantityItemKey === FLAT_FEE ? undefined : values.quantityItemKey,
    };

    try {
      await mutateAsync(request);

      toast({
        variant: "success",
        title: "Price added",
        description: "Subscribers can now check out on this price.",
      });

      navigate(detailPath);
    } catch (error) {
      setSubmissionError(
        error instanceof Error ? error.message : "The price could not be created.",
      );
    }
  };

  if (isLoading) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <Skeleton className="h-32 w-full rounded-2xl" />
        <Skeleton className="h-64 w-full rounded-xl" />
      </main>
    );
  }

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title="Add a price"
        description={plan ? `For ${plan.displayName}.` : "Add a price to this plan."}
        backTo={detailPath}
        backLabel="Back to plan"
        icon={<Plus className="h-6 w-6" />}
      />

      <Card className="max-w-2xl rounded-xl p-5 sm:p-6">
        <Form {...form}>
          <form className="space-y-5" onSubmit={form.handleSubmit(submit)} noValidate>
            <div className="grid gap-5 sm:grid-cols-2">
              <FormField
                control={form.control}
                name="currencyCode"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Currency</FormLabel>
                    <Select value={field.value} onValueChange={field.onChange}>
                      <FormControl>
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        {SUBSCRIPTION_CURRENCY_OPTIONS.map((currency) => (
                          <SelectItem key={currency.code} value={currency.code}>
                            {currency.code} — {currency.name}
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
                name="amount"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Amount</FormLabel>
                    <FormControl>
                      <Input {...field} type="number" min={0} step="0.01" placeholder="89.00" />
                    </FormControl>
                    <FormDescription>In major units — 89.00, not 8900.</FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                control={form.control}
                name="interval"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Billing interval</FormLabel>
                    <Select
                      value={String(field.value)}
                      onValueChange={(value) => field.onChange(Number(value))}
                    >
                      <FormControl>
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        {BILLING_INTERVAL_OPTIONS.map((option) => (
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

              <FormField
                control={form.control}
                name="intervalCount"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Every</FormLabel>
                    <FormControl>
                      <Input {...field} type="number" min={1} max={36} />
                    </FormControl>
                    <FormDescription>
                      Paired with the interval — 3 with &ldquo;month&rdquo; is quarterly.
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                control={form.control}
                name="quantityItemKey"
                render={({ field }) => (
                  <FormItem className="sm:col-span-2">
                    <FormLabel>What this multiplies</FormLabel>
                    <Select value={field.value} onValueChange={field.onChange}>
                      <FormControl>
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        <SelectItem value={FLAT_FEE}>Flat fee</SelectItem>
                        {(plan?.quantityItems ?? []).map((item) => (
                          <SelectItem key={item.itemKey} value={item.itemKey}>
                            Per {item.unitLabel} ({item.itemKey})
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <FormDescription>
                      Charge once per period, or multiply by a quantity item this plan defines.
                    </FormDescription>
                    <FormMessage />
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

            <div className="flex flex-col-reverse gap-3 border-t pt-5 sm:flex-row sm:justify-end">
              <Button
                type="button"
                variant="outline"
                onClick={() => navigate(detailPath)}
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
                {isPending ? "Adding price…" : "Add price"}
              </Button>
            </div>
          </form>
        </Form>
      </Card>
    </main>
  );
};
