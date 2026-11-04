import { useEffect, useMemo, useState } from "react";
import {
  AlertCircle,
  AlertTriangle,
  Archive,
  Check,
  Copy,
  Layers,
  Pencil,
  Plus,
} from "lucide-react";
import { Link, useParams } from "react-router";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardTitle } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { toast } from "@/hooks/use-toast";
import { ORGANIZATION_PAGE_SIZE } from "../constants/subscription.constants";
import { SUBSCRIBER_SCOPE } from "../models/subscription-plan.model";
import { describeQuantityBand } from "../utilities/quantity-discount-format";
import { PlanSummaryCard, type PlanSummaryData } from "../components/plan-summary-card";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useArchiveSubscriptionPrice } from "../hooks/use-archive-subscription-price";
import { useOrganizationScope, withOrganizationScope } from "../hooks/use-organization-scope";
import { useSubscriptionPlan } from "../hooks/use-subscription-plan";
import { describeEntitlementMeterMismatch } from "../utilities/plan-consistency";
import {
  formatEntitlementLimit,
  formatMeterAllowance,
  formatPrice,
} from "../utilities/subscription-format";

export const SubscriptionPlanDetailPage = () => {
  const { t } = useT("utilities.subscription.pages");
  const { itemId, planId } = useParams();
  const organizationScope = useOrganizationScope();
  const basePath = `/app/${itemId ?? ""}/subscription/plans`;
  const listPath = withOrganizationScope(basePath, organizationScope);

  const { data: plan, error, isError, isLoading } = useSubscriptionPlan(planId, organizationScope);

  const { mutateAsync: archivePrice } = useArchiveSubscriptionPrice();
  const [retiringPriceId, setRetiringPriceId] = useState<string | null>(null);

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });

  const planOrganizationId = plan?.organizationId;

  const organizationLabel = useMemo(() => {
    if (!planOrganizationId) {
      return t("PLAN_DETAIL_PAGE.TENANT_WIDE");
    }

    const match = organizationsData?.organizations.find(
      (organization) => organization.itemId === planOrganizationId,
    );

    return match?.name ?? planOrganizationId;
  }, [organizationsData, planOrganizationId, t]);

  if (isLoading) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <Skeleton className="h-32 w-full rounded-2xl" />
        <Skeleton className="h-48 w-full rounded-xl" />
      </main>
    );
  }

  if (isError || !plan) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <SubscriptionPlanPageHeader
          title={t("PLAN_DETAIL_PAGE.TITLE_NOT_LOADED")}
          description={t("PLAN_DETAIL_PAGE.NOT_LOADED")}
          backTo={listPath}
        />
        <Card className="flex min-h-56 flex-col items-center justify-center text-center">
          <span className="rounded-full bg-destructive/10 p-4 text-destructive">
            <AlertCircle className="h-7 w-7" />
          </span>
          <h3 className="mt-4 text-lg font-semibold">{t("PLAN_DETAIL_PAGE.NOT_FOUND")}</h3>
          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            {error instanceof Error
              ? error.message
              : t("PLAN_DETAIL_PAGE.NOT_FOUND_HINT")}
          </p>
        </Card>
      </main>
    );
  }

  const retirePrice = async (priceId: string) => {
    setRetiringPriceId(priceId);

    try {
      await archivePrice({ priceId, organizationId: plan.organizationId ?? undefined });
      toast({
        title: t("PLAN_DETAIL_PAGE.PRICE_RETIRED"),
        description: t("PLAN_DETAIL_PAGE.PRICE_RETIRED_DESCRIPTION"),
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: t("PLAN_DETAIL_PAGE.RETIRE_FAILED"),
        description: error instanceof Error ? error.message : t("PLAN_DETAIL_PAGE.TRY_AGAIN"),
      });
    } finally {
      setRetiringPriceId(null);
    }
  };

  // Absent reads as active, matching the card: a response cached before the field existed must
  // not put the whole page into its read-only state.
  const isArchived = plan.status === "Archived";

  const summary: PlanSummaryData = {
    displayName: plan.displayName,
    code: plan.code,
    organizationLabel,
    trialDurationKind: plan.trialDurationKind ?? null,
    trialDurationCount: plan.trialDurationCount ?? null,
    trialRequiresPaymentMethod: plan.trialRequiresPaymentMethod,
    subscriberScope: plan.subscriberScope === SUBSCRIBER_SCOPE.User ? "User" : "Organization",
    quantityItems: plan.quantityItems.map((item) => ({
      quantityDiscountTiers: item.quantityDiscountTiers,
      itemKey: item.itemKey,
      unitLabel: item.unitLabel,
      defaultQuantity: item.defaultQuantity,
      maxQuantity: item.maxQuantity,
      countsMembers: item.countsMembers,
    })),
    meters: plan.meters,
    entitlements: plan.entitlements,
    prices: plan.prices,
    // Optional on the response for plans stored before it carried them.
    trialGrants: plan.trialGrants ?? [],
    credits: plan.meters
      .filter((meter) => meter.topUp)
      .map((meter) => ({ meterKey: meter.meterKey, ...meter.topUp! })),
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title={plan.displayName}
        description={plan.description || t("PLAN_DETAIL_PAGE.NO_DESCRIPTION")}
        backTo={listPath}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" asChild>
              <Link
                to={withOrganizationScope(`${basePath}/create`, plan.organizationId)}
                state={{ duplicatePlan: plan }}
              >
                <Copy className="mr-2 h-4 w-4" />
                {t("PLAN_DETAIL_PAGE.DUPLICATE")}
              </Link>
            </Button>
            {/* One button, leading to the one editor, subscribed or not: an edit is the plan's
                next version and existing subscribers keep the version they bought.

                Gone entirely for an archived plan, rather than disabled: the server refuses every
                catalogue mutation on one, so a button that opened the editor would lead to a form
                whose every submission fails. Duplicate is the way forward, and it is the action
                left standing. */}
            {isArchived ? null : (
            <Button variant="outline" asChild>
              <Link
                to={withOrganizationScope(
                  `${basePath}/${encodeURIComponent(plan.planId)}/edit`,
                  plan.organizationId,
                )}
              >
                <Pencil className="mr-2 h-4 w-4" />
                {t("common:EDIT")}
              </Link>
            </Button>
            )}
          </div>
        }
      />

      {/* Stated before anything else on the page, because every action below it behaves
          differently and the two reassurances are the ones an author actually needs: nothing was
          lost, and nobody was cut off. */}
      {isArchived ? (
        <div
          role="status"
          className="flex flex-col gap-1 rounded-xl border border-dashed bg-muted/40 p-4"
        >
          <div className="flex items-center gap-2">
            <Archive className="h-4 w-4 text-muted-foreground" />
            <h2 className="font-semibold">{t("PLAN_DETAIL_PAGE.ARCHIVED_TITLE")}</h2>
          </div>
          <p className="text-sm text-muted-foreground">
            {t("PLAN_DETAIL_PAGE.ARCHIVED_EXPLANATION")}
          </p>
        </div>
      ) : null}

      {/* Display only, both directions: naming a predecessor never migrated a subscriber and
          never touched either plan's editability — see Plan.PredecessorPlanId server-side. */}
      {(plan.predecessorPlanId || plan.successorPlanId) && (
        <div className="flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
          {plan.predecessorPlanId && (
            <Link
              to={withOrganizationScope(
                `${basePath}/${encodeURIComponent(plan.predecessorPlanId)}`,
                plan.organizationId,
              )}
              className="underline-offset-4 hover:underline"
            >
              {t("PLAN_DETAIL_PAGE.REPLACES", {
                name: plan.predecessorDisplayName ?? t("PLAN_DETAIL_PAGE.A_RETIRED_PLAN"),
              })}
            </Link>
          )}
          {plan.successorPlanId && (
            <Link
              to={withOrganizationScope(
                `${basePath}/${encodeURIComponent(plan.successorPlanId)}`,
                plan.organizationId,
              )}
              className="underline-offset-4 hover:underline"
            >
              {t("PLAN_DETAIL_PAGE.REPLACED_BY", {
                name: plan.successorDisplayName ?? t("PLAN_DETAIL_PAGE.A_NEWER_PLAN"),
              })}
            </Link>
          )}
        </div>
      )}

      <div className="grid items-start gap-5 xl:grid-cols-[1fr_22rem]">
        <div className="space-y-5">
          {plan.quantityItems.length > 0 && (
            <Section title={t("PLAN_DETAIL_PAGE.SECTION_QUANTITY_ITEMS")}>
              <div className="grid gap-3 sm:grid-cols-2">
                {plan.quantityItems.map((item) => (
                  <Card key={item.itemKey} className="rounded-lg">
                    <p className="font-medium">{item.itemKey}</p>
                    <p className="text-sm text-muted-foreground">
                      {t("PLAN_DETAIL_PAGE.BY_DEFAULT", {
                        count: item.defaultQuantity,
                        quantity: item.defaultQuantity.toLocaleString(),
                        unit: item.unitLabel,
                      })}
                      {item.maxQuantity !== null &&
                        ` (${t("PLAN_DETAIL_PAGE.MAX", { max: item.maxQuantity.toLocaleString() })})`}
                    </p>
                    {/* An administrator checking the live catalogue before editing it needs to see
                        the bands here: they decide what every subscriber is charged, and were
                        previously visible only by reading the API response. */}
                    {item.quantityDiscountTiers?.length ? (
                      <ul className="mt-2 space-y-0.5 text-sm text-muted-foreground">
                        {item.quantityDiscountTiers.map((tier, index) => (
                          <li key={index}>{describeQuantityBand(tier, item.unitLabel)}</li>
                        ))}
                      </ul>
                    ) : null}
                  </Card>
                ))}
              </div>
            </Section>
          )}

          {plan.meters.length > 0 && (
            <Section title={t("PLAN_DETAIL_PAGE.SECTION_METERS")}>
              <div className="grid gap-3 sm:grid-cols-2">
                {plan.meters.map((meter) => (
                  <Card key={meter.meterKey} className="rounded-lg">
                    <p className="font-medium">{meter.displayName}</p>
                    {/* The value every POST /subscription-usage call has to carry. Showing only
                        the display name sent integrators to the API to find out what to send,
                        which is the errand this page exists to save them. */}
                    <IdentifierField label={t("PLAN_DETAIL_PAGE.METER_KEY")} value={meter.meterKey} />
                    <p className="mt-2 text-sm text-muted-foreground">
                      {formatMeterAllowance(meter)}
                    </p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {meter.resetPolicy === "Never"
                        ? t("PLAN_DETAIL_PAGE.RESET_NEVER")
                        : meter.resetPolicy === "CarryForward"
                          ? t("PLAN_DETAIL_PAGE.RESET_CARRY_FORWARD", {
                              cap: meter.carryForwardCap ?? 0,
                            })
                          : t("PLAN_DETAIL_PAGE.RESET_PERIODIC")}
                    </p>
                    {/* Optional-chained deliberately: a plan stored before the response
                        carried thresholds has no array here at all, and reading length off
                        that took the whole page down rather than hiding one line. */}
                    {meter.thresholdPercents?.length ? (
                      <p className="mt-2 text-xs text-muted-foreground">
                        {t("PLAN_DETAIL_PAGE.NOTIFIES_AT", {
                          percents: meter.thresholdPercents.map((percent) => `${percent}%`).join(", "),
                        })}
                      </p>
                    ) : null}
                  </Card>
                ))}
              </div>
            </Section>
          )}

          {plan.entitlements.length > 0 && (
            <Section title={t("PLAN_DETAIL_PAGE.SECTION_ENTITLEMENTS")}>
              <div className="grid gap-3 sm:grid-cols-2">
                {plan.entitlements.map((entitlement) => {
                  const mismatch = describeEntitlementMeterMismatch(entitlement, plan.meters);

                  return (
                    <Card key={entitlement.key} className="rounded-lg">
                      <p className="font-medium">{entitlement.key}</p>
                      <p className="mt-1 text-sm text-muted-foreground">
                        {formatEntitlementLimit(entitlement)}
                      </p>
                      {/* Which meter draws this down was equally invisible, so a limit could not
                          be traced to the counter that enforces it without reading the API. */}
                      {entitlement.meterKey && (
                        <IdentifierField
                          label={t("PLAN_DETAIL_PAGE.DRAWS_DOWN")}
                          value={entitlement.meterKey}
                        />
                      )}
                      {mismatch && (
                        <p className="mt-2 flex items-start gap-1.5 text-xs text-warning-700">
                          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                          {mismatch}
                        </p>
                      )}
                    </Card>
                  );
                })}
              </div>
            </Section>
          )}

          <Section title={t("PLAN_DETAIL_PAGE.SECTION_PRICES")}>
            {plan.prices.length === 0 ? (
              <Card className="flex flex-col items-center gap-3 rounded-lg py-8 text-center">
                <Layers className="h-6 w-6 text-muted-foreground" />
                <p className="text-sm text-muted-foreground">
                  {isArchived
                    ? t("PLAN_DETAIL_PAGE.NO_PRICE_ARCHIVED")
                    : t("PLAN_DETAIL_PAGE.NO_PRICE")}
                </p>
                {/* Offered only while the plan can still be sold. The server refuses a price on an
                    archived plan, so this button led to a form whose submission always failed. */}
                {isArchived ? null : (
                  <Button asChild size="sm">
                    <Link
                      to={withOrganizationScope(
                        `${basePath}/${encodeURIComponent(plan.planId)}/edit`,
                        plan.organizationId,
                      )}
                    >
                      <Plus className="mr-2 h-4 w-4" />
                      {t("PLAN_DETAIL_PAGE.ADD_PRICE")}
                    </Link>
                  </Button>
                )}
              </Card>
            ) : (
              <div className="grid gap-3 sm:grid-cols-2">
                {plan.prices.map((price) => (
                  <Card key={price.priceId} className="rounded-lg">
                    <p className="font-medium">{formatPrice(price)}</p>
                    {price.displayPriceNote && (
                      <p className="text-xs text-muted-foreground">{price.displayPriceNote}</p>
                    )}
                    {/* Subscribing names the price by this id, so leaving it off the page meant
                        nobody could subscribe without reading the API first. */}
                    <IdentifierField label={t("PLAN_DETAIL_PAGE.PRICE_ID")} value={price.priceId} />
                    <div className="mt-2 flex items-center justify-between gap-2">
                      <Badge variant="outline" className="font-normal">
                        {price.currencyCode}
                      </Badge>
                      {/* Kept here as well as in the editor: this is the list where somebody
                          reads the prices, so it is where they decide one should come off the
                          menu. One button, not a second copy of the form.

                          Absent once the plan is archived. Retiring a price is a change to what
                          the plan sells, and the plan sells nothing — the server refuses it, and
                          there is nothing left for it to achieve. */}
                      {isArchived ? null : (
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          disabled={retiringPriceId !== null}
                          onClick={() => retirePrice(price.priceId)}
                          className="h-7 text-xs text-muted-foreground hover:text-destructive"
                        >
                          {retiringPriceId === price.priceId
                            ? t("PLAN_DETAIL_PAGE.RETIRING")
                            : t("PLAN_DETAIL_PAGE.RETIRE")}
                        </Button>
                      )}
                    </div>
                  </Card>
                ))}
              </div>
            )}
          </Section>
        </div>

        <div className="xl:sticky xl:top-5">
          <PlanSummaryCard plan={summary} />
        </div>
      </div>
    </main>
  );
};

