import { useMemo, useState } from "react";
import {
  AlertCircle,
  Archive,
  CheckCircle2,
  Layers,
  Plus,
  RefreshCw,
  Search,
  X,
} from "lucide-react";
import { Link, useParams, useSearchParams } from "react-router";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "@/hooks/use-toast";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import {
  ORGANIZATION_PAGE_SIZE,
  ORGANIZATION_QUERY_PARAM,
  TENANT_WIDE_ORGANIZATION,
} from "../constants/subscription.constants";
import { ArchivePlanDialog } from "../components/archive-plan-dialog";
import { PlanCard } from "../components/plan-card";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { withOrganizationScope } from "../hooks/use-organization-scope";
import { useSubscriptionPlans } from "../hooks/use-subscription-plans";
import type {
  PlanCatalogueFilterName,
  SubscriptionPlan,
} from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";
import {
  applyCatalogueFilters,
  clearCatalogueFilters,
  countActiveFilters,
  PLAN_SORT_LABELS,
  PLAN_STATUS_TABS,
  readCatalogueFilters,
  summariseCatalogue,
  writeCatalogueFilters,
  type PlanCatalogueSort,
} from "../utilities/plan-catalogue-filters";

export const SubscriptionPlanListPage = () => {
  const { itemId } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const queryClient = useQueryClient();
  const organizationScope = searchParams.get(ORGANIZATION_QUERY_PARAM) ?? undefined;

  // Every control lives in the URL, so a reload and a shared link both reproduce the list the
  // sender was looking at. There is no second copy in component state to fall out of step with it.
  const filters = useMemo(() => readCatalogueFilters(searchParams), [searchParams]);

  const setFilters = (next: Partial<typeof filters>) =>
    setSearchParams(writeCatalogueFilters(searchParams, { ...filters, ...next }), {
      replace: true,
    });

  const [planToArchive, setPlanToArchive] = useState<SubscriptionPlan | null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);

  // Fetched once as All and filtered in the browser. Switching tabs is then instant and the
  // summary counts stay consistent with what the tabs show, rather than each tab knowing only its
  // own half of the catalogue.
  const {
    data: plans,
    error,
    isError,
    isFetching,
    isLoading,
    refetch,
  } = useSubscriptionPlans(organizationScope, "All");

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });

  const organizations = organizationsData?.organizations ?? [];

  const organizationName = useMemo(() => {
    const names = new Map(
      (organizationsData?.organizations ?? []).map((organization) => [
        organization.itemId,
        organization.name,
      ]),
    );

    return (organizationId: string | null) =>
      organizationId ? (names.get(organizationId) ?? organizationId) : "Tenant-wide";
  }, [organizationsData]);

  const basePath = `/app/${itemId ?? ""}/subscription/plans`;
  const createPath = withOrganizationScope(`${basePath}/create`, organizationScope);

  const summary = useMemo(() => summariseCatalogue(plans ?? []), [plans]);
  const filteredPlans = useMemo(
    () => applyCatalogueFilters(plans ?? [], filters),
    [plans, filters],
  );
  const activeFilterCount = countActiveFilters(filters);

  // Family-less plans are collected into a single "standalone" section rather than one section
  // per plan, so they share the same 3-column grid instead of stacking one full-width row each.
  // Sections otherwise appear in the order their first plan was encountered, which — since
  // filteredPlans is already sorted — keeps the catalogue's chosen order intact.
  const catalogueGroups = useMemo(() => {
    const families = new Map<string, SubscriptionPlan[]>();
    const standalone: SubscriptionPlan[] = [];
    const order: Array<{ key: string; familyCode: string | null }> = [];

    filteredPlans.forEach((plan) => {
      if (plan.familyCode) {
        if (!families.has(plan.familyCode)) {
          order.push({ key: `family:${plan.familyCode}`, familyCode: plan.familyCode });
        }
        families.set(plan.familyCode, [...(families.get(plan.familyCode) ?? []), plan]);

        return;
      }

      if (standalone.length === 0) {
        order.push({ key: "standalone", familyCode: null });
      }
      standalone.push(plan);
    });

    return order.map(({ key, familyCode }) => ({
      key,
      familyCode,
      levels: familyCode
        ? (families.get(familyCode) ?? []).sort(
            (left, right) => (left.familyRank ?? 0) - (right.familyRank ?? 0),
          )
        : standalone,
    }));
  }, [filteredPlans]);

  const archivePlan = async (plan: SubscriptionPlan) => {
    await subscriptionService.archivePlan(
      plan.planId,
      plan.organizationId ?? undefined,
    );

    // Invalidated rather than reloaded: the card leaves the Active tab as soon as the refetch
    // lands, and it is already present under Archived, so there is nothing to navigate to.
    await queryClient.invalidateQueries({ queryKey: ["subscription-plans"] });

    toast({
      variant: "success",
      title: "Plan archived",
      description: `${plan.displayName} is off the menu. Everyone already on it carries on unchanged.`,
    });
  };

  // Active and Archived double as the tab switcher: the number they show is exactly the one the
  // corresponding tab counts, so making them buttons removes a second, disconnected way of
  // reading the same fact. Plan families has no matching tab and stays a plain stat.
  const summaryCards = [
    {
      label: "Active plans",
      value: summary.active,
      icon: CheckCircle2,
      status: "Active" as const,
    },
    {
      label: "Archived plans",
      value: summary.archived,
      icon: Archive,
      status: "Archived" as const,
    },
    { label: "Plan families", value: summary.families, icon: Layers, status: null },
  ];

  const emptyStateCopy = () => {
    if (plans?.length === 0) {
      return {
        title: "No subscription plan yet",
        body: "Create your first plan to start selling subscriptions.",
      };
    }

    if (filters.status === "Archived") {
      return {
        title: "No archived plans",
        body: "Plans you take off the menu will be kept here, and stay readable.",
      };
    }

    if (activeFilterCount > 0) {
      return {
        title: "No plans match these filters",
        body: "Try a different name or code, or clear the filters.",
      };
    }

    return {
      title: "No active plans",
      body: "Every plan in this scope has been archived.",
    };
  };

  const empty = emptyStateCopy();

  const tabCount: Record<PlanCatalogueFilterName, number> = {
    Active: summary.active,
    Archived: summary.archived,
    All: summary.all,
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title="Subscription plans"
        description="What your tenant sells — configure once here, then subscribe organizations to it from your own product."
        actions={
          <>
            <Button
              variant="outline"
              size="icon"
              className="bg-background/80"
              onClick={() => refetch()}
              disabled={isFetching}
              aria-label="Refresh plans"
            >
              <RefreshCw className={`h-4 w-4 ${isFetching ? "animate-spin" : ""}`} />
            </Button>
            <Button asChild>
              <Link to={createPath}>
                <Plus className="mr-2 h-4 w-4" />
                Create plan
              </Link>
            </Button>
          </>
        }
      />

      <div className="grid gap-3 sm:grid-cols-3">
        {summaryCards.map((card) => {
          const isSelected = card.status !== null && filters.status === card.status;
          const content = (
            <div className="flex items-center gap-3.5">
              <span
                className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full transition-all duration-300 ${
                  isSelected
                    ? "bg-gradient-to-br from-blocks-primary-500 to-blocks-secondary-500 text-primary-foreground shadow-md shadow-blocks-primary-200"
                    : "bg-muted text-muted-foreground group-hover:bg-blocks-primary-50 group-hover:text-blocks-primary-600"
                }`}
              >
                <card.icon className="h-[18px] w-[18px]" />
              </span>
              <div>
                <p className="text-2xl font-bold leading-none tracking-tight tabular-nums">
                  {card.value}
                </p>
                <p className="mt-1.5 text-xs font-medium text-muted-foreground">
                  {card.label}
                </p>
              </div>
            </div>
          );

          return card.status ? (
            <Card
              key={card.label}
              className={`group rounded-2xl p-0 transition-all duration-300 ${
                isSelected
                  ? "border-blocks-primary-300 shadow-md shadow-blocks-primary-100/60"
                  : "hover:-translate-y-0.5 hover:border-blocks-primary-200 hover:shadow-lg hover:shadow-blocks-primary-100/40 motion-reduce:hover:translate-y-0"
              }`}
            >
              <button
                type="button"
                onClick={() => setFilters({ status: card.status })}
                aria-pressed={isSelected}
                className="w-full rounded-2xl px-5 py-4 text-left outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                {content}
              </button>
            </Card>
          ) : (
            <Card key={card.label} className="group rounded-2xl transition-all duration-300">
              {content}
            </Card>
          );
        })}
      </div>

      {/* No overflow-hidden here: the sticky filter bar inside is a child of this Card, and an
          overflow-hidden ancestor turns off position: sticky (it stops scrolling relative to the
          page and just sits wherever this box's own — non-scrolling — layout puts it). */}
      <Card className="min-w-0 rounded-2xl p-0 shadow-sm">
        <div className="sticky top-0 z-10 space-y-3 rounded-t-2xl border-b bg-card/85 p-4 backdrop-blur-md sm:p-5 supports-[backdrop-filter]:bg-card/70">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <Tabs
              value={filters.status}
              onValueChange={(value) =>
                setFilters({ status: value as PlanCatalogueFilterName })
              }
            >
              <TabsList>
                {PLAN_STATUS_TABS.map((tab) => (
                  <TabsTrigger key={tab} value={tab}>
                    {tab} ({tabCount[tab]})
                  </TabsTrigger>
                ))}
              </TabsList>
            </Tabs>

            <div className="flex w-full flex-col gap-2 sm:flex-row lg:w-auto">
              <Select
                value={organizationScope ?? TENANT_WIDE_ORGANIZATION}
                onValueChange={(value) => {
                  // The scope changes which catalogue is fetched, so the other filters are kept
                  // rather than reset: somebody comparing two organizations is still looking for
                  // the same thing in each.
                  const next = new URLSearchParams(searchParams);

                  if (value === TENANT_WIDE_ORGANIZATION) {
                    next.delete(ORGANIZATION_QUERY_PARAM);
                  } else {
                    next.set(ORGANIZATION_QUERY_PARAM, value);
                  }

                  setSearchParams(next, { replace: true });
                }}
              >
                <SelectTrigger className="sm:w-52" aria-label="Organization">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={TENANT_WIDE_ORGANIZATION}>Tenant-wide only</SelectItem>
                  {organizations.map((organization) => (
                    <SelectItem key={organization.itemId} value={organization.itemId}>
                      {organization.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <Select
                value={filters.sort}
                onValueChange={(value) => setFilters({ sort: value as PlanCatalogueSort })}
              >
                <SelectTrigger className="sm:w-48" aria-label="Sort plans">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {Object.entries(PLAN_SORT_LABELS).map(([value, label]) => (
                    <SelectItem key={value} value={value}>
                      {label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <div className="relative min-w-0 sm:w-64">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground transition-colors" />
                <Input
                  value={filters.search}
                  onChange={(event) => setFilters({ search: event.target.value })}
                  placeholder="Search name, code, family, or description"
                  className={`rounded-full transition-shadow focus-visible:shadow-md focus-visible:shadow-blocks-primary-100 ${
                    filters.search ? "px-9" : "pl-9"
                  }`}
                  aria-label="Search subscription plans"
                />
                {filters.search ? (
                  <button
                    type="button"
                    onClick={() => setFilters({ search: "" })}
                    aria-label="Clear search"
                    className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-muted-foreground outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    <X className="h-3.5 w-3.5" />
                  </button>
                ) : null}
              </div>
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <p className="text-xs text-muted-foreground">
                {organizationScope
                  ? "Showing this organization's own plans alongside the tenant-wide ones."
                  : "Showing tenant-wide plans. Choose an organization to see plans scoped to it."}
              </p>
              <p className="text-xs text-muted-foreground" role="status" aria-live="polite">
                Showing {filteredPlans.length} of {plans?.length ?? 0} plans
              </p>
            </div>

            {activeFilterCount > 0 ? (
              <div className="flex items-center gap-2">
                <span className="inline-flex items-center gap-1.5 rounded-full border border-blocks-primary-200 bg-blocks-primary-50 px-2.5 py-1 text-[11px] font-semibold tracking-wide text-blocks-primary-700">
                  {activeFilterCount} filter{activeFilterCount === 1 ? "" : "s"} active
                </span>
                <Button
                  variant="ghost"
                  size="sm"
                  className="rounded-full text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                  onClick={() => setFilters(clearCatalogueFilters(filters))}
                >
                  <X className="mr-1 h-3 w-3" />
                  Clear filters
                </Button>
              </div>
            ) : null}
          </div>
        </div>

        {isFetching && !isLoading ? (
          // A background refetch (after archiving, or a manual refresh) has nothing else to show
          // for it besides the spinning header icon, which sits well away from the grid the user
          // is actually looking at.
          <div className="h-0.5 overflow-hidden bg-muted">
            <div className="h-full w-1/3 animate-pulse bg-blocks-primary-500" />
          </div>
        ) : null}

        <div className={`min-w-0 p-4 sm:p-5 ${isFetching && !isLoading ? "opacity-60" : ""}`}>
          {isLoading ? (
            <div
              className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3"
              aria-label="Loading plans"
            >
              {Array.from({ length: 6 }, (_, index) => (
                <Skeleton
                  key={index}
                  className="h-56 w-full rounded-2xl"
                  style={{ animationDelay: `${index * 75}ms` }}
                />
              ))}
            </div>
          ) : isError ? (
            <div className="flex min-h-72 flex-col items-center justify-center text-center">
              <span className="rounded-full bg-destructive/10 p-4 text-destructive">
                <AlertCircle className="h-7 w-7" />
              </span>
              <h3 className="mt-4 text-lg font-semibold">Plans could not be loaded</h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">
                {error instanceof Error ? error.message : "Try loading the plan list again."}
              </p>
              <Button className="mt-5" variant="outline" onClick={() => refetch()}>
                Try again
              </Button>
            </div>
          ) : filteredPlans.length === 0 ? (
            <div className="flex min-h-72 flex-col items-center justify-center text-center">
              <span className="rounded-full bg-muted p-4 text-muted-foreground">
                <Layers className="h-7 w-7" />
              </span>
              <h3 className="mt-4 text-lg font-semibold">{empty.title}</h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">{empty.body}</p>
              {plans?.length === 0 ? (
                <Button asChild className="mt-5">
                  <Link to={createPath}>
                    <Plus className="mr-2 h-4 w-4" />
                    Create plan
                  </Link>
                </Button>
              ) : activeFilterCount > 0 ? (
                <Button
                  variant="outline"
                  className="mt-5"
                  onClick={() => setFilters(clearCatalogueFilters(filters))}
                >
                  Clear filters
                </Button>
              ) : null}
            </div>
          ) : (
            <div className="min-w-0 space-y-5">
              {catalogueGroups.map((group) => (
                <section
                  key={group.key}
                  className={
                    group.familyCode
                      ? "min-w-0 overflow-hidden rounded-2xl border border-blocks-secondary-100 bg-blocks-secondary-50/30 p-4 sm:p-5"
                      : "min-w-0"
                  }
                >
                  {group.familyCode && (
                    <div className="mb-4 flex min-w-0 items-center gap-2.5">
                      <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-blocks-secondary-400 to-blocks-secondary-600 text-primary-foreground shadow-sm">
                        <Layers className="h-4 w-4" />
                      </span>
                      <div className="min-w-0">
                        <h3 className="break-words font-semibold tracking-tight">
                          {group.familyCode}
                        </h3>
                        <p className="text-xs text-muted-foreground">
                          {group.levels.length} level{group.levels.length === 1 ? "" : "s"}
                        </p>
                      </div>
                    </div>
                  )}
                  <div className="grid min-w-0 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                    {group.levels.map((plan, index) => (
                      <div
                        key={plan.planId}
                        className="min-w-0 animate-in fade-in slide-in-from-bottom-2 fill-mode-both duration-500 motion-reduce:animate-none"
                        style={{ animationDelay: `${Math.min(index, 8) * 60}ms` }}
                      >
                        <PlanCard
                          plan={plan}
                          organizationLabel={organizationName(plan.organizationId)}
                          detailPath={withOrganizationScope(
                            `${basePath}/${encodeURIComponent(plan.planId)}`,
                            plan.organizationId,
                          )}
                          editPath={withOrganizationScope(
                            `${basePath}/${encodeURIComponent(plan.planId)}/edit`,
                            plan.organizationId,
                          )}
                          duplicatePath={withOrganizationScope(
                            `${basePath}/create`,
                            plan.organizationId,
                          )}
                          onArchive={(selected) => {
                            setPlanToArchive(selected);
                            setIsDialogOpen(true);
                          }}
                        />
                      </div>
                    ))}
                  </div>
                </section>
              ))}
            </div>
          )}
        </div>
      </Card>

      <ArchivePlanDialog
        plan={planToArchive}
        isOpen={isDialogOpen}
        onOpenChange={setIsDialogOpen}
        onConfirm={archivePlan}
      />
    </main>
  );
};
