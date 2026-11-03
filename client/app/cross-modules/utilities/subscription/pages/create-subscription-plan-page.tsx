import { useT } from "@seliseblocks/genesis-os";
import { useMemo, useState } from "react";
import { useLocation, useNavigate, useParams } from "react-router";
import { toast } from "@/hooks/use-toast";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { PlanBuilder } from "../components/plan-builder/plan-builder";
import { useCreateSubscriptionPlan } from "../hooks/use-create-subscription-plan";
import { useCreateSubscriptionPrice } from "../hooks/use-create-subscription-price";
import { withOrganizationScope } from "../hooks/use-organization-scope";
import { defaultSubscriptionPlanFormValues } from "../schemas/subscription-plan.schema";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { planToFormValues, toCreatePlanRequest } from "../utilities/plan-form-mapping";
import { submitPlanWithPrices } from "../utilities/submit-plan-with-prices";

export const CreateSubscriptionPlanPage = () => {
  const { t } = useT("utilities.subscription.pages");
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
  // Checked by default, but not assumed: a duplicate can just as easily be a fresh plan started
  // from a convenient template, so whether it replaces the one it came from is a choice, not an
  // inference from clicking "Duplicate plan".
  const [marksAsSuccessor, setMarksAsSuccessor] = useState(true);

  return (
    <>
      {duplicatePlan && (
        <label className="flex items-center gap-2 px-4 pt-4 text-sm text-muted-foreground sm:px-6 lg:px-8">
          <Checkbox
            checked={marksAsSuccessor}
            onCheckedChange={(checked) => setMarksAsSuccessor(checked === true)}
          />
          {t("CREATE_PAGE.REPLACES_PLAN", { name: duplicatePlan.displayName })}
        </label>
      )}
      <PlanBuilder
        mode="create"
        defaultValues={initialValues}
        title={
          duplicatePlan
            ? t("CREATE_PAGE.TITLE_DUPLICATE", { name: duplicatePlan.displayName })
            : t("CREATE_PAGE.TITLE")
        }
        description={t("CREATE_PAGE.DESCRIPTION")}
        backTo={listPath}
        submitLabel={t("CREATE_PAGE.SUBMIT")}
        submittingLabel={t("CREATE_PAGE.SUBMITTING")}
        // One submission spans both calls, so the buttons stay locked through the price loop too.
        isSubmitting={isPending || isPricing}
        onSubmit={async (values) => {
          const planRequest = toCreatePlanRequest(values);

          if (duplicatePlan && marksAsSuccessor) {
            planRequest.predecessorPlanId = duplicatePlan.planId;
          }

          const { plan, failures } = await submitPlanWithPrices({
            planRequest,
            prices: values.prices,
            createPlan,
            createPrice,
          });

          if (failures.length > 0) {
            toast({
              variant: "destructive",
              title: t("CREATE_PAGE.PARTIAL_TITLE", {
                name: plan.displayName,
                count: failures.length,
              }),
              description: t("CREATE_PAGE.PARTIAL_DESCRIPTION", {
                failures: failures.join(" "),
              }),
            });
          } else {
            toast({
              variant: "success",
              title: t("CREATE_PAGE.CREATED_TITLE"),
              description: t("CREATE_PAGE.CREATED_DESCRIPTION", { name: plan.displayName }),
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
    </>
  );
};