/**
 * An identifier the caller has to send verbatim, shown so it can be read and copied rather than
 * retyped. Monospaced because a slug that is retyped by eye is a slug that arrives with a typo.
 */
const IdentifierField = ({ label, value }: { label: string; value: string }) => {
  const { t } = useT("utilities.subscription.pages");
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) {
      return;
    }

    const timer = setTimeout(() => setCopied(false), 1500);

    return () => clearTimeout(timer);
  }, [copied]);

  return (
    <div className="mt-1 flex items-center gap-1.5">
      <span className="text-xs text-muted-foreground">{label}</span>
      <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs">{value}</code>
      <button
        type="button"
        // Optional-chained: clipboard access is unavailable outside a secure context, and a
        // page that throws there would take the whole plan view down over a convenience.
        onClick={() => {
          navigator.clipboard?.writeText(value).then(
            () => setCopied(true),
            () => setCopied(false),
          );
        }}
        aria-label={t("PLAN_DETAIL_PAGE.COPY_IDENTIFIER", { label, value })}
        className="text-muted-foreground transition hover:text-foreground"
      >
        {copied ? (
          <Check className="h-3.5 w-3.5 text-blocks-primary-600" />
        ) : (
          <Copy className="h-3.5 w-3.5" />
        )}
      </button>
    </div>
  );
};

const Section = ({ title, children }: { title: string; children: React.ReactNode }) => (
  <div className="space-y-3">
    <CardTitle className="text-base">{title}</CardTitle>
    {children}
  </div>
);
