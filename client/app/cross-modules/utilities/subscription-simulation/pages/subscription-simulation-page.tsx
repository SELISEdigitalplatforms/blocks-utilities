import { useMemo, useState } from "react";
import { AlertCircle, FlaskConical, Layers, RefreshCw } from "lucide-react";
import { useSearchParams } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";
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
import { ToastAction } from "@/components/ui-kits/toaster/toast";
import {
  ORGANIZATION_PAGE_SIZE,
  ORGANIZATION_QUERY_PARAM,
  TENANT_WIDE_ORGANIZATION,
} from "../../subscription/constants/subscription.constants";
import { SubscriptionPlanPageHeader } from "../../subscription/components/subscription-plan-page-header";
import { useSubscriptionPlans } from "../../subscription/hooks/use-subscription-plans";
import {
  SUBSCRIBER_SCOPE,
  type SubscriptionPlan,
} from "../../subscription/models/subscription-plan.model";
import { AdvanceRenewalDialog } from "../components/advance-renewal-dialog";
import { AuditTrailDialog } from "../components/audit-trail-dialog";
import { CancelSubscriptionDialog } from "../components/cancel-subscription-dialog";
import { ChangePlanDialog } from "../components/change-plan-dialog";
import { ChangeQuantityDialog } from "../components/change-quantity-dialog";
import { CloseUsagePeriodDialog } from "../components/close-usage-period-dialog";
import { DataConsoleDialog } from "../components/data-console-dialog";
import { DiscountCodeCard } from "../components/discount-code-card";
import { MembersCard } from "../components/members-card";
import { useCancelPendingQuantityChange } from "../hooks/use-quantity-change";
import { useCancelPendingPlanChange } from "../hooks/use-change-subscription-plan";
import { useWithdrawCancellation } from "../hooks/use-cancel-subscription";
import { useStartPaymentMethodSetup } from "../hooks/use-start-payment-method-setup";
import { CurrentSubscriptionCard } from "../components/current-subscription-card";
import { OverageTermsSection } from "../components/overage-terms-section";
import { CreditsCard } from "../components/credits-card";
import { PaymentOutcomeDialog } from "../components/payment-outcome-dialog";
import { RunDueJobsDialog } from "../components/run-due-jobs-dialog";
import { SimulatedPlanCard } from "../components/simulated-plan-card";
import { SimulationHarnessCard } from "../components/simulation-harness-card";
import { SubscribeDialog } from "../components/subscribe-dialog";
import { UsageSection } from "../components/usage-section";
import { useCurrentSimulatedSubscription } from "../hooks/use-current-simulated-subscription";
import { useMyPlaces } from "../hooks/use-members";
import type {
  SubscriptionSimulationActionResponse,
  SubscriptionSimulationJobRunResponse,
} from "../models/subscription-simulation-harness.model";

// Reserves the organization's one open signup or live subscription slot — includes Incomplete.
const GRANTING_STATUSES = new Set(["Incomplete", "Trialing", "Active", "PastDue"]);
// Actually grants entitlements per the lifecycle: Incomplete has not paid yet and grants nothing.
const ENTITLED_STATUSES = new Set(["Trialing", "Active", "PastDue"]);

