import { useT } from "@seliseblocks/genesis-os";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { useMeterCatalogue } from "../../hooks/use-meter-catalogue";
import { useOrganizationScope } from "../../hooks/use-organization-scope";
import type { MeterCatalogueEntry } from "../../models/meter-catalogue.model";

const AGGREGATION_INDEX: Record<MeterCatalogueEntry["aggregation"], number> = { Sum: 0, Max: 1, Last: 2 };

/**
 * Pre-fills a plan meter from the catalogue (spec 005). Picking is optional: a key typed by hand
 * still works, and joins the catalogue when the plan is saved.
 */
export const CatalogueMeterPicker = ({
  onPick,
}: {
  onPick: (meter: { meterKey: string; displayName: string; unitLabel: string; aggregation: number; quantityScale: number }) => void;
}) => {
  const { t } = useT("utilities.subscription.plan-builder");
  const organizationId = useOrganizationScope();
  const catalogue = useMeterCatalogue(organizationId);
  const entries = (catalogue.data ?? []).filter((entry) => entry.status === "Active");

  if (entries.length === 0) {
    return null;
  }

  return (
    <Select
      value=""
      onValueChange={(meterKey) => {
        const entry = entries.find((candidate) => candidate.meterKey === meterKey);
        if (!entry) return;
        onPick({
          meterKey: entry.meterKey,
          displayName: entry.displayName,
          unitLabel: entry.unitLabel,
          aggregation: AGGREGATION_INDEX[entry.aggregation] ?? 0,
          quantityScale: entry.quantityScale,
        });
      }}
    >
      <SelectTrigger aria-label={t("CATALOGUE_METER_PICKER.LABEL")}>
        <SelectValue placeholder={t("CATALOGUE_METER_PICKER.PLACEHOLDER")} />
      </SelectTrigger>
      <SelectContent>
        {entries.map((entry) => (
          <SelectItem key={entry.entryId} value={entry.meterKey}>
            {entry.displayName} ({entry.meterKey})
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
};
