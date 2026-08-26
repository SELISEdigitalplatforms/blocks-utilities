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
          <DialogTitle>Audit trail — {planName}</DialogTitle>
          <DialogDescription>
            Sends{" "}
            <code>
              GET /api/subscriptions/{subscriptionId}/audit
            </code>
            . The immutable lifecycle trail for this subscription, newest first — it never carries
            who performed an action or a payment identifier, and it is scoped to this subscription
            only, not a tenant-wide audit search.
          </DialogDescription>
        </DialogHeader>

        <div className="flex items-center justify-between gap-2">
          <Select
            value={String(limit)}
            onValueChange={(value) => setLimit(Number(value))}
          >
            <SelectTrigger className="w-40" aria-label="Number of events">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {AUDIT_TRAIL_LIMIT_OPTIONS.map((option) => (
                <SelectItem key={option} value={String(option)}>
                  Last {option}
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
            Refresh
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
              <span className="font-medium">The audit trail could not be loaded</span>
            </div>
            <p className="text-sm text-muted-foreground">
              {error instanceof Error ? error.message : "Try again in a moment."}
            </p>
            <Button size="sm" variant="outline" onClick={() => refetch()}>
              Try again
            </Button>
          </div>
        ) : !events?.length ? (
          <p className="py-8 text-center text-sm text-muted-foreground">
            No audit events recorded for this subscription yet.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Time</TableHead>
                <TableHead>Operation / stage</TableHead>
                <TableHead>Outcome</TableHead>
                <TableHead>Status transition</TableHead>
                <TableHead>Amount</TableHead>
                <TableHead>Source</TableHead>
                <TableHead>Attempt</TableHead>
                <TableHead>Error</TableHead>
                <TableHead>IDs</TableHead>
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
                        corr {shortenId(event.correlationId)}
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
