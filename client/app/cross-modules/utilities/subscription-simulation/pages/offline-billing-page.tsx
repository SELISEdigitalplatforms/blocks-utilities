import { useState } from "react";
import { Landmark, Loader2 } from "lucide-react";
import { useSearchParams } from "react-router";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
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
} from "../../subscription/constants/subscription.constants";
import { SubscriptionPlanPageHeader } from "../../subscription/components/subscription-plan-page-header";
import { useSubscriptionPlans } from "../../subscription/hooks/use-subscription-plans";
import { SUBSCRIBER_SCOPE } from "../../subscription/models/subscription-plan.model";
import { formatPrice, toMajorUnits } from "../../subscription/utilities/subscription-format";
import {
  billingProfileGapOf,
  subscriptionApiFailure,
  type BillingProfileGap,
} from "../../subscription/utilities/subscription-api-failure";
import { BillingProfileIncompleteNotice } from "../components/billing-profile-incomplete-notice";
import { BilledByInvoiceBadge, OfflinePricingFields } from "../components/offline-pricing-fields";
import { SubscriptionStatusBadge } from "../components/subscription-status-badge";
import { detectBrowserTimeZone } from "../constants/subscription-simulation.constants";
import { useCurrentSimulatedSubscription } from "../hooks/use-current-simulated-subscription";
import {
  useActivateOfflineSubscription,
  useRepriceOfflineSubscription,
} from "../hooks/use-offline-billing";
import {
  isOfflineSubscription,
  type OfflinePricingMode,
  type SimulatedSubscription,
} from "../models/subscription-simulation.model";
import { describeOfflinePricing, parseOfflinePricing } from "../utilities/offline-pricing";

const MODULE = "utilities.subscription-simulation";

// The statuses the server re-prices: a live subscription, with a next renewal to install it at.
const LIVE_STATUSES = new Set(["Trialing", "Active", "PastDue"]);

/**
 * A tenant admin activating and re-pricing subscriptions for organizations that pay by bank
 * transfer (spec 006). Never the console's own organization: an offline contract is always for a
 * customer, so nothing below renders until one is named.
 */
