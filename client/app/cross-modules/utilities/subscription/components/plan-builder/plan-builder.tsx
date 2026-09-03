import { zodResolver } from "@hookform/resolvers/zod";
import { ChevronDown } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { FormProvider, useForm, useWatch } from "react-hook-form";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
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
  firstPlanBuilderErrorField,
  firstPlanBuilderErrorStep,
  PLAN_BUILDER_STEP_TITLES,
  type PlanBuilderFieldPath,
} from "../../utilities/plan-builder-steps";
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
import { toBasisPoints } from "../../utilities/subscription-tax";
import { FLAT_FEE } from "../../schemas/subscription-price.schema";
import { toMinorUnits } from "../../utilities/subscription-format";
import { PlanSummaryCard, type PlanSummaryData } from "../plan-summary-card";
import { SubscriptionPlanPageHeader } from "../subscription-plan-page-header";
import { PlanBuilderActions } from "./plan-builder-actions";
import { PlanBuilderProgress } from "./plan-builder-progress";
import { previewStickyTop, useStickyStepper } from "./use-sticky-stepper";
import { StepIdentity } from "./step-identity";
import { StepPricingModel } from "./step-pricing-model";
import { StepReview } from "./step-review";
import { StepTrial } from "./step-trial";
import { StepUsageLimits } from "./step-usage-limits";

