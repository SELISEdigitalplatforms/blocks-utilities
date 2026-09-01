import { useMemo, useState } from "react";
import { AlertCircle, Archive, Layers, Plus, RefreshCw, Search, X } from "lucide-react";
import { Link, useParams, useSearchParams } from "react-router";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "@/hooks/use-toast";
import { Badge } from "@/components/ui-kits/badge/badge";
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
  countActiveFilters,
  DEFAULT_CATALOGUE_FILTERS,
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

  const catalogueGroups = useMemo(() => {
    const groups = new Map<string, SubscriptionPlan[]>();
    filteredPlans.forEach((plan) => {
      const key = plan.familyCode ? `family:${plan.familyCode}` : `plan:${plan.planId}`;
      groups.set(key, [...(groups.get(key) ?? []), plan]);
    });
    return [...groups.entries()].map(([key, levels]) => ({
      key,
      familyCode: key.startsWith("family:") ? key.slice(7) : null,
      levels: levels.sort((left, right) => (left.familyRank ?? 0) - (right.familyRank ?? 0)),
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

  const summaryCards = [
    { label: "Active plans", value: summary.active, icon: Layers },
    { label: "Archived plans", value: summary.archived, icon: Archive },
    { label: "Plan families", value: summary.families, icon: Layers },
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
        {summaryCards.map((card) => (
          <Card key={card.label} className="rounded-xl">
            <div className="flex items-center gap-3">
              <span className="rounded-lg bg-muted p-2 text-muted-foreground">
                <card.icon className="h-4 w-4" />
              </span>
              <div>
                <p className="text-2xl font-semibold leading-none">{card.value}</p>
                <p className="mt-1 text-xs text-muted-foreground">{card.label}</p>
              </div>
            </div>
          </Card>
        ))}
      </div>

      <Card className="rounded-xl p-0">
        <div className="space-y-3 border-b p-4 sm:p-5">
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
                    {tab}
                    {tab === "Archived" && summary.archived > 0
                      ? ` (${summary.archived})`
                      : ""}
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
                <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
                <Input
                  value={filters.search}
                  onChange={(event) => setFilters({ search: event.target.value })}
                  placeholder="Search plan name or code"
                  className="pl-9"
                  aria-label="Search subscription plans"
                />
              </div>
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-xs text-muted-foreground">
              {organizationScope
                ? "Showing this organization's own plans alongside the tenant-wide ones."
                : "Showing tenant-wide plans. Choose an organization to see plans scoped to it."}
            </p>

            {activeFilterCount > 0 ? (
              <div className="flex items-center gap-2">
                <Badge variant="secondary" className="font-normal">
                  {activeFilterCount} filter{activeFilterCount === 1 ? "" : "s"} active
                </Badge>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setFilters(DEFAULT_CATALOGUE_FILTERS)}
                >
                  <X className="mr-1 h-3 w-3" />
                  Clear filters
                </Button>
              </div>
            ) : null}
          </div>
        </div>

        <div className="p-4 sm:p-5">
          {isLoading ? (
            <div
              className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3"
              aria-label="Loading plans"
            >
              {Array.from({ length: 3 }, (_, index) => (
                <Skeleton key={index} className="h-56 w-full rounded-xl" />
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
                  onClick={() => setFilters(DEFAULT_CATALOGUE_FILTERS)}
                >
                  Clear filters
                </Button>
              ) : null}
            </div>
          ) : (
            <div className="space-y-4">
              {catalogueGroups.map((group) => (
                <section key={group.key} className={group.familyCode ? "rounded-xl border p-4" : ""}>
                  {group.familyCode && (
                    <div className="mb-3">
                      <h3 className="font-semibold">{group.levels[0]?.displayName}</h3>
                      <p className="text-xs text-muted-foreground">
                        {group.familyCode} · {group.levels.length} level
                        {group.levels.length === 1 ? "" : "s"}
                      </p>
                    </div>
                  )}
                  <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                    {group.levels.map((plan) => (
                      <PlanCard
                        key={plan.planId}
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