export const OfflineBillingPage = () => {
  const { t } = useT(MODULE);
  const [searchParams, setSearchParams] = useSearchParams();
  const organizationId = searchParams.get(ORGANIZATION_QUERY_PARAM) ?? undefined;
  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title={t("OFFLINE_BILLING_PAGE.TITLE")}
        description={t("OFFLINE_BILLING_PAGE.DESCRIPTION")}
        icon={<Landmark className="h-6 w-6" />}
      />

      <Card className="rounded-xl p-0">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
          <h2 className="font-semibold">{t("OFFLINE_BILLING_PAGE.ORGANIZATION")}</h2>
          <Select
            value={organizationId ?? ""}
            onValueChange={(value) => setSearchParams({ [ORGANIZATION_QUERY_PARAM]: value })}
          >
            <SelectTrigger className="sm:w-64" aria-label={t("OFFLINE_BILLING_PAGE.ORGANIZATION")}>
              <SelectValue placeholder={t("OFFLINE_BILLING_PAGE.CHOOSE_ORGANIZATION")} />
            </SelectTrigger>
            <SelectContent>
              {(organizationsData?.organizations ?? []).map((organization) => (
                <SelectItem key={organization.itemId} value={organization.itemId}>
                  {organization.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="p-4 sm:p-5">
          {organizationId ? (
            <OrganizationOfflineBilling key={organizationId} organizationId={organizationId} />
          ) : (
            <p className="text-sm text-muted-foreground">{t("OFFLINE_BILLING_PAGE.CHOOSE_ORGANIZATION_HINT")}</p>
          )}
        </div>
      </Card>
    </main>
  );
};

const OrganizationOfflineBilling = ({ organizationId }: { organizationId: string }) => {
  const { t } = useT(MODULE);
  const current = useCurrentSimulatedSubscription(organizationId);

  if (current.isLoading) {
    return <Skeleton className="h-40 w-full rounded-xl" />;
  }

  if (current.isError) {
    return (
      <p className="text-sm text-destructive">
        {current.error instanceof Error ? current.error.message : t("OFFLINE_BILLING_PAGE.LOAD_FAILED")}
      </p>
    );
  }

  const subscription = current.data;
  if (!subscription) {
    return <ActivationForm organizationId={organizationId} />;
  }

  const offline = isOfflineSubscription(subscription);
  return (
    <div className="space-y-4">
      <div className="rounded-md border p-3" data-testid="offline-billing-current">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-semibold">{subscription.planName}</span>
          <SubscriptionStatusBadge status={subscription.status} />
          {offline && <BilledByInvoiceBadge />}
        </div>
        {offline && (
          <p className="mt-1 text-xs text-muted-foreground">
            {describeOfflinePricing(t, subscription.offlinePricingMode, subscription.contractAmountMinor, subscription.currencyCode)}
            {subscription.pendingOfflinePricingMode &&
              ` · ${t("OFFLINE_PRICING.FROM_NEXT_RENEWAL", {
                pricing: describeOfflinePricing(t, subscription.pendingOfflinePricingMode, subscription.pendingContractAmountMinor, subscription.currencyCode),
              })}`}
          </p>
        )}
      </div>

      {!offline ? (
        <p className="text-sm text-muted-foreground">{t("OFFLINE_BILLING_PAGE.PAYS_BY_CARD")}</p>
      ) : LIVE_STATUSES.has(subscription.status) ? (
        <RepricingForm organizationId={organizationId} subscription={subscription} />
      ) : null}
    </div>
  );
};

const ActivationForm = ({ organizationId }: { organizationId: string }) => {
  const { t } = useT(MODULE);
  const activate = useActivateOfflineSubscription();
  const { data: allPlans, isLoading } = useSubscriptionPlans(organizationId);
  // An offline contract is the organization's own subscription; a plan sold to each person is not one.
  const plans = (allPlans ?? []).filter(
    (plan) =>
      (plan.subscriberScope ?? SUBSCRIBER_SCOPE.Organization) === SUBSCRIBER_SCOPE.Organization &&
      plan.prices.length > 0,
  );

  const [planCode, setPlanCode] = useState("");
  const [priceId, setPriceId] = useState("");
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [timeZoneId, setTimeZoneId] = useState(detectBrowserTimeZone);
  const [discountCode, setDiscountCode] = useState("");
  const [mode, setMode] = useState<OfflinePricingMode>("Catalogue");
  const [contractAmount, setContractAmount] = useState("");
  const [formError, setFormError] = useState<string | null>(null);
  const [profileGap, setProfileGap] = useState<BillingProfileGap | null>(null);

  const plan = plans.find((candidate) => candidate.code === planCode);
  const price = plan?.prices.find((candidate) => candidate.priceId === priceId);

  const choosePlan = (code: string) => {
    const chosen = plans.find((candidate) => candidate.code === code);
    setPlanCode(code);
    setPriceId(chosen?.prices[0]?.priceId ?? "");
    setQuantities(
      Object.fromEntries((chosen?.quantityItems ?? []).map((item) => [item.itemKey, String(item.defaultQuantity)])),
    );
  };

  const submit = async () => {
    if (!plan || !price) {
      setFormError(t("OFFLINE_BILLING_PAGE.CHOOSE_PLAN_AND_PRICE"));
      return;
    }

    const pricing = parseOfflinePricing(mode, contractAmount, price.currencyCode);
    if (!pricing.valid) {
      setFormError(t(pricing.errorKey));
      return;
    }

    setFormError(null);
    setProfileGap(null);
    try {
      await activate.mutateAsync({
        organizationId,
        planCode: plan.code,
        priceId: price.priceId,
        // The server checks each against the plan's bounds and answers with the field at fault.
        quantities: plan.quantityItems.map((item) => ({
          itemKey: item.itemKey,
          quantity: Number(quantities[item.itemKey] ?? item.defaultQuantity),
        })),
        timeZoneId: timeZoneId.trim() || "UTC",
        discountCode: discountCode.trim() || undefined,
        pricing: pricing.pricing,
      });
      toast({ variant: "success", title: t("OFFLINE_BILLING_PAGE.ACTIVATED") });
    } catch (error) {
      const gap = billingProfileGapOf(subscriptionApiFailure(error));
      setProfileGap(gap);
      setFormError(gap ? null : error instanceof Error ? error.message : t("SUBSCRIPTION_SIMULATION_SERVICE.OFFLINE_ACTIVATION_FAILED"));
    }
  };

  if (isLoading) {
    return <Skeleton className="h-40 w-full rounded-xl" />;
  }

  return (
    <div className="max-w-xl space-y-4" data-testid="offline-activation-form">
      <p className="text-sm text-muted-foreground">{t("OFFLINE_BILLING_PAGE.NO_SUBSCRIPTION")}</p>

      <div className="space-y-1.5">
        <Label htmlFor="offline-plan">{t("OFFLINE_BILLING_PAGE.PLAN")}</Label>
        <Select value={planCode} onValueChange={choosePlan}>
          <SelectTrigger id="offline-plan">
            <SelectValue placeholder={t("OFFLINE_BILLING_PAGE.CHOOSE_PLAN")} />
          </SelectTrigger>
          <SelectContent>
            {plans.map((candidate) => (
              <SelectItem key={candidate.planId} value={candidate.code}>
                {candidate.displayName}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {plan && (
        <>
          <div className="space-y-1.5">
            <Label htmlFor="offline-price">{t("OFFLINE_BILLING_PAGE.PRICE")}</Label>
            <Select value={priceId} onValueChange={setPriceId}>
              <SelectTrigger id="offline-price">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {plan.prices.map((candidate) => (
                  <SelectItem key={candidate.priceId} value={candidate.priceId}>
                    {candidate.displayPriceNote ?? formatPrice(candidate)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {plan.quantityItems.map((item) => (
            <div className="space-y-1.5" key={item.itemKey}>
              <Label htmlFor={`offline-quantity-${item.itemKey}`}>{item.unitLabel}</Label>
              <Input
                id={`offline-quantity-${item.itemKey}`}
                type="number"
                min={item.minQuantity}
                max={item.maxQuantity ?? undefined}
                value={quantities[item.itemKey] ?? ""}
                onChange={(event) => setQuantities((all) => ({ ...all, [item.itemKey]: event.target.value }))}
              />
            </div>
          ))}

          <div className="space-y-1.5">
            <Label htmlFor="offline-time-zone">{t("OFFLINE_BILLING_PAGE.TIME_ZONE")}</Label>
            <Input id="offline-time-zone" value={timeZoneId} onChange={(event) => setTimeZoneId(event.target.value)} />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="offline-discount">{t("OFFLINE_BILLING_PAGE.DISCOUNT_CODE")}</Label>
            <Input id="offline-discount" value={discountCode} onChange={(event) => setDiscountCode(event.target.value)} />
          </div>

          <OfflinePricingFields
            idPrefix="offline-activation"
            mode={mode}
            contractAmount={contractAmount}
            currencyCode={price?.currencyCode ?? ""}
            onModeChange={setMode}
            onContractAmountChange={setContractAmount}
          />
        </>
      )}

      {profileGap && <BillingProfileIncompleteNotice gap={profileGap} organizationId={organizationId} />}
      {formError && <p className="text-sm text-destructive">{formError}</p>}

      <Button onClick={submit} disabled={!plan || activate.isPending}>
        {activate.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
        {t("OFFLINE_BILLING_PAGE.ACTIVATE")}
      </Button>
    </div>
  );
};

const RepricingForm = ({
  organizationId,
  subscription,
}: {
  organizationId: string;
  subscription: SimulatedSubscription;
}) => {
  const { t } = useT(MODULE);
  const reprice = useRepriceOfflineSubscription();
  // Starts from what the next renewal would install, so saving unchanged changes nothing.
  const startingMode = subscription.pendingOfflinePricingMode ?? subscription.offlinePricingMode ?? "Catalogue";
  const startingAmount = subscription.pendingOfflinePricingMode
    ? subscription.pendingContractAmountMinor
    : subscription.contractAmountMinor;
  const [mode, setMode] = useState<OfflinePricingMode>(startingMode);
  const [contractAmount, setContractAmount] = useState(
    startingAmount != null ? String(toMajorUnits(startingAmount, subscription.currencyCode)) : "",
  );
  const [formError, setFormError] = useState<string | null>(null);

  const submit = async () => {
    const pricing = parseOfflinePricing(mode, contractAmount, subscription.currencyCode);
    if (!pricing.valid) {
      setFormError(t(pricing.errorKey));
      return;
    }

    setFormError(null);
    try {
      await reprice.mutateAsync({
        subscriptionId: subscription.subscriptionId,
        organizationId,
        pricing: pricing.pricing,
      });
      toast({ variant: "success", title: t("OFFLINE_BILLING_PAGE.REPRICED") });
    } catch (error) {
      setFormError(error instanceof Error ? error.message : t("SUBSCRIPTION_SIMULATION_SERVICE.OFFLINE_REPRICING_FAILED"));
    }
  };

  return (
    <div className="max-w-xl space-y-4" data-testid="offline-repricing-form">
      <div>
        <h3 className="font-medium">{t("OFFLINE_BILLING_PAGE.REPRICE")}</h3>
        <p className="text-xs text-muted-foreground">{t("OFFLINE_BILLING_PAGE.REPRICE_HINT")}</p>
      </div>
      <OfflinePricingFields
        idPrefix="offline-repricing"
        mode={mode}
        contractAmount={contractAmount}
        currencyCode={subscription.currencyCode}
        onModeChange={setMode}
        onContractAmountChange={setContractAmount}
      />
      {formError && <p className="text-sm text-destructive">{formError}</p>}
      <Button onClick={submit} disabled={reprice.isPending}>
        {reprice.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
        {t("OFFLINE_BILLING_PAGE.SCHEDULE_REPRICING")}
      </Button>
    </div>
  );
};