export const SubscriptionSimulationPage = () => {
  const { t } = useT("utilities.subscription-simulation");
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
      return t("SUBSCRIPTION_SIMULATION_PAGE.CONSOLE_ORGANIZATION");
    }
    return (
      organizationsData?.organizations?.find(
        (organization) => organization.itemId === organizationScope,
      )?.name ?? organizationScope
    );
  }, [organizationsData, organizationScope, t]);

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

  // The plans the signed-in user holds a place on — not every user-wise plan the organization
  // holds. A plan with no place of theirs lends them neither its limits nor its entitlements, and
  // reading one anyway gated their usage on an entitlement they did not have.
  const { data: myPlaces } = useMyPlaces(organizationScope);
  const memberPlans = (plans ?? []).filter((plan) =>
    (myPlaces ?? []).some(
      (place) => ENTITLED_STATUSES.has(place.status) && place.planCode === plan.code,
    ),
  );

  const queryClient = useQueryClient();

  // Everything the page shows, not just the plans and the current subscription: the per-person
  // list, its rosters and the usage read too. A payment landing in Stripe changes those without
  // any action here, and only a reload used to bring the Members card up to date.
  const refresh = () => {
    refetchPlans();
    refetchCurrent();
    queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] });
    queryClient.invalidateQueries({ queryKey: ["subscription-simulation-members"] });
    queryClient.invalidateQueries({ queryKey: ["subscription-usage"] });
  };

  const cancelPendingQuantity = useCancelPendingQuantityChange();
  const cancelPendingPlan = useCancelPendingPlanChange();
  const withdrawCancellation = useWithdrawCancellation();
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
        title: t("SUBSCRIPTION_SIMULATION_PAGE.CARD_SETUP_READY"),
        description:
          t("SUBSCRIPTION_SIMULATION_PAGE.OPEN_THE_CHECKOUT_LINK_FROM_2"),
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: t("SUBSCRIPTION_SIMULATION_PAGE.THE_CARD_COULD_NOT_BE"),
        description:
          error instanceof Error ? error.message : t("SUBSCRIPTION_SIMULATION_PAGE.RELOAD_AND_TRY_AGAIN"),
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
        title: t("SUBSCRIPTION_SIMULATION_PAGE.SCHEDULED_REDUCTION_WITHDRAWN"),
        description: t("SUBSCRIPTION_SIMULATION_PAGE.THE_CURRENT_QUANTITY_STAYS_IN"),
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: t("SUBSCRIPTION_SIMULATION_PAGE.THE_REDUCTION_COULD_NOT_BE"),
        description:
          error instanceof Error ? error.message : t("SUBSCRIPTION_SIMULATION_PAGE.RELOAD_AND_TRY_AGAIN"),
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
        title: t("SUBSCRIPTION_SIMULATION_PAGE.SCHEDULED_PLAN_CHANGE_WITHDRAWN"),
        description: t("SUBSCRIPTION_SIMULATION_PAGE.THE_CURRENT_PLAN_STAYS_IN"),
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: t("SUBSCRIPTION_SIMULATION_PAGE.THE_SCHEDULED_PLAN_CHANGE_COULD"),
        description:
          error instanceof Error ? error.message : t("SUBSCRIPTION_SIMULATION_PAGE.RELOAD_AND_TRY_AGAIN"),
      });
    }
  };

  /**
   * Undoes a cancellation scheduled for the end of the paid period. Reported as a toast for the
   * same reason the reduction and plan-change withdrawals above are: the control that raised it
   * disappears the moment the subscription is re-read without a scheduled cancellation.
   */
  const withdrawScheduledCancellation = async () => {
    if (!currentSubscription) {
      return;
    }

    try {
      await withdrawCancellation.mutateAsync({
        subscriptionId: currentSubscription.subscriptionId,
        organizationId: organizationScope,
      });

      toast({
        variant: "success",
        title: t("SUBSCRIPTION_SIMULATION_PAGE.CANCELLATION_UNDONE"),
        description: t("SUBSCRIPTION_SIMULATION_PAGE.THIS_SUBSCRIPTION_KEEPS_RENEWING"),
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: t("SUBSCRIPTION_SIMULATION_PAGE.THE_CANCELLATION_COULD_NOT_BE"),
        description:
          error instanceof Error ? error.message : t("SUBSCRIPTION_SIMULATION_PAGE.RELOAD_AND_TRY_AGAIN"),
      });
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title={t("SUBSCRIPTION_SIMULATION_PAGE.SUBSCRIPTION_SIMULATION")}
        description={t("SUBSCRIPTION_SIMULATION_PAGE.EXERCISE_THE_SUBSCRIPTION_MODULE_S")}
        icon={<FlaskConical className="h-6 w-6" />}
        actions={
          <Button
            variant="outline"
            className="bg-background/80"
            onClick={refresh}
            disabled={isFetchingPlans}
          >
            <RefreshCw className={`mr-2 h-4 w-4 ${isFetchingPlans ? "animate-spin" : ""}`} />
            {t("common:REFRESH")}
          </Button>
        }
      />

      <Card className="rounded-xl p-0">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
          <div>
            <h2 className="font-semibold">{t("SUBSCRIPTION_SIMULATION_PAGE.ACTING_AS")}</h2>
            <p className="text-xs text-muted-foreground">
              {(organizationScope ? t("SUBSCRIPTION_SIMULATION_PAGE.EVERY_ACTION_SCOPED") : t("SUBSCRIPTION_SIMULATION_PAGE.EVERY_ACTION_SCOPED_NO_ORGANIZATION"))}
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
            <SelectTrigger className="sm:w-64" aria-label={t("SUBSCRIPTION_SIMULATION_PAGE.ORGANIZATION")}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TENANT_WIDE_ORGANIZATION}>
                {t("SUBSCRIPTION_SIMULATION_PAGE.CONSOLE_ORGANIZATION")}
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
          <h3 className="text-sm font-medium text-muted-foreground">{t("SUBSCRIPTION_SIMULATION_PAGE.CURRENT_SUBSCRIPTION")}</h3>
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
            onWithdrawCancellation={withdrawScheduledCancellation}
            isWithdrawingCancellation={withdrawCancellation.isPending}
            onViewAuditTrail={() => setIsViewingAuditTrail(true)}
            onAddPaymentMethod={addPaymentMethod}
            isStartingPaymentMethodSetup={startPaymentMethodSetup.isPending}
          />
        </div>
      </Card>

      <Card className="rounded-xl p-0">
        <div className="border-b p-4 sm:p-5">
          <h2 className="font-semibold">{t("SUBSCRIPTION_SIMULATION_PAGE.PLAN_CATALOGUE")}</h2>
          <p className="text-xs text-muted-foreground">
            {t("SUBSCRIPTION_SIMULATION_PAGE.EVERY_PLAN_VISIBLE_TO", { scope: scopeLabel, endpoint: "GET /api/subscription-plans" })}
          </p>
        </div>

        <div className="p-4 sm:p-5">
          {isLoadingPlans ? (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3" aria-label={t("SUBSCRIPTION_SIMULATION_PAGE.LOADING_PLANS")}>
              {Array.from({ length: 3 }, (_, index) => (
                <Skeleton key={index} className="h-44 w-full rounded-xl" />
              ))}
            </div>
          ) : isPlansError ? (
            <div className="flex min-h-72 flex-col items-center justify-center text-center">
              <span className="rounded-full bg-destructive/10 p-4 text-destructive">
                <AlertCircle className="h-7 w-7" />
              </span>
              <h3 className="mt-4 text-lg font-semibold">{t("SUBSCRIPTION_SIMULATION_PAGE.PLANS_COULD_NOT_BE_LOADED")}</h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">
                {plansError instanceof Error ? plansError.message : t("SUBSCRIPTION_SIMULATION_PAGE.TRY_LOADING_THE_PLAN_LIST")}
              </p>
              <Button className="mt-5" variant="outline" onClick={() => refetchPlans()}>
                {t("SUBSCRIPTION_SIMULATION_PAGE.TRY_AGAIN")}
              </Button>
            </div>
          ) : !plans?.length ? (
            <div className="flex min-h-72 flex-col items-center justify-center text-center">
              <span className="rounded-full bg-muted p-4 text-muted-foreground">
                <Layers className="h-7 w-7" />
              </span>
              <h3 className="mt-4 text-lg font-semibold">{t("SUBSCRIPTION_SIMULATION_PAGE.NO_PLAN_ON_SALE_FOR")}</h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">
                {t("SUBSCRIPTION_SIMULATION_PAGE.AUTHOR_ONE_FROM_THE_SUBSCRIPTION")}
              </p>
            </div>
          ) : (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {plans.map((plan) => (
                <SimulatedPlanCard
                  key={plan.planId}
                  plan={plan}
                  organizationLabel={
                    plan.organizationId ? scopeLabel : t("SUBSCRIPTION_SIMULATION_PAGE.TENANT_WIDE")
                  }
                  // The organization's one-subscription rule is for its own plans only. A plan
                  // sold to each person sits beside it, as many of them as the organization wants.
                  hasActiveSubscription={
                    hasActiveSubscription && plan.subscriberScope !== SUBSCRIBER_SCOPE.User
                  }
                  onSubscribe={() => setSubscribingTo(plan)}
                />
              ))}
            </div>
          )}
        </div>
      </Card>

      <MembersCard plans={plans} organizationId={organizationScope} />

      {plans?.length ? (
        <DiscountCodeCard plans={plans} organizationId={organizationScope} />
      ) : null}

      {currentSubscription && (
        <OverageTermsSection subscription={currentSubscription} organizationId={organizationScope} />
      )}

      {currentSubscription && (
        <CreditsCard subscription={currentSubscription} organizationId={organizationScope} />
      )}

      {(isEntitled || memberPlans.length > 0) && (
        <UsageSection
          plan={isEntitled ? currentPlan : undefined}
          memberPlans={memberPlans}
          organizationId={organizationScope}
        />
      )}

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
              const perPerson = subscribingTo.subscriberScope === SUBSCRIBER_SCOPE.User;

              toast({
                title: t("SUBSCRIPTION_SIMULATION_PAGE.CHECKOUT_READY"),
                description: perPerson
                  ? t("SUBSCRIPTION_SIMULATION_PAGE.PAY_TO_ACTIVATE_IT_THE")
                  : t("SUBSCRIPTION_SIMULATION_PAGE.OPEN_THE_CHECKOUT_LINK_FROM"),
                // A per-person subscription is listed at the foot of the page, under the whole
                // catalogue, so the link is put in front of the subscriber instead. The
                // organization's own sits in the card at the top, where they already are.
                action: perPerson ? (
                  <ToastAction altText={t("SUBSCRIPTION_SIMULATION_PAGE.OPEN_CHECKOUT")} asChild>
                    <a href={checkoutUrl} target="_blank" rel="noreferrer">
                      {t("SUBSCRIPTION_SIMULATION_PAGE.OPEN_CHECKOUT")}
                    </a>
                  </ToastAction>
                ) : undefined,
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
          // current is always the organization's own, and a plan change cannot cross to a plan for
          // each person — the server refuses it, so it is not offered.
          plans={(plans ?? []).filter(
            (plan) => (plan.subscriberScope ?? SUBSCRIBER_SCOPE.Organization) === SUBSCRIBER_SCOPE.Organization,
          )}
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
