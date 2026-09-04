import { useMemo, useState } from "react";
import { AlertCircle, FlaskConical, Layers, RefreshCw } from "lucide-react";
import { useSearchParams } from "react-router";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { toast } from "@/hooks/use-toast";
import {
  ORGANIZATION_PAGE_SIZE,
  ORGANIZATION_QUERY_PARAM,
  TENANT_WIDE_ORGANIZATION,
} from "../../subscription/constants/subscription.constants";
import { SubscriptionPlanPageHeader } from "../../subscription/components/subscription-plan-page-header";
import { useSubscriptionPlans } from "../../subscription/hooks/use-subscription-plans";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { AdvanceRenewalDialog } from "../components/advance-renewal-dialog";
import { AuditTrailDialog } from "../components/audit-trail-dialog";
import { CancelSubscriptionDialog } from "../components/cancel-subscription-dialog";
import { ChangePlanDialog } from "../components/change-plan-dialog";
import { ChangeQuantityDialog } from "../components/change-quantity-dialog";
import { CloseUsagePeriodDialog } from "../components/close-usage-period-dialog";
import { DataConsoleDialog } from "../components/data-console-dialog";
import { useCancelPendingQuantityChange } from "../hooks/use-quantity-change";
import { useCancelPendingPlanChange } from "../hooks/use-change-subscription-plan";
import { useStartPaymentMethodSetup } from "../hooks/use-start-payment-method-setup";
import { CurrentSubscriptionCard } from "../components/current-subscription-card";
import { OverageTermsSection } from "../components/overage-terms-section";
import { PaymentOutcomeDialog } from "../components/payment-outcome-dialog";
import { RunDueJobsDialog } from "../components/run-due-jobs-dialog";
import { SimulatedPlanCard } from "../components/simulated-plan-card";
import { SimulationHarnessCard } from "../components/simulation-harness-card";
import { SubscribeDialog } from "../components/subscribe-dialog";
import { UsageSection } from "../components/usage-section";
import { useCurrentSimulatedSubscription } from "../hooks/use-current-simulated-subscription";
import type {
  SubscriptionSimulationActionResponse,
  SubscriptionSimulationJobRunResponse,
} from "../models/subscription-simulation-harness.model";

// Reserves the organization's one open signup or live subscription slot — includes Incomplete.
const GRANTING_STATUSES = new Set(["Incomplete", "Trialing", "Active", "PastDue"]);
// Actually grants entitlements per the lifecycle: Incomplete has not paid yet and grants nothing.
const ENTITLED_STATUSES = new Set(["Trialing", "Active", "PastDue"]);

