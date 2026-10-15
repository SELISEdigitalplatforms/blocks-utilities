import { Loader2 } from "lucide-react";
import { useState } from "react";
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
import { toast } from "@/hooks/use-toast";
import { formatMoney } from "../../subscription/utilities/subscription-format";
import {
  useCreditGrants,
  useGrantCredits,
  useMoveCredits,
  usePreviewCreditPurchase,
  usePurchaseCredits,
} from "../hooks/use-credits";
import { SubscriptionOperationError } from "../services/subscription-simulation.service";
import type {
  CreditGrant,
  CreditQuote,
  MeterTerms,
  SimulatedSubscription,
} from "../models/subscription-simulation.model";

const EXPIRY_LABEL: Record<CreditGrant["expiryPolicy"], string> = {
  Periodic: "expire with the usage window",
  CarryForward: "carry forward until the plan changes",
  Never: "never expire",
};

const errorMessage = (error: unknown, fallback: string) =>
  error instanceof Error && error.message ? error.message : fallback;

/** Answers that say the charge is still being settled, not that it failed. */
const UNSETTLED_PURCHASE_CODES = new Set([
  "subscription_credits_purchase_in_flight",
  "subscription_credits_charge_unresolved",
]);

/**
 * Whether the server answered that this purchase did not go through. A network failure, a 5xx, or
 * an answer that the charge is still unresolved is not that answer.
 */
const isSettledPurchaseFailure = (error: unknown): boolean =>
  error instanceof SubscriptionOperationError &&
  error.code !== "unknown" &&
  !(error.status !== undefined && error.status >= 500) &&
  !UNSETTLED_PURCHASE_CODES.has(error.code);

/** A blank seat field means the shared pool; anything else must be a seat number. */
const seatOf = (value: string): number | null => (value.trim() === "" ? null : Number(value));

const bucketLabel = (seat: number | null) => (seat === null ? "Shared pool" : `Seat ${seat}`);

type Action = { kind: "buy" | "grant" | "move"; meter: MeterTerms };

/**
 * Buying, granting and moving a subscription's meter credits, and the grants it holds.
 *
 * Every action is shown to every viewer. The console has no permission model of its own, so a
 * viewer without manage permission is refused by the server and shown its message.
 */
export const CreditsCard = ({
  subscription,
  organizationId,
}: {
  subscription: SimulatedSubscription;
  organizationId: string | undefined;
}) => {
  const [action, setAction] = useState<Action | null>(null);
  const grants = useCreditGrants(subscription.subscriptionId, organizationId);

  // Every meter can be granted to or moved between; only priced ones can be bought.
  const meters = subscription.meters;

  if (meters.length === 0) {
    return null;
  }

  return (
    <Card className="rounded-xl p-0">
      <div className="border-b p-4 sm:p-5">
        <h2 className="font-semibold">Credits</h2>
        <p className="text-xs text-muted-foreground">
          Extra credits are spent after the plan&apos;s allowance runs out and before any overage.
          A seat spends its own credits first, then the shared pool&apos;s.
        </p>
      </div>

      <ul className="divide-y">
        {meters.map((meter) => (
          <li
            key={meter.meterKey}
            className="flex flex-col gap-2 p-4 sm:flex-row sm:items-center sm:justify-between sm:px-5"
          >
            <div>
              <p className="text-sm font-medium">{meter.displayName || meter.meterKey}</p>
              <p className="text-xs text-muted-foreground">
                {meter.creditPurchase
                  ? `${meter.creditPurchase.unitAmount} ${meter.creditPurchase.currencyCode} per credit, ` +
                    `${meter.creditPurchase.minQuantity.toLocaleString()}–${meter.creditPurchase.maxQuantity.toLocaleString()} per purchase. ` +
                    `Credits ${EXPIRY_LABEL[meter.creditPurchase.expiryPolicy]}.`
                  : `Credits for this meter are not for sale in ${subscription.currencyCode}.`}
              </p>
            </div>
            <div className="flex gap-2">
              {meter.creditPurchase && (
                <Button size="sm" onClick={() => setAction({ kind: "buy", meter })}>
                  Buy credits
                </Button>
              )}
              <Button size="sm" variant="outline" onClick={() => setAction({ kind: "grant", meter })}>
                Grant
              </Button>
              <Button size="sm" variant="outline" onClick={() => setAction({ kind: "move", meter })}>
                Move
              </Button>
            </div>
          </li>
        ))}
      </ul>

      <GrantList grants={grants.data} isLoading={grants.isLoading} error={grants.error} />

      {action?.kind === "buy" && (
        <BuyCreditsDialog
          subscription={subscription}
          meter={action.meter}
          organizationId={organizationId}
          onClose={() => setAction(null)}
        />
      )}
      {action?.kind === "grant" && (
        <GrantCreditsDialog
          subscriptionId={subscription.subscriptionId}
          meter={action.meter}
          organizationId={organizationId}
          onClose={() => setAction(null)}
        />
      )}
      {action?.kind === "move" && (
        <MoveCreditsDialog
          subscriptionId={subscription.subscriptionId}
          meter={action.meter}
          organizationId={organizationId}
          onClose={() => setAction(null)}
        />
      )}
    </Card>
  );
};

