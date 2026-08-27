import { zodResolver } from "@hookform/resolvers/zod";
import { Info, Loader2, Plus } from "lucide-react";
import { useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Form } from "@/components/ui-kits/form/form";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import {
  buildSubscriptionPlanSchema,
  type CreateSubscriptionPlanFormValues,
} from "../schemas/subscription-plan.schema";
import type { AutomaticDiscountCombination } from "../utilities/subscription-discount";
import type { TaxMode } from "../utilities/subscription-tax";
import { planToFormValues } from "../utilities/plan-form-mapping";
import { PlanPriceFields } from "./plan-builder/plan-price-fields";
import { SubscriptionPlanPageHeader } from "./subscription-plan-page-header";

export interface PlanPricesEditorProps {
  plan: SubscriptionPlan;
  backTo: string;
  isSubmitting: boolean;
  retiringPriceId?: string | null;
  onRetirePrice: (priceId: string) => void;
  onUpdatePriceTax: (priceId: string, taxPercent?: number, taxMode?: TaxMode) => Promise<void>;
  onUpdatePriceDiscount: (
    priceId: string,
    discountPercent?: number,
    combination?: AutomaticDiscountCombination,
  ) => Promise<void>;
  /** Adds the authored prices. Resolves with one line per price that did not land. */
  onSubmit: (values: CreateSubscriptionPlanFormValues) => Promise<void>;
}

/**
 * Prices for a plan whose own terms can no longer change.
 *
 * The server refuses to update a plan the moment anybody subscribes to it, because subscribing
 * copies the terms onto the subscription and the bills already raised were worked out from that
 * copy. Adding a price is deliberately not refused: a price is a new thing to sell, not a change
 * to something already sold, and repricing is add-the-new-one-then-retire-the-old-one. So this
 * exists for exactly the plans the builder turns away — which are the plans most likely to need a
 * new price, since only a plan somebody is on can need repricing.
 *
 * The fields come from the builder rather than being written again here. The standalone page this
 * replaced had its own copy and had drifted: no billing alignment, no tax, no automatic discount,
 * so a price added there could not say everything a price can say.
 */
export const PlanPricesEditor = ({
  plan,
  backTo,
  isSubmitting,
  retiringPriceId = null,
  onRetirePrice,
  onUpdatePriceTax,
  onUpdatePriceDiscount,
  onSubmit,
}: PlanPricesEditorProps) => {
  const [submissionError, setSubmissionError] = useState<string | null>(null);

  // The whole plan, so the price fields can read the quantity items a price may multiply — and so
  // the plan's own values are the saved ones rather than blanks that would fail validation. Only
  // the prices are ever sent.
  const defaultValues = useMemo(() => planToFormValues(plan), [plan]);

  // The builder's schema, which carries the two cross-price rules worth having here: a price
  // cannot multiply a quantity item the plan does not define, and two prices cannot charge on
  // identical terms. Requiring one price is right for a form whose only purpose is adding one.
  const schema = useMemo(() => buildSubscriptionPlanSchema({ requirePrice: true }), []);

  const form = useForm<CreateSubscriptionPlanFormValues>({
    resolver: zodResolver(schema),
    mode: "onBlur",
    defaultValues,
  });

  const submit = async (values: CreateSubscriptionPlanFormValues) => {
    setSubmissionError(null);

    try {
      await onSubmit(values);
    } catch (error) {
      setSubmissionError(
        error instanceof Error ? error.message : "The price could not be added.",
      );
    }
  };

  /**
   * The safety net for a rejection with nowhere to show itself.
   *
   * The resolver checks the whole plan, because the price rules it carries are worth having — a
   * price cannot multiply a quantity item the plan does not define, and two prices cannot charge
   * identical terms. The cost is that a saved plan whose own values no longer satisfy the current
   * schema would fail validation on a field this page does not render, and the form would refuse to
   * submit while saying nothing. Rather than leave that as a dead button, it is named.
   */
  const reportUnrenderableRejection = (errors: Record<string, unknown>) => {
    const offending = Object.keys(errors).filter((field) => field !== "prices");

    if (offending.length === 0) {
      // Every complaint is about a price, and each one is already shown against its own field.
      return;
    }

    setSubmissionError(
      `This plan's saved terms are no longer valid in this form (${offending.join(", ")}), so a ` +
        "price cannot be added until that is corrected. Nothing was sent.",
    );
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title={`Prices for ${plan.displayName}`}
        description="Add a price, retire one, or adjust tax and automatic discount."
        backTo={backTo}
        backLabel="Back to plan"
        icon={<Plus className="h-6 w-6" />}
      />

      {/* Said once, plainly, instead of leaving somebody to discover it by filling in a form the
          server will refuse. The builder's own dead end used to be all this page said. */}
      <Card className="flex items-start gap-3 rounded-xl border-warning-200 bg-warning-50/60 p-4">
        <Info className="mt-0.5 h-5 w-5 shrink-0 text-warning-700" />
        <div className="text-sm">
          <p className="font-medium text-warning-800">
            This plan&apos;s own terms are settled
          </p>
          <p className="mt-1 text-muted-foreground">
            Somebody has subscribed, and subscribing copies the plan&apos;s terms onto the
            subscription. Its name, entitlements and trial can no longer change — but what it sells
            on still can. To reprice, add the new price and retire the old one: everybody already on
            the old one keeps the terms they bought.
          </p>
        </div>
      </Card>

      <Card className="max-w-3xl rounded-xl p-5 sm:p-6">
        <Form {...form}>
          <form
            className="space-y-5"
            onSubmit={form.handleSubmit(submit, reportUnrenderableRejection)}
            noValidate
          >
            <PlanPriceFields
              isEditing
              existingPrices={plan.prices}
              retiringPriceId={retiringPriceId}
              onRetirePrice={onRetirePrice}
              onUpdatePriceTax={onUpdatePriceTax}
              onUpdatePriceDiscount={onUpdatePriceDiscount}
            />

            {submissionError && (
              <div
                role="alert"
                className="rounded-lg border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive"
              >
                {submissionError}
              </div>
            )}

            <div className="flex flex-col-reverse gap-3 border-t pt-5 sm:flex-row sm:justify-end">
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <Plus className="mr-2 h-4 w-4" />
                )}
                {isSubmitting ? "Adding…" : "Add prices"}
              </Button>
            </div>
          </form>
        </Form>
      </Card>
    </main>
  );
};
