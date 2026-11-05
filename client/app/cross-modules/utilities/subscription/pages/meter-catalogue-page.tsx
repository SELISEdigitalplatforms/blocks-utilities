import { useT } from "@seliseblocks/genesis-os";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { useState } from "react";
import { useParams } from "react-router";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { METER_AGGREGATION_OPTIONS } from "../constants/subscription.constants";
import { METER_CATALOGUE_QUERY_KEY, useMeterCatalogue } from "../hooks/use-meter-catalogue";
import { useOrganizationScope } from "../hooks/use-organization-scope";
import type { MeterCatalogueEntry } from "../models/meter-catalogue.model";
import { meterCatalogueService } from "../services/meter-catalogue.service";
import { METER_QUANTITY_MAX_SCALE } from "../utilities/meter-quantity";

const MODULE = "utilities.subscription.pages";

const AGGREGATION_NAMES = ["Sum", "Max", "Last"] as const;
const aggregationIndex = (name: string) => Math.max(0, AGGREGATION_NAMES.indexOf(name as (typeof AGGREGATION_NAMES)[number]));

interface Draft {
  meterKey: string;
  displayName: string;
  unitLabel: string;
  aggregation: number;
  quantityScale: string;
}

const emptyDraft = (): Draft => ({ meterKey: "", displayName: "", unitLabel: "", aggregation: 0, quantityScale: "0" });

const draftOf = (entry: MeterCatalogueEntry): Draft => ({
  meterKey: entry.meterKey,
  displayName: entry.displayName,
  unitLabel: entry.unitLabel,
  aggregation: aggregationIndex(entry.aggregation),
  quantityScale: String(entry.quantityScale),
});

/**
 * The meter catalogue (spec 005): the meters a tenant counts, named once so plans and credit
 * offers refer to the same thing. Archiving keeps the entry — a plan or an offer may still point
 * at it — and only refuses new offers.
 */
