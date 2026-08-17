import { zodResolver } from "@hookform/resolvers/zod";
import { ArrowLeft, ArrowRight, Check, ChevronDown, Loader2 } from "lucide-react";
import { useMemo, useState } from "react";
import { FormProvider, useForm, useWatch } from "react-hook-form";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { toast } from "@/hooks/use-toast";
import StepperProviderComponent, { useStepper } from "@/components/stepper/stepper-provider";
import type { Steps } from "@/components/stepper/stepper-models";
import {
  ORGANIZATION_PAGE_SIZE,
  TENANT_WIDE_ORGANIZATION,
} from "../../constants/subscription.constants";
import type { PlanPrice } from "../../models/subscription-plan.model";
import {
  BILLING_INTERVAL_NAMES,
  ENTITLEMENT_LIMIT_KIND_NAMES,
} from "../../models/subscription-plan.model";
import {
  buildSubscriptionPlanSchema,
  type CreateSubscriptionPlanFormValues,
} from "../../schemas/subscription-plan.schema";
import { FLAT_FEE } from "../../schemas/subscription-price.schema";
import { toMinorUnits } from "../../utilities/subscription-format";
import { PlanSummaryCard, type PlanSummaryData } from "../plan-summary-card";
import { SubscriptionPlanPageHeader } from "../subscription-plan-page-header";
import { PlanBuilderProgress } from "./plan-builder-progress";
import { StepIdentity } from "./step-identity";
import { StepPricingModel } from "./step-pricing-model";
import { StepReview } from "./step-review";
import { StepTrial } from "./step-trial";
import { StepUsageLimits } from "./step-usage-limits";

const STEPS: Steps = [
  { id: 1, title: "Identity" },
  { id: 2, title: "Pricing model" },
  { id: 3, title: "Usage limits" },
  { id: 4, title: "Trial" },
  { id: 5, title: "Review" },
];

export interface PlanBuilderProps {
  /**
   * Editing differs in three ways only: the code and organization are fixed, prices already exist
   * so a new one is optional, and the wording. Everything else is the same walkthrough, which is
   * why it is one component — two copies would drift the moment a step changed.
   */
  mode: "create" | "edit";
  defaultValues: CreateSubscriptionPlanFormValues;
  title: string;
  description: string;
  backTo: string;
  submitLabel: string;
  submittingLabel: string;
  isSubmitting: boolean;
  /**
   * The prices the plan already has, shown read-only while editing. Empty when creating, and
   * empty is also the honest answer for a plan that has none yet.
   */
  existingPrices?: PlanPrice[];
  /** Rejecting leaves the draft alone and shows the reason; the caller navigates on success. */
  onSubmit: (values: CreateSubscriptionPlanFormValues) => Promise<void>;
}

export const PlanBuilder = (props: PlanBuilderProps) => (
  <StepperProviderComponent steps={STEPS}>
    <PlanBuilderWizard {...props} />
  </StepperProviderComponent>
);

