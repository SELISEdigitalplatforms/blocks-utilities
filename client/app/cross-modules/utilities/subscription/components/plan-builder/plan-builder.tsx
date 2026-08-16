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
import StepHorizontalTrackBar from "@/components/stepper/horizontal-track-bar";
import StepVerticalTrackBar from "@/components/stepper/vertical-track-bar";
import StepperProviderComponent, { useStepper } from "@/components/stepper/stepper-provider";
import type { Steps } from "@/components/stepper/stepper-models";
import {
  ORGANIZATION_PAGE_SIZE,
  TENANT_WIDE_ORGANIZATION,
} from "../../constants/subscription.constants";
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
      setSubmissionError(
        error instanceof Error ? error.message : "This plan could not be saved.",
      );
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader title={title} description={description} backTo={backTo} />

      <FormProvider {...form}>
        <form
          onSubmit={(event) => {
            event.preventDefault();
          }}
        >
          <div className="grid gap-5 xl:grid-cols-[14rem_minmax(0,1fr)_22rem]">
            <div className="hidden xl:block">
              <Card className="sticky top-5 rounded-xl">
                <StepVerticalTrackBar />
              </Card>
            </div>
            <div className="xl:hidden">
              <Card className="rounded-xl">
                <StepHorizontalTrackBar />
              </Card>
            </div>

            <Card className="min-w-0 rounded-xl">
              {currentStep === 1 && <StepIdentity isEditing={isEditing} />}
              {currentStep === 2 && <StepPricingModel isEditing={isEditing} />}
              {currentStep === 3 && <StepUsageLimits />}
              {currentStep === 4 && <StepTrial />}
              {currentStep === 5 && <StepReview plan={summary} />}

              {submissionError && (
                <div
                  role="alert"
                  className="mt-5 rounded-lg border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive"
                >
                  {submissionError}
                </div>
              )}

              {!isLastStep && (
                <Collapsible className="mt-5 xl:hidden">
                  <CollapsibleTrigger className="flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
                    <ChevronDown className="h-4 w-4" />
                    Preview this plan so far
                  </CollapsibleTrigger>
                  <CollapsibleContent className="pt-3">
                    <PlanSummaryCard plan={summary} />
                  </CollapsibleContent>
                </Collapsible>
              )}

              <div className="mt-6 flex justify-between border-t pt-5">
                <Button
                  type="button"
                  variant="outline"
                  onClick={previousStep}
                  disabled={currentStep === 1 || isSubmitting}
                >
                  <ArrowLeft className="mr-2 h-4 w-4" />
                  Back
                </Button>

                {isLastStep ? (
                  <Button type="button" onClick={submit} disabled={isSubmitting}>
                    {isSubmitting ? (
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    ) : (
                      <Check className="mr-2 h-4 w-4" />
                    )}
                    {isSubmitting ? submittingLabel : submitLabel}
                  </Button>
                ) : (
                  <Button type="button" onClick={nextStep}>
                    Next
                    <ArrowRight className="ml-2 h-4 w-4" />
                  </Button>
                )}
              </div>
            </Card>

            <div className="hidden xl:block">
              <div className="sticky top-5">
                <PlanSummaryCard plan={summary} />
              </div>
            </div>
          </div>
        </form>
      </FormProvider>
    </main>
  );
};
