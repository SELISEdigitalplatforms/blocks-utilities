import { AlertCircle } from "lucide-react";
import { useT } from "@seliseblocks/genesis-os";
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
import { useUpdateSubscriptionPriceDiscount } from "../hooks/use-update-subscription-price-discount";
import { useUpdateSubscriptionPriceTax } from "../hooks/use-update-subscription-price-tax";
import { toBasisPoints, type TaxMode } from "../utilities/subscription-tax";
import type { AutomaticDiscountCombination } from "../utilities/subscription-discount";
import { planToFormValues, toUpdatePlanRequest } from "../utilities/plan-form-mapping";
import { submitPlanWithPrices } from "../utilities/submit-plan-with-prices";

export const EditSubscriptionPlanPage = () => {
  const { t } = useT("utilities.subscription.pages");
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
  const { mutateAsync: updatePriceDiscount } = useUpdateSubscriptionPriceDiscount();
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
          title={t("EDIT_PAGE.TITLE_NOT_LOADED")}
          description={t("EDIT_PAGE.NOT_LOADED")}
          backTo={withOrganizationScope(basePath, organizationScope)}
        />
        <Card className="flex min-h-56 flex-col items-center justify-center text-center">
          <span className="rounded-full bg-destructive/10 p-4 text-destructive">
            <AlertCircle className="h-7 w-7" />
          </span>
          <h3 className="mt-4 text-lg font-semibold">{t("EDIT_PAGE.NOT_FOUND")}</h3>
          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            {error instanceof Error
              ? error.message
              : t("EDIT_PAGE.NOT_FOUND_HINT")}
          </p>
        </Card>
      </main>
    );
  }

  // Hoisted out of the JSX to keep the builder's props readable.
  const retirePrice = async (priceId: string) => {
    setRetiringPriceId(priceId);

    try {
      await archivePrice({ priceId, organizationId: plan.organizationId ?? undefined });
      toast({
        title: t("EDIT_PAGE.PRICE_RETIRED"),
        description: t("EDIT_PAGE.PRICE_RETIRED_DESCRIPTION"),
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: t("EDIT_PAGE.RETIRE_FAILED"),
        description: error instanceof Error ? error.message : t("EDIT_PAGE.TRY_AGAIN"),
      });
    } finally {
      setRetiringPriceId(null);
    }
  };

  const updatePriceTaxRate = async (
    priceId: string,
    taxPercent?: number,
    taxMode?: TaxMode,
  ) => {
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
      title: t("EDIT_PAGE.TAX_SAVED"),
      description: t("EDIT_PAGE.TAX_SAVED_DESCRIPTION"),
    });
  };

  const updateAutomaticDiscount = async (
    priceId: string,
    discountPercent?: number,
    combination?: AutomaticDiscountCombination,
  ) => {
    await updatePriceDiscount({
      priceId,
      request: {
        organizationId: plan.organizationId ?? undefined,
        // Zero clears it, which is why an absent percentage is sent as zero rather than omitted:
        // omitting the field would leave the stored discount in place.
        automaticDiscountBasisPoints: discountPercent ? toBasisPoints(discountPercent) : 0,
        quantityDiscountCombination: discountPercent ? combination : undefined,
      },
    });
    toast({
      variant: "success",
      title: discountPercent ? t("EDIT_PAGE.DISCOUNT_SAVED") : t("EDIT_PAGE.DISCOUNT_CLEARED"),
      description: t("EDIT_PAGE.DISCOUNT_SAVED_DESCRIPTION"),
    });
  };

  /**
   * An archived plan is closed to everything this page does. Every submission the builder offered
   * would be refused by the server.
   *
   * Reached by typing or bookmarking the URL, since no link points here for an archived plan any
   * more. Answered with an explanation and a way back rather than a redirect, so somebody who
   * followed an old link is told why instead of being bounced somewhere they did not ask for.
   */
  if (plan.status === "Archived") {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <SubscriptionPlanPageHeader
          title={plan.displayName}
          description={t("EDIT_PAGE.ARCHIVED")}
          backTo={detailPath}
        />
        <Card className="space-y-2 rounded-xl border-dashed bg-muted/40">
          <h2 className="font-semibold">{t("EDIT_PAGE.ARCHIVED_TITLE")}</h2>
          <p className="text-sm text-muted-foreground">
            {t("EDIT_PAGE.ARCHIVED_EXPLANATION")}
          </p>
        </Card>
      </main>
    );
  }

  /**
   * A subscribed plan is edited like any other: each save is the plan's next version. Existing
   * subscribers bill from the snapshot they took at signup, so the edit reaches only whoever
   * subscribes next — the author needs to be told that before saving, not after.
   */
  // No version number: meter-rate changes and archiving also move it on, so "creates v3" could be
  // wrong by the time the save lands.
  const editDescription = plan.hasSubscribers
    ? t("EDIT_PAGE.DESCRIPTION_SUBSCRIBED")
    : t("EDIT_PAGE.DESCRIPTION");

  return (
    <PlanBuilder
      mode="edit"
      defaultValues={defaultValues}
      title={t("EDIT_PAGE.TITLE", { name: plan.displayName })}
      description={editDescription}
      backTo={detailPath}
      submitLabel={t("common:SAVE_CHANGES")}
      submittingLabel={t("EDIT_PAGE.SUBMITTING")}
      isSubmitting={isPending || isPricing}
      existingPrices={plan.prices}
      retiringPriceId={retiringPriceId}
      onRetirePrice={retirePrice}
      onUpdatePriceTax={updatePriceTaxRate}
      onUpdatePriceDiscount={updateAutomaticDiscount}
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
            title: t("EDIT_PAGE.PARTIAL_TITLE", { count: failures.length }),
            description: t("EDIT_PAGE.PARTIAL_DESCRIPTION", { failures: failures.join(" ") }),
          });
        } else {
          toast({
            variant: "success",
            title: t("EDIT_PAGE.SAVED"),
            description: t("EDIT_PAGE.SAVED_DESCRIPTION", { name: values.displayName }),
          });
        }

        navigate(detailPath);
      }}
    />
  );
};
