import { zodResolver } from "@hookform/resolvers/zod";
import { ArrowLeft, ArrowRight, Check, ChevronDown, Loader2 } from "lucide-react";
import { useState } from "react";
import { FormProvider, useForm, useWatch } from "react-hook-form";
import { useNavigate, useParams } from "react-router";
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
} from "../constants/subscription.constants";
import { PlanSummaryCard, type PlanSummaryData } from "../components/plan-summary-card";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { StepIdentity } from "../components/plan-builder/step-identity";
import { StepPricingModel } from "../components/plan-builder/step-pricing-model";
import { StepReview } from "../components/plan-builder/step-review";
import { StepTrial } from "../components/plan-builder/step-trial";
import { StepUsageLimits } from "../components/plan-builder/step-usage-limits";
import { withOrganizationScope } from "../hooks/use-organization-scope";
import { useCreateSubscriptionPlan } from "../hooks/use-create-subscription-plan";
import {
  createSubscriptionPlanSchema,
  defaultSubscriptionPlanFormValues,
  type CreateSubscriptionPlanFormValues,
} from "../schemas/subscription-plan.schema";
import type { CreateSubscriptionPlanRequest } from "../models/subscription-plan.model";

const STEPS: Steps = [
  { id: 1, title: "Identity" },
  { id: 2, title: "Pricing model" },
  { id: 3, title: "Usage limits" },
  { id: 4, title: "Trial" },
  { id: 5, title: "Review" },
];

const LIMIT_KIND_NAME = ["Boolean", "Count", "Unlimited"] as const;

export const CreateSubscriptionPlanPage = () => {
  return (
    <StepperProviderComponent steps={STEPS}>
      <CreateSubscriptionPlanWizard />
    </StepperProviderComponent>
  );
};

const CreateSubscriptionPlanWizard = () => {
  const { itemId } = useParams();
  const navigate = useNavigate();
  const { currentStep, nextStep, previousStep, totalSteps } = useStepper();
  const { mutateAsync, isPending } = useCreateSubscriptionPlan();
  const [submissionError, setSubmissionError] = useState<string | null>(null);
  const listPath = `/app/${itemId ?? ""}/subscription/plans`;

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });

  const form = useForm<CreateSubscriptionPlanFormValues>({
    resolver: zodResolver(createSubscriptionPlanSchema),
    mode: "onBlur",
    defaultValues: defaultSubscriptionPlanFormValues,
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
      limitKind: LIMIT_KIND_NAME[entitlement?.limitKind ?? 0],
      limit: entitlement?.limit ?? null,
      unitLabel: entitlement?.unitLabel ?? null,
    })),
    prices: [],
  };

  const isLastStep = currentStep === totalSteps;

  const submit = async () => {
    setSubmissionError(null);

    const valid = await form.trigger();

    if (!valid) {
      toast({
        variant: "destructive",
        title: "Check the highlighted fields",
        description: "Some steps still have something to fix before this plan can be created.",
      });
      return;
    }

    const values = form.getValues();

    const request: CreateSubscriptionPlanRequest = {
      code: values.code.trim(),
      displayName: values.displayName.trim(),
      description: values.description?.trim() || undefined,
      featuresJson: values.featuresJson?.trim() || undefined,
      organizationId:
        values.organizationId === TENANT_WIDE_ORGANIZATION ? undefined : values.organizationId,
      trialDays: values.trialDays,
      trialRequiresPaymentMethod: values.trialRequiresPaymentMethod,
      quantityItems: values.quantityItems.map((item) => ({
        itemKey: item.itemKey.trim(),
        unitLabel: item.unitLabel.trim(),
        minQuantity: item.minQuantity,
        maxQuantity: item.maxQuantity,
        defaultQuantity: item.defaultQuantity,
      })),
      meters: values.meters.map((meter) => ({
        meterKey: meter.meterKey.trim(),
        displayName: meter.displayName.trim(),
        unitLabel: meter.unitLabel.trim(),
        aggregation: meter.aggregation,
        includedQuantity: meter.includedQuantity,
        overageAllowed: meter.overageAllowed,
        thresholdPercents: meter.thresholdPercents,
        rateTables: meter.rateTables.map((table) => ({
          currencyCode: table.currencyCode,
          tiers: table.tiers,
        })),
      })),
      entitlements: values.entitlements.map((entitlement) => ({
        key: entitlement.key.trim(),
        limitKind: entitlement.limitKind,
        limit: entitlement.limit,
        meterKey: entitlement.meterKey?.trim() || undefined,
        unitLabel: entitlement.unitLabel?.trim() || undefined,
      })),
      trialGrants: values.trialGrants.map((grant) => ({
        meterKey: grant.meterKey.trim(),
        includedQuantity: grant.includedQuantity,
      })),
    };

    try {
      const plan = await mutateAsync(request);

      toast({
        variant: "success",
        title: "Plan created",
        description: `${plan.displayName} is ready.`,
      });

      // Carries the organization the plan was just scoped to. Without it the detail page
      // resolves as the console organization, and a plan belonging to someone else reads as
      // missing — the plan is created and then immediately unreachable.
      navigate(
        withOrganizationScope(
          `${listPath}/${encodeURIComponent(plan.planId)}`,
          plan.organizationId,
        ),
      );
    } catch (error) {
      setSubmissionError(
        error instanceof Error ? error.message : "The plan could not be created.",
      );
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title="Create subscription plan"
        description="A guided walkthrough — nothing is created until you review and confirm at the end."
        backTo={listPath}
      />

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
              {currentStep === 1 && <StepIdentity />}
              {currentStep === 2 && <StepPricingModel />}
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
                  disabled={currentStep === 1 || isPending}
                >
                  <ArrowLeft className="mr-2 h-4 w-4" />
                  Back
                </Button>

                {isLastStep ? (
                  <Button type="button" onClick={submit} disabled={isPending}>
                    {isPending ? (
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    ) : (
                      <Check className="mr-2 h-4 w-4" />
                    )}
                    {isPending ? "Creating plan…" : "Create plan"}
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
