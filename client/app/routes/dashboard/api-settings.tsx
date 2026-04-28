import { useCallback, useMemo, useState } from "react";
import { ExternalLink, BookOpen } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { useProjectStore } from "@/store/useProjectStore";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { ServiceGroupCard } from "@blocks-idp/api-settings/components/service-group-card";
import { BulkActionBar } from "@blocks-idp/api-settings/components/bulk-action-bar";
import {
  useGetApiEndpoints,
  useUpdateApiEndpoint,
  useBulkUpdateApiEndpoints,
} from "@blocks-idp/api-settings/hooks/use-api-settings";
import { IApiEndpoint } from "@blocks-idp/api-settings/models/api-endpoint.model";

/** ─── Loading skeleton ──────────────────────────────────────────────────────── */
const ServiceGroupSkeleton = () => (
  <div className="rounded-lg border border-border bg-card p-4">
    <div className="flex items-center gap-3">
      <Skeleton className="h-5 w-5 rounded" />
      <Skeleton className="h-6 w-6 rounded" />
      <div className="flex-1 space-y-2">
        <Skeleton className="h-5 w-40" />
        <Skeleton className="h-4 w-64" />
      </div>
      <Skeleton className="h-6 w-24 rounded" />
      <Skeleton className="h-8 w-28 rounded" />
    </div>
  </div>
);