const PlanBuilderWizard = ({
  mode,
  defaultValues,
  title,
  description,
  backTo,
  submitLabel,
  submittingLabel,
  isSubmitting,
  existingPrices = [],
  onSubmit,
}: PlanBuilderProps) => {
  const { currentStep, nextStep, previousStep, totalSteps } = useStepper();
  const [submissionError, setSubmissionError] = useState<string | null>(null);
  const isEditing = mode === "edit";

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });

  const schema = useMemo(
    () => buildSubscriptionPlanSchema({ requirePrice: !isEditing }),
    [isEditing],
  );

  const form = useForm<CreateSubscriptionPlanFormValues>({
    resolver: zodResolver(schema),
    mode: "onBlur",
    defaultValues,
  });

  const draft = useWatch({ control: form.control });

  const organizationLabel =
    !draft.organizationId || draft.organizationId === TENANT_WIDE_ORGANIZATION
      ? "Tenant-wide"
      : (organizationsData?.organizations.find(
          (organization) => organization.itemId === draft.organizationId,
        )?.name ?? draft.organizationId);

  const summary: PlanSummaryData = {
    displayName: draft.displayName ?? "",
    code: draft.code ?? "",
    organizationLabel,
    trialDays: draft.trialDays ?? null,
    trialRequiresPaymentMethod: draft.trialRequiresPaymentMethod ?? true,
    quantityItems: (draft.quantityItems ?? []).map((item) => ({
      itemKey: item?.itemKey ?? "",
      unitLabel: item?.unitLabel ?? "",
      defaultQuantity: item?.defaultQuantity ?? 0,
      maxQuantity: item?.maxQuantity ?? null,
    })),
    meters: (draft.meters ?? []).map((meter) => ({
      meterKey: meter?.meterKey ?? "",
      displayName: meter?.displayName ?? "",
      unitLabel: meter?.unitLabel ?? "",
      includedQuantity: meter?.includedQuantity ?? 0,
      overageAllowed: meter?.overageAllowed ?? false,
      // Only the currency matters to the summary — its presence is what decides whether
      // overage reads as billed or given away.
      rateTables: (meter?.rateTables ?? [])
        .map((table) => table?.currencyCode)
        .filter((currencyCode): currencyCode is string => Boolean(currencyCode))
        .map((currencyCode) => ({ currencyCode })),
    })),
    entitlements: (draft.entitlements ?? []).map((entitlement) => ({
      key: entitlement?.key ?? "",
      limitKind: ENTITLEMENT_LIMIT_KIND_NAMES[entitlement?.limitKind ?? 0],
      limit: entitlement?.limit ?? null,
      unitLabel: entitlement?.unitLabel ?? null,
      meterKey: entitlement?.meterKey ?? null,
    })),
    trialGrants: (draft.trialGrants ?? []).map((grant) => ({
      meterKey: grant?.meterKey ?? "",
      includedQuantity: grant?.includedQuantity ?? 0,
    })),
    prices: (draft.prices ?? []).map((price) => ({
      currencyCode: price?.currencyCode ?? "USD",
      unitAmountMinor: toMinorUnits(price?.amount ?? 0, price?.currencyCode ?? "USD"),
      interval: BILLING_INTERVAL_NAMES[price?.interval ?? 2],
      intervalCount: price?.intervalCount ?? 1,
      quantityItemKey:
        !price?.quantityItemKey || price.quantityItemKey === FLAT_FEE
          ? null
          : price.quantityItemKey,
    })),
  };

  const isLastStep = currentStep === totalSteps;

  const submit = async () => {
    setSubmissionError(null);

    if (!(await form.trigger())) {
      toast({
        variant: "destructive",
        title: "Check the highlighted fields",
        description: "Some steps still have something to fix before this can be saved.",
      });
      return;
    }

    try {
      await onSubmit(form.getValues());
    } catch (error) {
      setSubmissionError(error instanceof Error ? error.message : "This plan could not be saved.");
    }
  };

  return (
    <main className="relative min-w-0 overflow-hidden bg-gradient-to-b from-blocks-primary-shades-50/60 via-background to-background px-4 py-5 sm:px-6 sm:py-7 lg:px-8 lg:py-9">
      <div className="pointer-events-none absolute -right-32 top-24 h-80 w-80 rounded-full bg-blocks-secondary-100/30 blur-3xl" />
      <div className="pointer-events-none absolute -left-32 top-96 h-72 w-72 rounded-full bg-blocks-primary-100/30 blur-3xl" />

      <div className="relative mx-auto max-w-[96rem] space-y-5">
        <SubscriptionPlanPageHeader title={title} description={description} backTo={backTo} />

        <FormProvider {...form}>
          <form
            onSubmit={(event) => {
              event.preventDefault();
            }}
          >
            <div className="space-y-5">
              <PlanBuilderProgress />

              <div className="grid min-w-0 gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
                <Card className="min-w-0 rounded-2xl border-border/70 bg-card/95 p-5 shadow-[0_18px_50px_-32px_rgba(15,23,42,0.35)] sm:p-7 [&_h2]:text-xl [&_h2]:font-semibold [&_h2]:tracking-tight [&_h3]:tracking-tight">
                  {currentStep === 1 && <StepIdentity isEditing={isEditing} />}
                  {currentStep === 2 && (
                    <StepPricingModel
                      isEditing={isEditing}
                      existingPrices={existingPrices}
                    />
                  )}
                  {currentStep === 3 && <StepUsageLimits />}
                  {currentStep === 4 && <StepTrial />}
                  {currentStep === 5 && <StepReview plan={summary} />}

                  {submissionError && (
                    <div
                      role="alert"
                      className="mt-5 rounded-xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm font-medium text-destructive"
                    >
                      {submissionError}
                    </div>
                  )}

                  {!isLastStep && (
                    <Collapsible className="mt-5 xl:hidden">
                      <CollapsibleTrigger className="flex items-center gap-1 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground">
                        <ChevronDown className="h-4 w-4" />
                        Preview this plan so far
                      </CollapsibleTrigger>
                      <CollapsibleContent className="pt-3">
                        <PlanSummaryCard plan={summary} />
                      </CollapsibleContent>
                    </Collapsible>
                  )}

                  <div className="mt-7 flex justify-between border-t border-border/70 pt-5">
                    <Button
                      type="button"
                      variant="outline"
                      onClick={previousStep}
                      disabled={currentStep === 1 || isSubmitting}
                      className="rounded-lg"
                    >
                      <ArrowLeft className="mr-2 h-4 w-4" />
                      Back
                    </Button>

                    {isLastStep ? (
                      <Button
                        type="button"
                        onClick={submit}
                        disabled={isSubmitting}
                        className="rounded-lg shadow-sm"
                      >
                        {isSubmitting ? (
                          <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                        ) : (
                          <Check className="mr-2 h-4 w-4" />
                        )}
                        {isSubmitting ? submittingLabel : submitLabel}
                      </Button>
                    ) : (
                      <Button type="button" onClick={nextStep} className="rounded-lg shadow-sm">
                        Next
                        <ArrowRight className="ml-2 h-4 w-4" />
                      </Button>
                    )}
                  </div>
                </Card>

                <aside className="hidden xl:block" aria-label="Plan preview">
                  <div className="sticky top-5">
                    <PlanSummaryCard plan={summary} />
                  </div>
                </aside>
              </div>
            </div>
          </form>
        </FormProvider>
      </div>
    </main>
  );
};
