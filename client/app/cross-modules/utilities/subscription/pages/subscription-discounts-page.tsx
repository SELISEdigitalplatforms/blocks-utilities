import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams } from "react-router";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { CampaignBuilder } from "../components/campaign-builder/campaign-builder";
import type { CampaignDraft } from "../components/campaign-builder/campaign-draft";
import { toCreateDiscountRequest } from "../components/campaign-builder/campaign-draft";
import { useOrganizationScope } from "../hooks/use-organization-scope";
import { useSubscriptionPlans } from "../hooks/use-subscription-plans";
import type { SubscriptionDiscount, SubscriptionPlan } from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";
import { formatMoney, formatPrice } from "../utilities/subscription-format";

/**
 * What a discount applies to, in one line.
 *
 * Names the cadence rather than the identifier: "pro · CHF 145.00 / year" is what somebody authored,
 * and a price id tells a reader nothing about whether the restriction is the one they meant. A price
 * that has since been retired is still shown by id, because the restriction is still in force.
 */
const describeApplicability = (
  planCodes: string[],
  priceIds: string[] | undefined,
  plans: SubscriptionPlan[],
): string => {
  const prices = (priceIds ?? []).map((priceId) => {
    const match = plans
      .flatMap((plan) => plan.prices.map((price) => ({ plan, price })))
      .find((candidate) => candidate.price.priceId === priceId);

    return match ? `${match.plan.code} · ${formatPrice(match.price)}` : priceId;
  });

  if (planCodes.length === 0 && prices.length === 0) {
    return "Applies to any plan and any price.";
  }

  const parts = [
    planCodes.length > 0 ? `plans: ${planCodes.join(", ")}` : null,
    prices.length > 0 ? `prices: ${prices.join(", ")}` : null,
  ].filter(Boolean);

  // "and" rather than "or": both restrictions have to match, which is the part an author is most
  // likely to get wrong when they set both.
  return `Restricted to ${parts.join(" and ")}.`;
};

/** A campaign's own window and rules, in one line — absent for a Standard discount. */
const describeCampaign = (discount: SubscriptionDiscount): string | null => {
  if (discount.campaignKind === "Standard") return null;

  const kindLabel =
    discount.campaignKind === "FreeOpeningCalendarPeriod" ? "Free opening month" : "First-year discount";
  const window =
    discount.validFromDate && discount.validThroughDate
      ? `${discount.validFromDate} – ${discount.validThroughDate}`
      : null;

  return [kindLabel, window].filter(Boolean).join(" · ");
};

const EFFECTIVE_STATE_VARIANT: Record<SubscriptionDiscount["effectiveState"], "default" | "secondary" | "outline"> = {
  Active: "default",
  Upcoming: "secondary",
  Expired: "outline",
  Archived: "outline",
};

export const SubscriptionDiscountsPage = () => {
  const { itemId } = useParams();
  const organizationId = useOrganizationScope();
  const queryClient = useQueryClient();
  const [isCreating, setIsCreating] = useState(false);
  const [submissionError, setSubmissionError] = useState<string | null>(null);

  const catalogue = useSubscriptionPlans(organizationId);
  const availablePlans = catalogue.data ?? [];

  const discounts = useQuery({
    queryKey: ["subscription-discounts", organizationId],
    queryFn: () => subscriptionService.listDiscounts(organizationId),
  });

  const create = useMutation({
    mutationFn: (draft: CampaignDraft) =>
      subscriptionService.createDiscount(toCreateDiscountRequest(draft, organizationId)),
    onSuccess: async () => {
      setIsCreating(false);
      setSubmissionError(null);
      await queryClient.invalidateQueries({ queryKey: ["subscription-discounts"] });
    },
  });

  const archive = useMutation({
    mutationFn: (discountId: string) => subscriptionService.archiveDiscount(discountId, organizationId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["subscription-discounts"] }),
  });

  const submitDraft = async (draft: CampaignDraft) => {
    setSubmissionError(null);
    try {
      await create.mutateAsync(draft);
    } catch (error) {
      setSubmissionError(error instanceof Error ? error.message : "The discount could not be created.");
      throw error;
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title="Subscription discounts"
        description="Reusable discount codes and time-boxed campaigns are validated at signup and copied onto each subscription."
        backTo={`/app/${itemId ?? ""}/subscription/plans`}
      />

      {isCreating ? (
        <CampaignBuilder
          plans={availablePlans}
          organizationId={organizationId}
          isSubmitting={create.isPending}
          submissionError={submissionError}
          onSubmit={submitDraft}
          onCancel={() => {
            setIsCreating(false);
            setSubmissionError(null);
          }}
        />
      ) : (
        <Card className="flex items-center justify-between gap-4 rounded-xl p-5">
          <div>
            <h2 className="font-semibold">Create a discount</h2>
            <p className="text-sm text-muted-foreground">
              An ordinary code, a free opening month, or a first-year offer — four short steps.
            </p>
          </div>
          <Button onClick={() => setIsCreating(true)}>New discount</Button>
        </Card>
      )}

      <Card className="rounded-xl p-0">
        <div className="border-b p-4 font-semibold">Discount catalogue</div>
        <div className="divide-y">
          {(discounts.data ?? []).map((discount) => {
            const campaignSummary = describeCampaign(discount);

            return (
              <div key={discount.discountId} className="flex items-center justify-between gap-4 p-4">
                <div>
                  <p className="flex items-center gap-2 font-medium">
                    {discount.displayName} <code className="text-xs">{discount.code}</code>
                    <Badge variant={EFFECTIVE_STATE_VARIANT[discount.effectiveState]}>
                      {discount.effectiveState}
                    </Badge>
                    {campaignSummary && <Badge variant="secondary">{campaignSummary}</Badge>}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {discount.percentBasisPoints
                      ? `${discount.percentBasisPoints / 100}% off`
                      : `${formatMoney(discount.amountMinor ?? 0, discount.currencyCode ?? "USD")} off`}{" "}
                    · {discount.status}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {describeApplicability(discount.applicablePlanCodes, discount.applicablePriceIds, availablePlans)}
                  </p>
                  {discount.entitlementOverrideKey && (
                    <p className="text-xs text-muted-foreground">
                      Temporarily caps {discount.entitlementOverrideKey} at {discount.entitlementOverrideLimit}.
                    </p>
                  )}
                </div>
                {discount.status === "Active" && (
                  <Button variant="ghost" size="sm" onClick={() => archive.mutate(discount.discountId)}>
                    Retire
                  </Button>
                )}
              </div>
            );
          })}
          {!discounts.isLoading && !(discounts.data?.length) && (
            <p className="p-4 text-sm text-muted-foreground">No discounts authored yet.</p>
          )}
        </div>
      </Card>
    </main>
  );
};