/** ─── Page component ────────────────────────────────────────────────────────── */
export default function ApiSettingsPage() {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetApiEndpoints({ projectKey: tenantId, page: 0, pageSize: 100 });
  const { mutateAsync: updateEndpoint } = useUpdateApiEndpoint();
  const { mutateAsync: bulkUpdate } = useBulkUpdateApiEndpoints();

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  const endpoints = data?.data ?? [];

  // Group endpoints: service → controller (nested)
  const serviceGroups = useMemo(() => {
    const byService: Record<string, Record<string, IApiEndpoint[]>> = {};
    for (const ep of endpoints) {
      const svc = ep.service || "unknown";
      const ctrl = ep.controller || svc;
      ((byService[svc] ??= {})[ctrl] ??= []).push(ep);
    }
    return Object.entries(byService)
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([svc, controllers]) => {
        const firstEp = Object.values(controllers)[0]?.[0];
        const baseUrl = firstEp?.baseUrl || "";
        const version = firstEp?.version || "v1";
        return {
          service: svc,
          baseUrl,
          version,
          swaggerJsonUrl: `${baseUrl}/${svc}/${version}/swagger/${version}/swagger.json`,
          swaggerUiUrl: `${baseUrl}/${svc}/${version}/swagger/index.html`,
          controllers: Object.entries(controllers)
            .sort(([a], [b]) => a.localeCompare(b))
            .map(([ctrl, eps]) => [
              ctrl,
              eps.sort((a, b) => {
                // Sort by method first (GET, POST, PUT, etc.), then by controller
                const methodOrder: Record<string, number> = { GET: 0, POST: 1, PUT: 2, PATCH: 3, DELETE: 4 };
                const aMethodKey = a.method?.toUpperCase?.() || "";
                const bMethodKey = b.method?.toUpperCase?.() || "";
                const aMethod = methodOrder[aMethodKey] ?? 999;
                const bMethod = methodOrder[bMethodKey] ?? 999;
                if (aMethod !== bMethod) return aMethod - bMethod;

                // Sort by controller for stable sorting
                const aPath = a.controller || a.method || "";
                const bPath = b.controller || b.method || "";
                return aPath.localeCompare(bPath);
              }),
            ]) as [string, IApiEndpoint[]][],
        };
      });
  }, [endpoints]);

  // ── Selection handlers ──────────────────────────────────────────────────────
  const handleSelectEndpoint = useCallback((id: string, checked: boolean) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      checked ? next.add(id) : next.delete(id);
      return next;
    });
  }, []);

  const handleSelectGroup = useCallback((ids: string[], checked: boolean) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      ids.forEach((id) => (checked ? next.add(id) : next.delete(id)));
      return next;
    });
  }, []);

  const clearSelection = useCallback(() => setSelectedIds(new Set()), []);

  // ── Toggle handlers ────────────────────────────────────────────────────────
  const handleToggleMfa = useCallback(
    async (ep: IApiEndpoint, value: boolean) => {
      try {
        const result = await updateEndpoint({
          projectKey: tenantId,
          itemId: ep.itemId,
          service: ep.service,
          method: ep.method,
          controller: ep.controller,
          description: ep.description,
          isMFARequired: value,
          mfaType: ep.mfaType,
          isCaptchaRequired: ep.isCaptchaRequired,
          captchaProvider: ep.captchaProvider,
        });
        if (!result.isSuccess) {
          throw new Error(result.errors?.join(", ") || "Failed to update MFA setting");
        }
        showSuccessToast({ description: `MFA ${value ? "enabled" : "disabled"} for /${ep.controller}/${ep.method.charAt(0).toUpperCase() + ep.method.slice(1)}` });
      } catch (error) {
        showErrorToast({ errors: error instanceof Error ? error.message : "Failed to update MFA setting" });
      }
    },
    [tenantId, updateEndpoint],
  );

  const handleToggleCaptcha = useCallback(
    async (ep: IApiEndpoint, value: boolean) => {
      try {
        const result = await updateEndpoint({
          projectKey: tenantId,
          itemId: ep.itemId,
          service: ep.service,
          method: ep.method,
          controller: ep.controller,
          description: ep.description,
          isCaptchaRequired: value,
          captchaProvider: ep.captchaProvider,
          isMFARequired: ep.isMFARequired,
          mfaType: ep.mfaType,
        });
        if (!result.isSuccess) {
          throw new Error(result.errors?.join(", ") || "Failed to update Captcha setting");
        }
        showSuccessToast({ description: `Captcha ${value ? "enabled" : "disabled"} for /${ep.controller}/${ep.method.charAt(0).toUpperCase() + ep.method.slice(1)}` });
      } catch (error) {
        showErrorToast({ errors: error instanceof Error ? error.message : "Failed to update Captcha setting" });
      }
    },
    [tenantId, updateEndpoint],
  );

  // ── Bulk handlers (group presets) ─────────────────────────────────────────
  const handleBulkGroupMfa = useCallback(
    async (ids: string[], value: boolean) => {
      try {
        // Preserve current Captcha state when toggling MFA
        const groupEndpoints = endpoints.filter((ep) => ids.includes(ep.itemId));
        const captchaState =
          groupEndpoints.length > 0
            ? groupEndpoints.every((ep) => ep.isCaptchaRequired)
              ? true
              : groupEndpoints.some((ep) => ep.isCaptchaRequired)
                ? false // default to false if mixed states
                : false
            : false;

        const result = await bulkUpdate({
          projectKey: tenantId,
          itemIds: ids,
          isMFARequired: value,
          isCaptchaRequired: captchaState,
          disableAll: false,
        });
        if (!result.isSuccess) {
          throw new Error(result.errors?.join(", ") || "Failed to bulk update MFA");
        }
        showSuccessToast({ description: `MFA ${value ? "enabled" : "disabled"} for ${ids.length} endpoints` });
      } catch (error) {
        showErrorToast({ errors: error instanceof Error ? error.message : "Failed to bulk update MFA" });
      }
    },
    [tenantId, endpoints, bulkUpdate],
  );

  const handleBulkGroupCaptcha = useCallback(
    async (ids: string[], value: boolean) => {
      try {
        // Preserve current MFA state when toggling Captcha
        const groupEndpoints = endpoints.filter((ep) => ids.includes(ep.itemId));
        const mfaState =
          groupEndpoints.length > 0
            ? groupEndpoints.every((ep) => ep.isMFARequired)
              ? true
              : groupEndpoints.some((ep) => ep.isMFARequired)
                ? false // default to false if mixed states
                : false
            : false;

        const result = await bulkUpdate({
          projectKey: tenantId,
          itemIds: ids,
          isCaptchaRequired: value,
          isMFARequired: mfaState,
          disableAll: false,
        });
        if (!result.isSuccess) {
          throw new Error(result.errors?.join(", ") || "Failed to bulk update Captcha");
        }
        showSuccessToast({ description: `Captcha ${value ? "enabled" : "disabled"} for ${ids.length} endpoints` });
      } catch (error) {
        showErrorToast({ errors: error instanceof Error ? error.message : "Failed to bulk update Captcha" });
      }
    },
    [tenantId, endpoints, bulkUpdate],
  );

  const handleBulkGroupDisableAll = useCallback(
    async (ids: string[]) => {
      try {
        const result = await bulkUpdate({ projectKey: tenantId, itemIds: ids, isMFARequired: false, isCaptchaRequired: false, disableAll: true });
        if (!result.isSuccess) {
          throw new Error(result.errors?.join(", ") || "Failed to disable security features");
        }
        showSuccessToast({ description: `All security features disabled for ${ids.length} endpoints` });
      } catch (error) {
        showErrorToast({ errors: error instanceof Error ? error.message : "Failed to disable security features" });
      }
    },
    [tenantId, bulkUpdate],
  );

  // ── Bulk bar actions ───────────────────────────────────────────────────────
  const selectedArray = useMemo(() => Array.from(selectedIds), [selectedIds]);

  const handleBulkMfa = useCallback(async () => {
    try {
      // Preserve current Captcha state when enabling MFA
      const selectedEndpoints = endpoints.filter((ep) => selectedArray.includes(ep.itemId));
      const captchaState =
        selectedEndpoints.length > 0
          ? selectedEndpoints.every((ep) => ep.isCaptchaRequired)
            ? true
            : selectedEndpoints.some((ep) => ep.isCaptchaRequired)
              ? false // default to false if mixed states
              : false
          : false;

      const result = await bulkUpdate({
        projectKey: tenantId,
        itemIds: selectedArray,
        isMFARequired: true,
        isCaptchaRequired: captchaState,
        disableAll: false,
      });
      if (!result.isSuccess) {
        throw new Error(result.errors?.join(", ") || "Failed to enable MFA");
      }
      showSuccessToast({ description: `MFA enabled for ${selectedArray.length} endpoints` });
      clearSelection();
    } catch (error) {
      showErrorToast({ errors: error instanceof Error ? error.message : "Failed to enable MFA" });
    }
  }, [tenantId, endpoints, selectedArray, bulkUpdate, clearSelection]);

  const handleBulkCaptcha = useCallback(async () => {
    try {
      // Preserve current MFA state when enabling Captcha
      const selectedEndpoints = endpoints.filter((ep) => selectedArray.includes(ep.itemId));
      const mfaState =
        selectedEndpoints.length > 0
          ? selectedEndpoints.every((ep) => ep.isMFARequired)
            ? true
            : selectedEndpoints.some((ep) => ep.isMFARequired)
              ? false // default to false if mixed states
              : false
          : false;

      const result = await bulkUpdate({
        projectKey: tenantId,
        itemIds: selectedArray,
        isCaptchaRequired: true,
        isMFARequired: mfaState,
        disableAll: false,
      });
      if (!result.isSuccess) {
        throw new Error(result.errors?.join(", ") || "Failed to enable Captcha");
      }
      showSuccessToast({ description: `Captcha enabled for ${selectedArray.length} endpoints` });
      clearSelection();
    } catch (error) {
      showErrorToast({ errors: error instanceof Error ? error.message : "Failed to enable Captcha" });
    }
  }, [tenantId, endpoints, selectedArray, bulkUpdate, clearSelection]);

  return (
    <main className="flex flex-col gap-6 p-6 pb-24">
      {/* Header */}
      <div>
        <h1 className="text-xl font-semibold md:text-2xl">API Settings</h1>
        <p className="text-muted-foreground">
          Configure security policies for your API endpoints — enable MFA, Captcha, and manage access controls.
        </p>
      </div>

      {/* Service groups */}
      {isLoading ? (
        <div className="flex flex-col gap-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <ServiceGroupSkeleton key={i} />
          ))}
        </div>
      ) : serviceGroups.length === 0 ? (
        <div className="flex h-40 items-center justify-center rounded-lg border border-dashed bg-card text-muted-foreground">
          No API endpoints configured.
        </div>
      ) : (
        <div className="flex flex-col gap-8">
          {serviceGroups.map(({ service, swaggerJsonUrl, swaggerUiUrl, controllers }) => (
            <div key={service} className="flex flex-col gap-3">
              {/* Service section header */}
              <div className="flex items-start justify-between gap-4">
                <div className="min-w-0">
                  <h2 className="text-lg font-bold capitalize">{service}</h2>
                  <a
                    href={swaggerJsonUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1 text-[11px] text-muted-foreground underline-offset-2 hover:text-primary hover:underline"
                    title={swaggerJsonUrl}
                  >
                    <span className="truncate">{swaggerJsonUrl}</span>
                    <ExternalLink className="h-3 w-3 shrink-0" />
                  </a>
                </div>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => window.open(swaggerUiUrl, "_blank")}
                  className="shrink-0 gap-1.5"
                >
                  <BookOpen className="h-3.5 w-3.5" />
                  <span>API Docs</span>
                </Button>
              </div>
              {/* Controller cards */}
              <div className="flex flex-col gap-3">
                {controllers.map(([controller, eps]) => (
                  <ServiceGroupCard
                    key={controller}
                    controller={controller}
                    endpoints={eps}
                    selectedIds={selectedIds}
                    onSelectEndpoint={handleSelectEndpoint}
                    onSelectGroup={handleSelectGroup}
                    onToggleMfa={handleToggleMfa}
                    onToggleCaptcha={handleToggleCaptcha}
                    onBulkGroupMfa={handleBulkGroupMfa}
                    onBulkGroupCaptcha={handleBulkGroupCaptcha}
                  />
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Bulk action bar */}
      <BulkActionBar
        selectedCount={selectedIds.size}
        onEnableMfa={handleBulkMfa}
        onEnableCaptcha={handleBulkCaptcha}
        onClear={clearSelection}
      />
    </main>
  );
}