export const MeterCataloguePage = () => {
  const { t } = useT(MODULE);
  const { itemId } = useParams();
  const organizationId = useOrganizationScope();
  const queryClient = useQueryClient();
  const entries = useMeterCatalogue(organizationId);
  const [editing, setEditing] = useState<{ entry: MeterCatalogueEntry | null; draft: Draft } | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: [METER_CATALOGUE_QUERY_KEY] });

  const save = useMutation({
    mutationFn: ({ entry, draft }: { entry: MeterCatalogueEntry | null; draft: Draft }) =>
      entry
        ? meterCatalogueService.updateEntry(
            entry.meterKey,
            {
              displayName: draft.displayName.trim(),
              unitLabel: draft.unitLabel.trim(),
              aggregation: draft.aggregation,
              quantityScale: Number(draft.quantityScale),
              status: entry.status,
            },
            entry.organizationId ?? undefined,
          )
        : meterCatalogueService.createEntry({
            organizationId,
            meterKey: draft.meterKey.trim(),
            displayName: draft.displayName.trim(),
            unitLabel: draft.unitLabel.trim(),
            aggregation: draft.aggregation,
            quantityScale: Number(draft.quantityScale),
          }),
    onSuccess: async () => {
      setEditing(null);
      setProblem(null);
      await invalidate();
    },
    onError: (error) => setProblem(error instanceof Error ? error.message : t("METER_CATALOGUE_PAGE.SAVE_FAILED")),
  });

  const setStatus = useMutation({
    mutationFn: ({ entry, status }: { entry: MeterCatalogueEntry; status: MeterCatalogueEntry["status"] }) =>
      meterCatalogueService.updateEntry(
        entry.meterKey,
        {
          displayName: entry.displayName,
          unitLabel: entry.unitLabel,
          aggregation: aggregationIndex(entry.aggregation),
          quantityScale: entry.quantityScale,
          status,
        },
        entry.organizationId ?? undefined,
      ),
    onSuccess: invalidate,
  });

  const draft = editing?.draft;
  const update = (patch: Partial<Draft>) =>
    setEditing((current) => (current ? { ...current, draft: { ...current.draft, ...patch } } : current));

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title={t("METER_CATALOGUE_PAGE.TITLE")}
        description={t("METER_CATALOGUE_PAGE.DESCRIPTION")}
        backTo={`/app/${itemId ?? ""}/subscription/plans`}
        actions={
          <Button onClick={() => setEditing({ entry: null, draft: emptyDraft() })}>
            {t("METER_CATALOGUE_PAGE.NEW_METER")}
          </Button>
        }
      />

      <Card className="rounded-xl p-0">
        <div className="border-b p-4 font-semibold">{t("METER_CATALOGUE_PAGE.CATALOGUE")}</div>
        {entries.isLoading ? (
          <p className="p-4 text-sm text-muted-foreground">{t("METER_CATALOGUE_PAGE.LOADING")}</p>
        ) : entries.isError ? (
          <p className="p-4 text-sm text-destructive">
            {entries.error instanceof Error ? entries.error.message : t("METER_CATALOGUE_PAGE.LOAD_FAILED")}
          </p>
        ) : !entries.data?.length ? (
          <p className="p-4 text-sm text-muted-foreground">{t("METER_CATALOGUE_PAGE.EMPTY")}</p>
        ) : (
          <div className="divide-y">
            {entries.data.map((entry) => (
              <div key={entry.entryId} className="flex items-center justify-between gap-4 p-4">
                <div>
                  <p className="flex items-center gap-2 font-medium">
                    {entry.displayName} <code className="text-xs">{entry.meterKey}</code>
                    <Badge variant={entry.status === "Active" ? "default" : "outline"}>{entry.status}</Badge>
                    {entry.organizationId && <Badge variant="secondary">{t("METER_CATALOGUE_PAGE.OVERRIDE")}</Badge>}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {t("METER_CATALOGUE_PAGE.SUMMARY", {
                      unit: entry.unitLabel,
                      aggregation: entry.aggregation,
                      scale: entry.quantityScale,
                    })}
                  </p>
                </div>
                <div className="flex gap-2">
                  <Button size="sm" variant="outline" onClick={() => setEditing({ entry, draft: draftOf(entry) })}>
                    {t("common:EDIT")}
                  </Button>
                  <Button
                    size="sm"
                    variant="ghost"
                    disabled={setStatus.isPending}
                    onClick={() =>
                      setStatus.mutate({ entry, status: entry.status === "Active" ? "Archived" : "Active" })
                    }
                  >
                    {entry.status === "Active" ? t("METER_CATALOGUE_PAGE.ARCHIVE") : t("METER_CATALOGUE_PAGE.RESTORE")}
                  </Button>
                </div>
              </div>
            ))}
          </div>
        )}
      </Card>

      {editing && draft && (
        <Dialog open onOpenChange={(open) => !open && setEditing(null)}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>
                {editing.entry ? t("METER_CATALOGUE_PAGE.EDIT_METER") : t("METER_CATALOGUE_PAGE.NEW_METER")}
              </DialogTitle>
              <DialogDescription>{t("METER_CATALOGUE_PAGE.FORM_HINT")}</DialogDescription>
            </DialogHeader>
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1">
                <Label htmlFor="catalogue-key">{t("METER_CATALOGUE_PAGE.METER_KEY")}</Label>
                <Input
                  id="catalogue-key"
                  value={draft.meterKey}
                  disabled={Boolean(editing.entry)}
                  placeholder="ai-credits"
                  onChange={(event) => update({ meterKey: event.target.value })}
                />
              </div>
              <div className="space-y-1">
                <Label htmlFor="catalogue-name">{t("METER_CATALOGUE_PAGE.DISPLAY_NAME")}</Label>
                <Input id="catalogue-name" value={draft.displayName} onChange={(event) => update({ displayName: event.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="catalogue-unit">{t("METER_CATALOGUE_PAGE.UNIT_LABEL")}</Label>
                <Input id="catalogue-unit" value={draft.unitLabel} onChange={(event) => update({ unitLabel: event.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="catalogue-scale">{t("METER_CATALOGUE_PAGE.DECIMAL_PLACES")}</Label>
                <Input
                  id="catalogue-scale"
                  type="number"
                  min={0}
                  max={METER_QUANTITY_MAX_SCALE}
                  value={draft.quantityScale}
                  onChange={(event) => update({ quantityScale: event.target.value })}
                />
              </div>
              <div className="space-y-1 sm:col-span-2">
                <Label>{t("METER_CATALOGUE_PAGE.AGGREGATION")}</Label>
                <Select value={String(draft.aggregation)} onValueChange={(value) => update({ aggregation: Number(value) })}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {METER_AGGREGATION_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={String(option.value)}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            {problem && (
              <p role="alert" className="text-sm text-destructive">
                {problem}
              </p>
            )}
            <DialogFooter>
              <Button variant="outline" onClick={() => setEditing(null)}>
                {t("common:CANCEL")}
              </Button>
              <Button
                disabled={save.isPending || !draft.meterKey.trim() || !draft.displayName.trim() || !draft.unitLabel.trim()}
                onClick={() => save.mutate({ entry: editing.entry, draft })}
              >
                {save.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                {t("common:SAVE")}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      )}
    </main>
  );
};