const GrantList = ({
  grants,
  isLoading,
  error,
}: {
  grants: CreditGrant[] | undefined;
  isLoading: boolean;
  error: unknown;
}) => (
  <div className="border-t p-4 sm:p-5">
    <h3 className="mb-2 text-sm font-semibold">Grants</h3>
    {isLoading ? (
      <p className="text-xs text-muted-foreground">Loading…</p>
    ) : error ? (
      <p className="text-xs text-destructive">{errorMessage(error, "The credits could not be loaded.")}</p>
    ) : !grants?.length ? (
      <p className="text-xs text-muted-foreground">No credits bought or granted yet.</p>
    ) : (
      <table className="w-full text-left text-xs">
        <thead className="text-muted-foreground">
          <tr>
            <th className="py-1 font-medium">Meter</th>
            <th className="py-1 font-medium">Bucket</th>
            <th className="py-1 font-medium">Source</th>
            <th className="py-1 text-right font-medium">Remaining</th>
            <th className="py-1 font-medium">Expires</th>
            <th className="py-1 font-medium">Status</th>
          </tr>
        </thead>
        <tbody>
          {grants.map((grant) => (
            <tr key={grant.grantId} className="border-t">
              <td className="py-1">{grant.meterKey}</td>
              <td className="py-1">{bucketLabel(grant.seatNumber)}</td>
              <td className="py-1">{grant.source}</td>
              <td className="py-1 text-right tabular-nums">
                {grant.remaining.toLocaleString()} / {grant.quantity.toLocaleString()}
              </td>
              <td className="py-1">
                {grant.expiresAtUtc
                  ? new Date(grant.expiresAtUtc).toLocaleString()
                  : EXPIRY_LABEL[grant.expiryPolicy]}
              </td>
              <td className="py-1">
                <Badge variant={grant.status === "Active" ? "default" : "secondary"}>{grant.status}</Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    )}
  </div>
);

const BuyCreditsDialog = ({
  subscription,
  meter,
  organizationId,
  onClose,
}: {
  subscription: SimulatedSubscription;
  meter: MeterTerms;
  organizationId: string | undefined;
  onClose: () => void;
}) => {
  const terms = meter.creditPurchase!;
  const [quantity, setQuantity] = useState(String(Math.max(terms.minQuantity, 100)));
  const [seat, setSeat] = useState("");
  const [code, setCode] = useState("");
  const [quote, setQuote] = useState<CreditQuote | null>(null);
  // One key per quote: a retry of the same purchase reuses it and is never charged twice, and a
  // changed request gets a new one instead of being refused as a reused key.
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());
  const [problem, setProblem] = useState<string | null>(null);
  const preview = usePreviewCreditPurchase(subscription.subscriptionId);
  const purchase = usePurchaseCredits(subscription.subscriptionId);

  const request = () => ({
    organizationId,
    meterKey: meter.meterKey,
    quantity: Number(quantity),
    seatNumber: seatOf(seat),
    discountCode: code.trim() || undefined,
  });

  const edit = (apply: () => void) => {
    apply();
    setQuote(null);
    setProblem(null);
  };

  const getQuote = async () => {
    setProblem(null);
    try {
      setQuote(await preview.mutateAsync(request()));
      setIdempotencyKey(crypto.randomUUID());
    } catch (error) {
      setProblem(errorMessage(error, "The credits could not be quoted."));
    }
  };

  const buy = async () => {
    setProblem(null);
    try {
      const result = await purchase.mutateAsync({ ...request(), idempotencyKey });
      toast({
        title: result.replayed ? "Already bought" : "Credits bought",
        description: `${result.quote.quantity.toLocaleString()} credits for ${formatMoney(result.quote.totalMinor, result.quote.currencyCode)}.`,
      });
      onClose();
    } catch (error) {
      setProblem(errorMessage(error, "The credits could not be bought."));

      // The server replays a failed purchase for its key, so a retry after a declined card needs a
      // new one. Kept whenever the charge may still have gone through, where a retry under the
      // same key is exactly what stops it being charged twice.
      if (isSettledPurchaseFailure(error)) {
        setIdempotencyKey(crypto.randomUUID());
      }
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Buy {meter.displayName || meter.meterKey} credits</DialogTitle>
          <DialogDescription>
            Charged once to the subscription&apos;s saved card. Between{" "}
            {terms.minQuantity.toLocaleString()} and {terms.maxQuantity.toLocaleString()} credits.
          </DialogDescription>
        </DialogHeader>

        <div className="grid gap-3 sm:grid-cols-3">
          <div className="space-y-1">
            <Label htmlFor="credit-quantity">Credits</Label>
            <Input
              id="credit-quantity"
              type="number"
              min={terms.minQuantity}
              max={terms.maxQuantity}
              value={quantity}
              onChange={(event) => edit(() => setQuantity(event.target.value))}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="credit-seat">Seat (optional)</Label>
            <Input
              id="credit-seat"
              type="number"
              min={1}
              placeholder="Shared pool"
              value={seat}
              onChange={(event) => edit(() => setSeat(event.target.value))}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="credit-code">Discount code</Label>
            <Input
              id="credit-code"
              value={code}
              onChange={(event) => edit(() => setCode(event.target.value))}
            />
          </div>
        </div>

        {quote && (
          <dl className="grid grid-cols-2 gap-1 rounded-md border p-3 text-sm" aria-label="Quote">
            <dt>Subtotal</dt>
            <dd className="text-right tabular-nums">{formatMoney(quote.grossMinor, quote.currencyCode)}</dd>
            {quote.discountMinor > 0 && (
              <>
                <dt>Discount ({quote.discountCode})</dt>
                <dd className="text-right tabular-nums">
                  −{formatMoney(quote.discountMinor, quote.currencyCode)}
                </dd>
              </>
            )}
            {quote.taxMinor > 0 && (
              <>
                <dt>Tax</dt>
                <dd className="text-right tabular-nums">{formatMoney(quote.taxMinor, quote.currencyCode)}</dd>
              </>
            )}
            <dt className="font-semibold">Total</dt>
            <dd className="text-right font-semibold tabular-nums">
              {formatMoney(quote.totalMinor, quote.currencyCode)}
            </dd>
            <dt className="col-span-2 pt-1 text-xs text-muted-foreground">
              {quote.expiresAtUtc
                ? `These credits expire on ${new Date(quote.expiresAtUtc).toLocaleString()}.`
                : `These credits ${EXPIRY_LABEL[quote.expiryPolicy]}.`}
            </dt>
          </dl>
        )}

        {problem && (
          <p role="alert" className="text-sm text-destructive">
            {problem}
          </p>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          {quote ? (
            <Button onClick={buy} disabled={purchase.isPending}>
              {purchase.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Buy for {formatMoney(quote.totalMinor, quote.currencyCode)}
            </Button>
          ) : (
            <Button onClick={getQuote} disabled={preview.isPending}>
              {preview.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Get quote
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

const GrantCreditsDialog = ({
  subscriptionId,
  meter,
  organizationId,
  onClose,
}: {
  subscriptionId: string;
  meter: MeterTerms;
  organizationId: string | undefined;
  onClose: () => void;
}) => {
  const [quantity, setQuantity] = useState("10");
  const [seat, setSeat] = useState("");
  const [reason, setReason] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const grant = useGrantCredits(subscriptionId);

  const submit = async () => {
    setProblem(null);
    try {
      await grant.mutateAsync({
        organizationId,
        meterKey: meter.meterKey,
        quantity: Number(quantity),
        seatNumber: seatOf(seat),
        reason: reason.trim(),
      });
      toast({ title: "Credits granted", description: `${quantity} credits, no charge.` });
      onClose();
    } catch (error) {
      setProblem(errorMessage(error, "The credits could not be granted."));
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Grant {meter.displayName || meter.meterKey} credits</DialogTitle>
          <DialogDescription>
            Free credits, with the meter&apos;s own expiry. The reason is kept with the grant.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1">
            <Label htmlFor="grant-quantity">Credits</Label>
            <Input
              id="grant-quantity"
              type="number"
              min={1}
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="grant-seat">Seat (optional)</Label>
            <Input
              id="grant-seat"
              type="number"
              min={1}
              placeholder="Shared pool"
              value={seat}
              onChange={(event) => setSeat(event.target.value)}
            />
          </div>
        </div>
        <div className="space-y-1">
          <Label htmlFor="grant-reason">Reason</Label>
          <Input
            id="grant-reason"
            value={reason}
            placeholder="Outage on 12 October"
            onChange={(event) => setReason(event.target.value)}
          />
        </div>
        {problem && (
          <p role="alert" className="text-sm text-destructive">
            {problem}
          </p>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={grant.isPending || !reason.trim()}>
            {grant.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Grant credits
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

const MoveCreditsDialog = ({
  subscriptionId,
  meter,
  organizationId,
  onClose,
}: {
  subscriptionId: string;
  meter: MeterTerms;
  organizationId: string | undefined;
  onClose: () => void;
}) => {
  const [quantity, setQuantity] = useState("10");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const move = useMoveCredits(subscriptionId);

  const submit = async () => {
    setProblem(null);
    try {
      await move.mutateAsync({
        organizationId,
        meterKey: meter.meterKey,
        quantity: Number(quantity),
        fromSeatNumber: seatOf(from),
        toSeatNumber: seatOf(to),
      });
      toast({
        title: "Credits moved",
        description: `${quantity} credits from ${bucketLabel(seatOf(from)).toLowerCase()} to ${bucketLabel(seatOf(to)).toLowerCase()}.`,
      });
      onClose();
    } catch (error) {
      setProblem(errorMessage(error, "The credits could not be moved."));
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Move {meter.displayName || meter.meterKey} credits</DialogTitle>
          <DialogDescription>
            Seated plans only. The credits that would expire soonest move first, and keep their expiry.
            Leave a seat blank for the shared pool.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-3">
          <div className="space-y-1">
            <Label htmlFor="move-quantity">Credits</Label>
            <Input
              id="move-quantity"
              type="number"
              min={1}
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="move-from">From seat</Label>
            <Input
              id="move-from"
              type="number"
              min={1}
              placeholder="Shared pool"
              value={from}
              onChange={(event) => setFrom(event.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="move-to">To seat</Label>
            <Input
              id="move-to"
              type="number"
              min={1}
              placeholder="Shared pool"
              value={to}
              onChange={(event) => setTo(event.target.value)}
            />
          </div>
        </div>
        {problem && (
          <p role="alert" className="text-sm text-destructive">
            {problem}
          </p>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={move.isPending || from === to}>
            {move.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Move credits
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