export const SubscriptionSimulationPage = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const organizationScope = searchParams.get(ORGANIZATION_QUERY_PARAM) ?? undefined;
  const [subscribingTo, setSubscribingTo] = useState<SubscriptionPlan | null>(null);
  const [isCanceling, setIsCanceling] = useState(false);
  const [isChangingPlan, setIsChangingPlan] = useState(false);
  const [isChangingQuantity, setIsChangingQuantity] = useState(false);
  const [isViewingAuditTrail, setIsViewingAuditTrail] = useState(false);
  const [isSimulatingPaymentOutcome, setIsSimulatingPaymentOutcome] = useState(false);
  const [isAdvancingRenewal, setIsAdvancingRenewal] = useState(false);
  const [isClosingUsagePeriod, setIsClosingUsagePeriod] = useState(false);
  const [isRunningDueJobs, setIsRunningDueJobs] = useState(false);
  const [isDataConsoleOpen, setIsDataConsoleOpen] = useState(false);
  const [harnessResult, setHarnessResult] = useState<
    SubscriptionSimulationActionResponse | SubscriptionSimulationJobRunResponse | null
  >(null);

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });
  const organizations = organizationsData?.organizations ?? [];

  const scopeLabel = useMemo(() => {
    if (!organizationScope) {
      // Not "Tenant-wide", which is what this said and is not what happens. Naming no
      // organization does not act tenant-wide: the server reads it as the console's own
      // organization (Payment:ConsoleOrganizationId, "default" out of the box) and a
      // subscription created here is stored under it. Calling it tenant-wide left an operator
      // believing they had a subscription belonging to no organization in particular, which
      // nothing in the model can produce.
      return "Console organization";
    }
    return (
      organizationsData?.organizations?.find(
        (organization) => organization.itemId === organizationScope,
      )?.name ?? organizationScope
    );
  }, [organizationsData, organizationScope]);

  const {
    data: plans,
    error: plansError,
    isError: isPlansError,
    isFetching: isFetchingPlans,
    isLoading: isLoadingPlans,
    refetch: refetchPlans,
  } = useSubscriptionPlans(organizationScope);

  const {
    data: currentSubscription,
    error: currentError,
    isError: isCurrentError,
    isLoading: isLoadingCurrent,
    refetch: refetchCurrent,
  } = useCurrentSimulatedSubscription(organizationScope);

  const hasActiveSubscription = Boolean(
    currentSubscription && GRANTING_STATUSES.has(currentSubscription.status),
  );
  const isEntitled = Boolean(
    currentSubscription && ENTITLED_STATUSES.has(currentSubscription.status),
  );
  const currentPlan = plans?.find((plan) => plan.code === currentSubscription?.planCode);

  const refresh = () => {
    refetchPlans();
    refetchCurrent();
  };

  const cancelPendingQuantity = useCancelPendingQuantityChange();
  const cancelPendingPlan = useCancelPendingPlanChange();
  const startPaymentMethodSetup = useStartPaymentMethodSetup();

  /**
   * Stores a card against the subscription that already exists -- never a payment, whichever of
   * the three cases the card is offering it for. The session opened is surfaced by the card
   * re-rendering `pendingCheckout` once this invalidates the query, the same way a fresh signup's
   * own checkout URL already appears.
   */
  const addPaymentMethod = async () => {
    if (!currentSubscription) {
      return;
    }

    try {
      await startPaymentMethodSetup.mutateAsync({
        subscriptionId: currentSubscription.subscriptionId,
        organizationId: organizationScope,
      });

      toast({
        title: "Card setup ready",
        description:
          "Open the checkout link from the current subscription card above. No charge has " +
          "been made.",
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: "The card could not be added",
        description:
          error instanceof Error ? error.message : "Reload and try again.",
      });
    }
  };

  /**
   * Withdraws a scheduled reduction. Reported as a toast rather than inline, because the control
   * that raised it disappears the moment the subscription is re-read without a pending change.
   */
  const withdrawScheduledQuantityChange = async () => {
    if (!currentSubscription) {
      return;
    }

    try {
      await cancelPendingQuantity.mutateAsync({
        subscriptionId: currentSubscription.subscriptionId,
        organizationId: organizationScope,
      });

      toast({
        variant: "success",
        title: "Scheduled reduction withdrawn",
        description: "The current quantity stays in force.",
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: "The reduction could not be withdrawn",
        description:
          error instanceof Error ? error.message : "Reload and try again.",
      });
    }
  };

  /**
   * Withdraws a scheduled plan change. Reported as a toast for the same reason the reduction
   * above is: the control that raised it disappears the moment the subscription is re-read.
   */
  const withdrawScheduledPlanChange = async () => {
    if (!currentSubscription) {
      return;
    }

    try {
      await cancelPendingPlan.mutateAsync({
        subscriptionId: currentSubscription.subscriptionId,
        organizationId: organizationScope,
      });

      toast({
        variant: "success",
        title: "Scheduled plan change withdrawn",
        description: "The current plan stays in force.",
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: "The scheduled plan change could not be withdrawn",
        description:
          error instanceof Error ? error.message : "Reload and try again.",
      });
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title="Subscription simulation"
        description="Exercise the Subscription module's client-facing API as an integrating application would — pick an organization, subscribe it to a plan, and watch the result."
        icon={<FlaskConical className="h-6 w-6" />}
        actions={
          <Button
            variant="outline"
            className="bg-background/80"
            onClick={refresh}
            disabled={isFetchingPlans}
          >
            <RefreshCw className={`mr-2 h-4 w-4 ${isFetchingPlans ? "animate-spin" : ""}`} />
            Refresh
          </Button>
        }
      />

      <Card className="rounded-xl p-0">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
          <div>
            <h2 className="font-semibold">Acting as</h2>
            <p className="text-xs text-muted-foreground">
              Every action below is scoped to this organization, exactly as it would be for a
              real integration.
              {!organizationScope &&
                " Naming no organization acts as the console's own — a real organization, not a tenant-wide one, and what a subscription created here belongs to."}
            </p>
          </div>

          <Select
            value={organizationScope ?? TENANT_WIDE_ORGANIZATION}
            onValueChange={(value) =>
              setSearchParams(
                value === TENANT_WIDE_ORGANIZATION ? {} : { [ORGANIZATION_QUERY_PARAM]: value },
              )
            }
          >
            <SelectTrigger className="sm:w-64" aria-label="Organization">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TENANT_WIDE_ORGANIZATION}>
                Console organization
              </SelectItem>
              {organizations.map((organization) => (
                <SelectItem key={organization.itemId} value={organization.itemId}>
                  {organization.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="space-y-2 p-4 sm:p-5">
          <h3 className="text-sm font-medium text-muted-foreground">Current subscription</h3>
          <CurrentSubscriptionCard
            subscription={currentSubscription}
            isLoading={isLoadingCurrent}
            isError={isCurrentError}
            error={currentError}
            scopeLabel={scopeLabel}
            onRetry={() => refetchCurrent()}
            onCancel={() => setIsCanceling(true)}
            onChangePlan={() => setIsChangingPlan(true)}
            onChangeQuantity={() => setIsChangingQuantity(true)}
            onCancelPendingQuantityChange={withdrawScheduledQuantityChange}
            isCancelingPendingQuantityChange={cancelPendingQuantity.isPending}
            onCancelPendingPlanChange={withdrawScheduledPlanChange}
            isCancelingPendingPlanChange={cancelPendingPlan.isPending}
            onViewAuditTrail={() => setIsViewingAuditTrail(true)}
            onAddPaymentMethod={addPaymentMethod}
            isStartingPaymentMethodSetup={startPaymentMethodSetup.isPending}
          />
        </div>
      </Card>

      <Card className="rounded-xl p-0">
        <div className="border-b p-4 sm:p-5">
          <h2 className="font-semibold">Plan catalogue</h2>
          <p className="text-xs text-muted-foreground">
            Every plan visible to <strong>{scopeLabel}</strong> — the same list a
            <code className="mx-1 rounded bg-muted px-1">
              GET /api/subscription-plans
            </code>
            call returns.
          </p>
        </div>

        <div className="p-4 sm:p-5">
          {isLoadingPlans ? (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3" aria-label="Loading plans">
              {Array.from({ length: 3 }, (_, index) => (
                <Skeleton key={index} className="h-44 w-full rounded-xl" />
              ))}
            </div>
          ) : isPlansError ? (
            <div className="flex min-h-72 flex-col items-center justify-center text-center">
              <span className="rounded-full bg-destructive/10 p-4 text-destructive">
                <AlertCircle className="h-7 w-7" />
              </span>
              <h3 className="mt-4 text-lg font-semibold">Plans could not be loaded</h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">
                {plansError instanceof Error ? plansError.message : "Try loading the plan list again."}
              </p>
              <Button className="mt-5" variant="outline" onClick={() => refetchPlans()}>
                Try again
              </Button>
            </div>
          ) : !plans?.length ? (
            <div className="flex min-h-72 flex-col items-center justify-center text-center">
              <span className="rounded-full bg-muted p-4 text-muted-foreground">
                <Layers className="h-7 w-7" />
              </span>
              <h3 className="mt-4 text-lg font-semibold">No plan on sale for this scope</h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">
                Author one from the subscription plans screen, or choose a different
                organization.
              </p>
            </div>
          ) : (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {plans.map((plan) => (
                <SimulatedPlanCard
                  key={plan.planId}
                  plan={plan}
                  organizationLabel={
                    plan.organizationId ? scopeLabel : "Tenant-wide"
                  }
                  hasActiveSubscription={hasActiveSubscription}
                  onSubscribe={() => setSubscribingTo(plan)}
                />
              ))}
            </div>
          )}
        </div>
      </Card>

      {currentSubscription && (
        <OverageTermsSection subscription={currentSubscription} organizationId={organizationScope} />
      )}

      {isEntitled && <UsageSection plan={currentPlan} organizationId={organizationScope} />}

      {currentSubscription && (
        <SimulationHarnessCard
          onSimulatePaymentOutcome={() => setIsSimulatingPaymentOutcome(true)}
          onAdvanceRenewal={() => setIsAdvancingRenewal(true)}
          onCloseUsagePeriod={() => setIsClosingUsagePeriod(true)}
          onRunDueJobs={() => setIsRunningDueJobs(true)}
          onOpenDataConsole={() => setIsDataConsoleOpen(true)}
          lastResult={harnessResult}
        />
      )}

      {subscribingTo && (
        <SubscribeDialog
          plan={subscribingTo}
          organizationId={organizationScope}
          open={Boolean(subscribingTo)}
          onOpenChange={(open) => {
            if (!open) {
              setSubscribingTo(null);
            }
          }}
          onSubscribed={(checkoutUrl) => {
            if (checkoutUrl) {
              toast({
                title: "Checkout ready",
                description: "Open the checkout link from the current subscription card above.",
              });
            }
          }}
        />
      )}

      {isCanceling && currentSubscription && (
        <CancelSubscriptionDialog
          subscription={currentSubscription}
          organizationId={organizationScope}
          open={isCanceling}
          onOpenChange={setIsCanceling}
        />
      )}

      {isChangingQuantity && currentSubscription && (
        <ChangeQuantityDialog
          subscription={currentSubscription}
          currentPlan={currentPlan}
          organizationId={organizationScope}
          open={isChangingQuantity}
          onOpenChange={setIsChangingQuantity}
          onRefresh={() => refetchCurrent()}
        />
      )}

      {isChangingPlan && currentSubscription && (
        <ChangePlanDialog
          subscription={currentSubscription}
          currentPlan={currentPlan}
          plans={plans ?? []}
          organizationId={organizationScope}
          open={isChangingPlan}
          onOpenChange={setIsChangingPlan}
        />
      )}

      {isViewingAuditTrail && currentSubscription && (
        <AuditTrailDialog
          subscriptionId={currentSubscription.subscriptionId}
          planName={currentSubscription.planName}
          organizationId={organizationScope}
          open={isViewingAuditTrail}
          onOpenChange={setIsViewingAuditTrail}
        />
      )}

      {isSimulatingPaymentOutcome && currentSubscription && (
        <PaymentOutcomeDialog
          subscriptionId={currentSubscription.subscriptionId}
          organizationId={organizationScope}
          open={isSimulatingPaymentOutcome}
          onOpenChange={setIsSimulatingPaymentOutcome}
          onResult={setHarnessResult}
        />
      )}

      {isAdvancingRenewal && currentSubscription && (
        <AdvanceRenewalDialog
          subscriptionId={currentSubscription.subscriptionId}
          organizationId={organizationScope}
          open={isAdvancingRenewal}
          onOpenChange={setIsAdvancingRenewal}
          onResult={setHarnessResult}
        />
      )}

      {isClosingUsagePeriod && currentSubscription && (
        <CloseUsagePeriodDialog
          subscriptionId={currentSubscription.subscriptionId}
          organizationId={organizationScope}
          open={isClosingUsagePeriod}
          onOpenChange={setIsClosingUsagePeriod}
          onResult={setHarnessResult}
        />
      )}

      {isRunningDueJobs && currentSubscription && (
        <RunDueJobsDialog
          subscriptionId={currentSubscription.subscriptionId}
          organizationId={organizationScope}
          open={isRunningDueJobs}
          onOpenChange={setIsRunningDueJobs}
          onResult={setHarnessResult}
        />
      )}

      {isDataConsoleOpen && currentSubscription && (
        <DataConsoleDialog
          subscriptionId={currentSubscription.subscriptionId}
          organizationId={organizationScope}
          open={isDataConsoleOpen}
          onOpenChange={setIsDataConsoleOpen}
        />
      )}
    </main>
  );
};
