import { useMemo } from "react";
import { useLocation, useNavigate, useParams } from "react-router";
import { toast } from "@/hooks/use-toast";
import { PlanBuilder } from "../components/plan-builder/plan-builder";
import { useCreateSubscriptionPlan } from "../hooks/use-create-subscription-plan";
import { useCreateSubscriptionPrice } from "../hooks/use-create-subscription-price";
import { withOrganizationScope } from "../hooks/use-organization-scope";
import { defaultSubscriptionPlanFormValues } from "../schemas/subscription-plan.schema";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { planToFormValues, toCreatePlanRequest } from "../utilities/plan-form-mapping";
import { submitPlanWithPrices } from "../utilities/submit-plan-with-prices";

export const CreateSubscriptionPlanPage = () => {
  const { itemId } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const { mutateAsync: createPlan, isPending } = useCreateSubscriptionPlan();
  const { mutateAsync: createPrice, isPending: isPricing } = useCreateSubscriptionPrice();
  const listPath = `/app/${itemId ?? ""}/subscription/plans`;
  const duplicatePlan = (location.state as { duplicatePlan?: SubscriptionPlan } | null)?.duplicatePlan;
  const initialValues = useMemo(
    () =>
      duplicatePlan
        // Prices come along, blank code aside: a duplicate exists to be edited into a sibling of
        // the plan it came from, and re-entering four prices and their tax by hand is where that
        // stops being quicker than starting over.
        ? { ...planToFormValues(duplicatePlan, { includePrices: true }), code: "" }
        : defaultSubscriptionPlanFormValues,
    [duplicatePlan],
  );

  return (
    <PlanBuilder
      mode="create"
      defaultValues={initialValues}
      title={duplicatePlan ? `Duplicate ${duplicatePlan.displayName}` : "Create subscription plan"}
      description="A guided walkthrough — nothing is created until you review and confirm at the end."
      backTo={listPath}
      submitLabel="Create plan"
      submittingLabel="Creating plan…"
      // One submission spans both calls, so the buttons stay locked through the price loop too.
      isSubmitting={isPending || isPricing}
      onSubmit={async (values) => {
        const { plan, failures } = await submitPlanWithPrices({
          planRequest: toCreatePlanRequest(values),
          prices: values.prices,
          createPlan,
          createPrice,
        });

        if (failures.length > 0) {
          toast({
            variant: "destructive",
            title: `${plan.displayName} was created, but ${failures.length} price${failures.length === 1 ? "" : "s"} was not`,
            description: `${failures.join(" ")} Add the missing prices from the plan page.`,
          });
        } else {
          toast({
            variant: "success",
            title: "Plan created",
            description: `${plan.displayName} is ready to subscribe to.`,
          });
        }

        // Carries the organization the plan was just scoped to. Without it the detail page
        // resolves as the console organization, and a plan belonging to someone else reads as
        // missing — the plan is created and then immediately unreachable.
        navigate(
          withOrganizationScope(
            `${listPath}/${encodeURIComponent(plan.planId)}`,
            plan.organizationId,
          ),
        );
      }}
    />
  );
};
