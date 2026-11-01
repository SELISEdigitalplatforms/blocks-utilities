import { AlertCircle, RefreshCw } from "lucide-react";
import { useState } from "react";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import {
  AUDIT_TRAIL_DEFAULT_LIMIT,
  AUDIT_TRAIL_LIMIT_OPTIONS,
} from "../constants/subscription-simulation.constants";
import { useAuditTrail } from "../hooks/use-audit-trail";
import { formatMoney } from "../../subscription/utilities/subscription-format";
import { AuditOutcomeBadge } from "./audit-outcome-badge";
import { useT } from "@seliseblocks/genesis-os";

const shortenId = (id: string) => (id.length > 12 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id);

export const AuditTrailDialog = ({
  subscriptionId,
  planName,
  organizationId,
  open,
  onOpenChange,
}: {
  subscriptionId: string;
  planName: string;
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const [limit, setLimit] = useState(AUDIT_TRAIL_DEFAULT_LIMIT);

  const {
    data: events,
    error,
    isError,
    isFetching,
    isLoading,
    refetch,
  } = useAuditTrail(subscriptionId, organizationId, limit, open);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[85vh] w-[95vw] max-w-5xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{t("AUDIT_TRAIL_DIALOG.AUDIT_TRAIL", { planName })}</DialogTitle>
          <DialogDescription>
            {t("AUDIT_TRAIL_DIALOG.SENDS_THE_IMMUTABLE_LIFECYCLE", { endpoint: `GET /api/subscriptions/${subscriptionId}/audit` })}</DialogDescription>
        </DialogHeader>

        <div className="flex items-center justify-between gap-2">
          <Select
            value={String(limit)}
            onValueChange={(value) => setLimit(Number(value))}
          >
            <SelectTrigger className="w-40" aria-label={t("AUDIT_TRAIL_DIALOG.NUMBER_OF_EVENTS")}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {AUDIT_TRAIL_LIMIT_OPTIONS.map((option) => (
                <SelectItem key={option} value={String(option)}>
                  {t("AUDIT_TRAIL_DIALOG.LAST", { option })}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Button
            size="sm"
            variant="outline"
            onClick={() => refetch()}
            disabled={isFetching}
          >
            <RefreshCw className={`mr-2 h-3.5 w-3.5 ${isFetching ? "animate-spin" : ""}`} />
            {t("common:REFRESH")}
          </Button>
        </div>

        {isLoading ? (
          <div className="space-y-2">
            {Array.from({ length: 5 }, (_, index) => (
              <Skeleton key={index} className="h-10 w-full rounded-md" />
            ))}
          </div>
        ) : isError ? (
          <div className="flex flex-col items-start gap-2">
            <div className="flex items-center gap-2 text-destructive">
              <AlertCircle className="h-4 w-4" />
              <span className="font-medium">{t("AUDIT_TRAIL_DIALOG.THE_AUDIT_TRAIL_COULD_NOT")}</span>
            </div>
            <p className="text-sm text-muted-foreground">
              {error instanceof Error ? error.message : t("AUDIT_TRAIL_DIALOG.TRY_AGAIN_IN_A_MOMENT")}
            </p>
            <Button size="sm" variant="outline" onClick={() => refetch()}>
              {t("AUDIT_TRAIL_DIALOG.TRY_AGAIN")}
            </Button>
          </div>
        ) : !events?.length ? (
          <p className="py-8 text-center text-sm text-muted-foreground">
            {t("AUDIT_TRAIL_DIALOG.NO_AUDIT_EVENTS_RECORDED_FOR")}
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("AUDIT_TRAIL_DIALOG.TIME")}</TableHead>
                <TableHead>{t("AUDIT_TRAIL_DIALOG.OPERATION_STAGE")}</TableHead>
                <TableHead>{t("AUDIT_TRAIL_DIALOG.OUTCOME")}</TableHead>
                <TableHead>{t("AUDIT_TRAIL_DIALOG.STATUS_TRANSITION")}</TableHead>
                <TableHead>{t("AUDIT_TRAIL_DIALOG.AMOUNT")}</TableHead>
                <TableHead>{t("AUDIT_TRAIL_DIALOG.SOURCE")}</TableHead>
                <TableHead>{t("AUDIT_TRAIL_DIALOG.ATTEMPT")}</TableHead>
                <TableHead>{t("common:ERROR")}</TableHead>
                <TableHead>{t("AUDIT_TRAIL_DIALOG.IDS")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {events.map((event) => (
                <TableRow key={event.eventId}>
                  <TableCell className="whitespace-nowrap text-xs">
                    {new Date(event.occurredAtUtc).toLocaleString()}
                  </TableCell>
                  <TableCell className="text-xs">
                    <div className="font-medium">{event.operation}</div>
                    <div className="text-muted-foreground">{event.stage}</div>
                  </TableCell>
                  <TableCell>
                    <AuditOutcomeBadge outcome={event.outcome} />
                  </TableCell>
                  <TableCell className="whitespace-nowrap text-xs">
                    {event.fromStatus && event.toStatus
                      ? `${event.fromStatus} → ${event.toStatus}`
                      : "—"}
                  </TableCell>
                  <TableCell className="whitespace-nowrap text-xs">
                    {event.amountMinor != null && event.currencyCode
                      ? formatMoney(event.amountMinor, event.currencyCode)
                      : "—"}
                  </TableCell>
                  <TableCell className="text-xs">{event.source}</TableCell>
                  <TableCell className="text-xs">{event.attempt ?? "—"}</TableCell>
                  <TableCell className="text-xs">
                    {event.errorCode ? (
                      <div>
                        <div className="font-medium text-destructive">{event.errorCode}</div>
                        {event.failureKind && (
                          <div className="text-muted-foreground">{event.failureKind}</div>
                        )}
                      </div>
                    ) : (
                      "—"
                    )}
                  </TableCell>
                  <TableCell className="space-y-1 text-xs">
                    <CopyToClipboardButton textToCopy={event.correlationId} isHoverable>
                      <span title={event.correlationId}>
                        {t("AUDIT_TRAIL_DIALOG.CORR", { correlationId: shortenId(event.correlationId) })}
                      </span>
                    </CopyToClipboardButton>
                    <CopyToClipboardButton textToCopy={event.operationId} isHoverable>
                      <span title={event.operationId}>
                        op {shortenId(event.operationId)}
                      </span>
                    </CopyToClipboardButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </DialogContent>
    </Dialog>
  );
};
