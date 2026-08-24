import { AlertCircle } from "lucide-react";
import { useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Card } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { toast } from "@/hooks/use-toast";
import { PlanBuilder } from "../components/plan-builder/plan-builder";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useArchiveSubscriptionPrice } from "../hooks/use-archive-subscription-price";
import { useCreateSubscriptionPrice } from "../hooks/use-create-subscription-price";
import { useOrganizationScope, withOrganizationScope } from "../hooks/use-organization-scope";
import { useSubscriptionPlan } from "../hooks/use-subscription-plan";
import { useUpdateSubscriptionPlan } from "../hooks/use-update-subscription-plan";
import { useUpdateSubscriptionPriceTax } from "../hooks/use-update-subscription-price-tax";
import { toBasisPoints } from "../utilities/subscription-tax";
import { planToFormValues, toUpdatePlanRequest } from "../utilities/plan-form-mapping";
import { submitPlanWithPrices } from "../utilities/submit-plan-with-prices";

export const EditSubscriptionPlanPage = () => {
  const { itemId, planId } = useParams();
  const navigate = useNavigate();
  const organizationScope = useOrganizationScope();
  const basePath = `/app/${itemId ?? ""}/subscription/plans`;

  const { data: plan, error, isError, isLoading } = useSubscriptionPlan(
    planId,
    organizationScope,
  );
  const { mutateAsync: updatePlan, isPending } = useUpdateSubscriptionPlan();
  const { mutateAsync: createPrice, isPending: isPricing } = useCreateSubscriptionPrice();
  const { mutateAsync: archivePrice } = useArchiveSubscriptionPrice();
  const { mutateAsync: updatePriceTax } = useUpdateSubscriptionPriceTax();
  const [retiringPriceId, setRetiringPriceId] = useState<string | null>(null);

  const detailPath = withOrganizationScope(
    `${basePath}/${encodeURIComponent(planId ?? "")}`,
    plan?.organizationId ?? organizationScope,
  );

  // Computed once from the loaded plan: the builder seeds its form from this, and a new object on
  // every render would fight whatever is being typed.
  const defaultValues = useMemo(() => (plan ? planToFormValues(plan) : null), [plan]);

  if (isLoading) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <Skeleton className="h-32 w-full rounded-2xl" />
        <Skeleton className="h-96 w-full rounded-xl" />
      </main>
    );
  }

  if (isError || !plan || !defaultValues) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <SubscriptionPlanPageHeader
          title="Edit plan"
          description="This plan could not be loaded."
          backTo={withOrganizationScope(basePath, organizationScope)}
        />
        <Card className="flex min-h-56 flex-col items-center justify-center text-center">
          <span className="rounded-full bg-destructive/10 p-4 text-destructive">
            <AlertCircle className="h-7 w-7" />
          </span>
          <h3 className="mt-4 text-lg font-semibold">Plan not found</h3>
          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            {error instanceof Error
              ? error.message
              : "It may belong to another organization, or no longer exists."}
          </p>
        </Card>
      </main>
    );
  }

  // The server refuses this outright; saying so here means nobody fills in a form for nothing.
  if (plan.hasSubscribers) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <SubscriptionPlanPageHeader
          title={plan.displayName}
          description="This plan can no longer be edited."
          backTo={detailPath}
          backLabel="Back to plan"
        />
        <Card className="flex min-h-56 flex-col items-center justify-center gap-2 text-center">
          <span className="rounded-full bg-warning-100 p-4 text-warning-700">
            <AlertCircle className="h-7 w-7" />
          </span>
          <h3 className="mt-2 text-lg font-semibold">Somebody has subscribed to this plan</h3>
          <p className="max-w-lg text-sm text-muted-foreground">
            Subscribing copies the plan&apos;s terms onto the subscription, and the bills already
            raised were worked out from that copy. Changing the plan now could not reach anyone
            already on it, and the catalogue would stop matching what they are being charged.
            Create a new plan and move subscribers across instead.
          </p>
        </Card>
      </main>
    );
  }

  return (
    <PlanBuilder
      mode="edit"
      defaultValues={defaultValues}
      title={`Edit ${plan.displayName}`}
      description="Rewrites what this plan sells. Nothing is saved until you confirm at the end."
      backTo={detailPath}
      submitLabel="Save changes"
      submittingLabel="Saving…"
      isSubmitting={isPending || isPricing}
      existingPrices={plan.prices}
      retiringPriceId={retiringPriceId}
      onRetirePrice={async (priceId) => {
        setRetiringPriceId(priceId);

        try {
          await archivePrice({ priceId, organizationId: plan.organizationId ?? undefined });
          toast({
            title: "Price retired",
            description:
              "It is no longer offered. Anyone already on it keeps their terms and renewals.",
          });
        } catch (error) {
          toast({
            variant: "destructive",
            title: "The price could not be retired",
            description:
              error instanceof Error ? error.message : "Try again in a moment.",
          });
        } finally {
          setRetiringPriceId(null);
        }
      }}
      onUpdatePriceTax={async (priceId, taxPercent, taxMode) => {
        await updatePriceTax({
          priceId,
          request: {
            organizationId: plan.organizationId ?? undefined,
            taxRateBasisPoints: taxPercent ? toBasisPoints(taxPercent) : undefined,
            taxMode: taxPercent ? taxMode : undefined,
          },
        });
        toast({
          variant: "success",
          title: "Price tax saved",
          description: "New subscriptions will use this tax configuration; existing subscriptions keep their snapshot.",
        });
      }}
      onSubmit={async (values) => {
        const { failures } = await submitPlanWithPrices({
          planRequest: toUpdatePlanRequest(values, plan.organizationId),
          prices: values.prices,
          // The same two-step: the plan is saved first, and prices added after it. A price that
          // fails must not read as a failed save, because the edit has already landed.
          createPlan: (request) => updatePlan({ planId: plan.planId, request }),
          createPrice,
        });

        if (failures.length > 0) {
          toast({
            variant: "destructive",
            title: `Changes saved, but ${failures.length} price${failures.length === 1 ? "" : "s"} was not added`,
            description: `${failures.join(" ")} Add them from the plan page.`,
          });
        } else {
          toast({
            variant: "success",
            title: "Changes saved",
            description: `${values.displayName} is up to date.`,
          });
        }

        navigate(detailPath);
      }}
    />
  );
};