const STEPS: Steps = [
  { id: 1, title: "Identity" },
  { id: 2, title: "Pricing model" },
  { id: 3, title: "What the plan grants" },
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
  /**
   * Retires one of the prices above. Omitted where nothing can be retired — creating a plan.
   */
  onRetirePrice?: (priceId: string) => void;
  onUpdatePriceTax?: (priceId: string, taxPercent?: number, taxMode?: "Exclusive" | "Inclusive") => Promise<void>;
  onUpdatePriceDiscount?: (priceId: string, discountPercent?: number, combination?: "BestDiscount" | "Additive") => Promise<void>;
  retiringPriceId?: string | null;
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
  onRetirePrice,
  onUpdatePriceTax,
  onUpdatePriceDiscount,
  retiringPriceId = null,
  onSubmit,
}: PlanBuilderProps) => {
  const { currentStep, nextStep, previousStep, totalSteps, goToStep } = useStepper();

  // The field to focus once the step holding it has mounted. Focusing during the same commit that
  // changes the step would find nothing: the control does not exist until that step renders.
  //
  // A ref rather than state because nothing renders from it — it is a message to the effect below,
  // consumed once and discarded. Holding it in state would ask for a re-render that changes no
  // output, and clearing it from inside the effect is the write React lints against.
  const pendingFocus = useRef<PlanBuilderFieldPath | null>(null);
  const [submissionError, setSubmissionError] = useState<string | null>(null);
  const isEditing = mode === "edit";
  const { stepperRef, isStuck, stepperHeight } = useStickyStepper();

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
    trialDurationKind: draft.trialDurationKind ?? null,
    trialDurationCount: draft.trialDurationCount ?? null,
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
      resetPolicy: meter?.resetPolicy ?? 0,
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
      taxRateBasisPoints: price?.taxPercent ? toBasisPoints(price.taxPercent) : null,
      taxMode: price?.taxPercent ? price.taxMode : null,
      // The review step summarises what will be sold, and a price with 8% off it is not the price
      // the amount alone says.
      automaticDiscountBasisPoints: price?.automaticDiscountPercent
        ? toBasisPoints(price.automaticDiscountPercent)
        : null,
    })),
  };

  useEffect(() => {
    const field = pendingFocus.current;

    if (field === null) {
      return;
    }

    pendingFocus.current = null;

    // The step is on screen by the time this runs, so the control is registered and focusable.
    // setFocus is a no-op for a name it cannot resolve — an array-level error whose path is the
    // array itself, for instance — and the step change has already done the useful half of the
    // job, so a miss is not worth reporting.
    form.setFocus(field, { shouldSelect: true });
  }, [currentStep, form]);

  const isLastStep = currentStep === totalSteps;

  // Assembled when the button is pressed rather than during render, so nothing on the render path
  // touches the focus target or runs a submit handler.
  const submit = () =>
    // handleSubmit rather than trigger, for the errors it hands the second callback. formState is a
    // render-time snapshot, so reading form.formState.errors straight after awaiting trigger() sees
    // the state from before validation ran — which reported "nothing is wrong" and sent the author
    // nowhere, the very thing this is here to fix.
    form.handleSubmit(
      async (values) => {
        setSubmissionError(null);

        try {
          await onSubmit(values);
        } catch (error) {
          setSubmissionError(
            error instanceof Error ? error.message : "This plan could not be saved.",
          );
        }
      },
      (errors) => {
        setSubmissionError(null);

        // Saving happens from the review step, so whatever is wrong is on a step that is not on
        // screen. Describing that ("some steps still have something to fix") left the author to open
        // each one and hunt, and react-hook-form's own focus-first-error cannot help either: the
        // control is unmounted, so there is nothing to focus. So go to the step first, then focus.
        const step = firstPlanBuilderErrorStep(errors);
        const field = firstPlanBuilderErrorField(errors);

        if (step !== undefined) {
          goToStep(step);
          // Focused once that step has rendered — see the effect above.
          pendingFocus.current = field ?? null;
        }

        toast({
          variant: "destructive",
          title:
            step === undefined
              ? "Check the highlighted fields"
              : `Something to fix in ${PLAN_BUILDER_STEP_TITLES[step] ?? `step ${step}`}`,
          description:
            step === undefined
              ? "Some steps still have something to fix before this can be saved."
              : "Taken to the first field that needs attention.",
        });
      },
    )();

  // `overflow-hidden` cannot live on <main> any more: an ancestor whose overflow is not `visible`
  // becomes the scrolling box for any sticky descendant, and <main> never scrolls - the page does.
  // That is why the `sticky` already present on the preview column was inert. The decorative blurs
  // still need clipping (H6), so they move into an absolutely-positioned clip layer: it is not an
  // ancestor of the sticky elements, so its overflow cannot re-break them.
  //
  // `overflow-clip` on <main> would be the tidier one-word fix and does not create a scroll
  // container - but it needs Safari 16, and this client sets no browserslist and no Vite build
  // target, so Safari 14/15 are in the baseline. There it would silently stop clipping.
  return (
    <main className="relative min-w-0 bg-gradient-to-b from-blocks-primary-shades-50/60 via-background to-background px-4 py-5 sm:px-6 sm:py-7 lg:px-8 lg:py-9">
      <div
        className="pointer-events-none absolute inset-0 overflow-hidden"
        aria-hidden="true"
        data-testid="plan-builder-decoration"
      >
        <div className="pointer-events-none absolute -right-32 top-24 h-80 w-80 rounded-full bg-blocks-secondary-100/30 blur-3xl" />
        <div className="pointer-events-none absolute -left-32 top-96 h-72 w-72 rounded-full bg-blocks-primary-100/30 blur-3xl" />
        <div className="pointer-events-none absolute -right-24 top-[42rem] h-64 w-64 rounded-full bg-blocks-primary-shades-300/60 blur-3xl" />
      </div>

      <div className="relative mx-auto max-w-[96rem] space-y-5">
        <SubscriptionPlanPageHeader title={title} description={description} backTo={backTo} />

        <FormProvider {...form}>
          <form
            onSubmit={(event) => {
              event.preventDefault();
            }}
          >
            <div className="space-y-5">
              {/*
                z-30 keeps the stepper above the form while staying below the z-50 Radix
                Select/Popover portals, so an open dropdown still layers over it (H4/C4).
              */}
              <div ref={stepperRef} className="sticky top-0 z-30">
                <PlanBuilderProgress isStuck={isStuck} />
              </div>

              <div className="grid min-w-0 gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
                <Card className="relative min-w-0 overflow-visible rounded-2xl border-border/60 bg-card/95 p-5 shadow-[0_24px_60px_-40px_hsl(var(--blocks-primary-700)/0.45)] backdrop-blur-sm transition-shadow duration-300 supports-[backdrop-filter]:bg-card/90 sm:p-7 [&_h3]:tracking-tight">
                  {/*
                    Keyed on the step so React remounts the body on every move, which is what lets
                    the enter animation replay - without the key it would run once, on first paint,
                    and every later step would appear instantly.
                  */}
                  <div
                    key={currentStep}
                    className="duration-300 animate-in fade-in slide-in-from-bottom-2"
                  >
                    {currentStep === 1 && <StepIdentity isEditing={isEditing} />}
                    {currentStep === 2 && (
                      <StepPricingModel
                        isEditing={isEditing}
                        existingPrices={existingPrices}
                        onRetirePrice={onRetirePrice}
                        onUpdatePriceTax={onUpdatePriceTax}
                        onUpdatePriceDiscount={onUpdatePriceDiscount}
                        retiringPriceId={retiringPriceId}
                      />
                    )}
                    {currentStep === 3 && <StepUsageLimits />}
                    {currentStep === 4 && <StepTrial />}
                    {currentStep === 5 && <StepReview plan={summary} />}
                  </div>

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

                  <PlanBuilderActions
                    isFirstStep={currentStep === 1}
                    isLastStep={isLastStep}
                    isSubmitting={isSubmitting}
                    submitLabel={submitLabel}
                    submittingLabel={submittingLabel}
                    onBack={previousStep}
                    onNext={nextStep}
                    onSubmit={submit}
                  />
                </Card>

                <aside className="hidden xl:block" aria-label="Plan preview">
                  {/*
                    Offset is measured, not hard-coded: the stepper's height changes with the
                    breakpoint (its description line and step titles are sm:-only, its
                    current-step title is sm:hidden), so a fixed value would overlap at some
                    widths and gap at others - exactly what H2 rules out.
                  */}
                  <div
                    className="sticky"
                    style={{ top: previewStickyTop(stepperHeight) }}
                    data-testid="plan-preview-sticky"
                  >
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
