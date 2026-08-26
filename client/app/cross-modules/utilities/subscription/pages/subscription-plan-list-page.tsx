import { useMemo, useState } from "react";
import { AlertCircle, Layers, Plus, RefreshCw, Search } from "lucide-react";
import { Link, useParams, useSearchParams } from "react-router";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
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
import {
  ORGANIZATION_PAGE_SIZE,
  ORGANIZATION_QUERY_PARAM,
  TENANT_WIDE_ORGANIZATION,
} from "../constants/subscription.constants";
import { PlanCard } from "../components/plan-card";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { withOrganizationScope } from "../hooks/use-organization-scope";
import { useSubscriptionPlans } from "../hooks/use-subscription-plans";

export const SubscriptionPlanListPage = () => {
  const { itemId } = useParams();
  const [search, setSearch] = useState("");
  const [searchParams, setSearchParams] = useSearchParams();
  const organizationScope = searchParams.get(ORGANIZATION_QUERY_PARAM) ?? undefined;
  const {
    data: plans,
    error,
    isError,
    isFetching,
    isLoading,
    refetch,
  } = useSubscriptionPlans(organizationScope);

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

  const filteredPlans = useMemo(() => {
    const term = search.trim().toLowerCase();

    if (!term) {
      return plans ?? [];
    }

    return (plans ?? []).filter(
      (plan) =>
        plan.displayName.toLowerCase().includes(term) ||
        plan.code.toLowerCase().includes(term),
    );
  }, [plans, search]);

  const catalogueGroups = useMemo(() => {
    const groups = new Map<string, typeof filteredPlans>();
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

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title="Subscription plans"
        description="What your tenant sells — configure once here, then subscribe organizations to it from your own product."
        actions={
          <>
            <Button variant="outline" asChild>
              <Link to={withOrganizationScope(`/app/${itemId ?? ""}/subscription/discounts`, organizationScope)}>
                Discounts
              </Link>
            </Button>
            <Button
              variant="outline"
              className="bg-background/80"
              onClick={() => refetch()}
              disabled={isFetching}
            >
              <RefreshCw className={`mr-2 h-4 w-4 ${isFetching ? "animate-spin" : ""}`} />
              Refresh
            </Button>
            <Button asChild>
              <Link to={withOrganizationScope(`${basePath}/create`, organizationScope)}>
                <Plus className="mr-2 h-4 w-4" />
                Create plan
              </Link>
            </Button>
          </>
        }
      />

      <Card className="rounded-xl p-0">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
          <div>
            <h2 className="font-semibold">Plan catalogue</h2>
            <p className="text-xs text-muted-foreground">
              {organizationScope
                ? "Showing this organization's own plans alongside the tenant-wide ones."
                : "Showing tenant-wide plans. Choose an organization to see plans scoped to it."}
            </p>
          </div>

          <div className="flex w-full flex-col gap-2 sm:w-auto sm:flex-row">
            <Select
              value={organizationScope ?? TENANT_WIDE_ORGANIZATION}
              onValueChange={(value) =>
                setSearchParams(
                  value === TENANT_WIDE_ORGANIZATION
                    ? {}
                    : { [ORGANIZATION_QUERY_PARAM]: value },
                )
              }
            >
              <SelectTrigger className="sm:w-56" aria-label="Organization">
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

            <div className="relative min-w-0 sm:w-64">
              <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
              <Input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search plan name or code"
                className="pl-9"
                aria-label="Search subscription plans"
              />
            </div>
          </div>
        </div>

        <div className="p-4 sm:p-5">
          {isLoading ? (
            <div
              className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3"
              aria-label="Loading plans"
            >
              {Array.from({ length: 3 }, (_, index) => (
                <Skeleton key={index} className="h-40 w-full rounded-xl" />
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
              <h3 className="mt-4 text-lg font-semibold">
                {plans?.length ? "No plans match this search" : "No subscription plan yet"}
              </h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">
                {plans?.length
                  ? "Try a different name or code."
                  : "Create your first plan to start selling subscriptions."}
              </p>
              {!plans?.length && (
                <Button asChild className="mt-5">
                  <Link to={withOrganizationScope(`${basePath}/create`, organizationScope)}>
                    <Plus className="mr-2 h-4 w-4" />
                    Create plan
                  </Link>
                </Button>
              )}
            </div>
          ) : (
            <div className="space-y-4">
              {catalogueGroups.map((group) => (
                <section key={group.key} className={group.familyCode ? "rounded-xl border p-4" : ""}>
                  {group.familyCode && (
                    <div className="mb-3">
                      <h3 className="font-semibold">{group.levels[0]?.displayName}</h3>
                      <p className="text-xs text-muted-foreground">{group.familyCode} · {group.levels.length} levels</p>
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
                      />
                    ))}
                  </div>
                </section>
              ))}
            </div>
          )}
        </div>
      </Card>
    </main>
  );
};
