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
  CreditPurchase,
  CreditQuote,
  MeterTerms,
  SimulatedSubscription,
} from "../models/subscription-simulation.model";
import { tStatic, useT } from "@seliseblocks/genesis-os";

const UTILITIES_SUBSCRIPTION_SIMULATION_MODULE = "utilities.subscription-simulation";
const CREDITS_CARD_SHARED_POOL = "CREDITS_CARD.SHARED_POOL";
const CREDITS_CARD_CREDITS = "CREDITS_CARD.CREDITS";
const COMMON_CANCEL = "common:CANCEL";

const expiryLabel = (policy: CreditGrant["expiryPolicy"]): string => {
  switch (policy) {
    case "Periodic":
      return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "CREDITS_CARD.EXPIRE_WITH_USAGE_WINDOW");
    case "CarryForward":
      return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "CREDITS_CARD.CARRY_FORWARD");
    default:
      return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "CREDITS_CARD.NEVER_EXPIRE");
  }
};

const expiresAfterText = (count: number, unit: string): string => {
  switch (unit) {
    case "Hour":
      return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "CREDITS_CARD.LAST_HOURS", { count });
    case "Week":
      return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "CREDITS_CARD.LAST_WEEKS", { count });
    case "Month":
      return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "CREDITS_CARD.LAST_MONTHS", { count });
    case "Year":
      return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "CREDITS_CARD.LAST_YEARS", { count });
    default:
      return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "CREDITS_CARD.LAST_DAYS", { count });
  }
};

const errorMessage = (error: unknown, fallback: string) =>
  error instanceof Error && error.message ? error.message : fallback;

/** Answers that say the charge is still being settled, not that it failed. */
const UNSETTLED_PURCHASE_CODES = new Set([
  "subscription_credits_purchase_in_flight",
  "subscription_credits_charge_unresolved",
  "subscription_credits_checkout_unresolved",
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

const bucketLabel = (seat: number | null) =>
  seat === null
    ? tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, CREDITS_CARD_SHARED_POOL)
    : tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "CREDITS_CARD.SEAT_NUMBER", { seat });

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
  const { t } = useT(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE);
  const [action, setAction] = useState<Action | null>(null);
  const grants = useCreditGrants(subscription.subscriptionId, organizationId);

  // Every meter can be granted to or moved between; only priced ones can be bought.
  const meters = subscription.meters;

  if (meters.length === 0) {
    return null;
  }

  const describeExpiry = (purchase: NonNullable<(typeof meters)[number]["creditPurchase"]>): string => {
    if (purchase.expiresAfter) return expiresAfterText(purchase.expiresAfter.count, purchase.expiresAfter.unit);
    return t("CREDITS_CARD.CREDITS_EXPIRY", { expiry: expiryLabel(purchase.expiryPolicy) });
  };

  return (
    <Card className="rounded-xl p-0">
      <div className="border-b p-4 sm:p-5">
        <h2 className="font-semibold">{t(CREDITS_CARD_CREDITS)}</h2>
        <p className="text-xs text-muted-foreground">
          {t("CREDITS_CARD.EXTRA_CREDITS_ARE_SPENT_AFTER")}
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
                  ? `${t("CREDITS_CARD.PURCHASE_TERMS", {
                      unitAmount: meter.creditPurchase.unitAmount,
                      currencyCode: meter.creditPurchase.currencyCode,
                      min: meter.creditPurchase.minQuantity.toLocaleString(),
                      max: meter.creditPurchase.maxQuantity.toLocaleString(),
                    })} ${describeExpiry(meter.creditPurchase)}`
                  : t("CREDITS_CARD.CREDITS_FOR_THIS_METER_ARE", { currencyCode: subscription.currencyCode })}
              </p>
            </div>
            <div className="flex gap-2">
              {meter.creditPurchase && (
                <Button size="sm" onClick={() => setAction({ kind: "buy", meter })}>
                  {t("CREDITS_CARD.BUY_CREDITS")}
                </Button>
              )}
              <Button size="sm" variant="outline" onClick={() => setAction({ kind: "grant", meter })}>
                {t("CREDITS_CARD.GRANT")}
              </Button>
              <Button size="sm" variant="outline" onClick={() => setAction({ kind: "move", meter })}>
                {t("CREDITS_CARD.MOVE")}
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
}) => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE);
  return (
  <div className="border-t p-4 sm:p-5">
    <h3 className="mb-2 text-sm font-semibold">{t("CREDITS_CARD.GRANTS")}</h3>
    {isLoading ? (
      <p className="text-xs text-muted-foreground">{t("CREDITS_CARD.LOADING")}</p>
    ) : error ? (
      <p className="text-xs text-destructive">{errorMessage(error, t("CREDITS_CARD.THE_CREDITS_COULD_NOT_BE"))}</p>
    ) : !grants?.length ? (
      <p className="text-xs text-muted-foreground">{t("CREDITS_CARD.NO_CREDITS_BOUGHT_OR_GRANTED")}</p>
    ) : (
      <table className="w-full text-left text-xs">
        <thead className="text-muted-foreground">
          <tr>
            <th className="py-1 font-medium">{t("CREDITS_CARD.METER")}</th>
            <th className="py-1 font-medium">{t("CREDITS_CARD.BUCKET")}</th>
            <th className="py-1 font-medium">{t("CREDITS_CARD.SOURCE")}</th>
            <th className="py-1 text-right font-medium">{t("CREDITS_CARD.REMAINING")}</th>
            <th className="py-1 font-medium">{t("CREDITS_CARD.EXPIRES")}</th>
            <th className="py-1 font-medium">{t("common:STATUS")}</th>
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
                  : expiryLabel(grant.expiryPolicy)}
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
};

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
  const { t } = useT(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE);
  const terms = meter.creditPurchase!;
  const [quantity, setQuantity] = useState(String(Math.max(terms.minQuantity, 100)));
  const [seat, setSeat] = useState("");
  const [code, setCode] = useState("");
  const [quote, setQuote] = useState<CreditQuote | null>(null);
  // One key per quote: a retry of the same purchase reuses it and is never charged twice, and a
  // changed request gets a new one instead of being refused as a reused key.
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());
  const [problem, setProblem] = useState<string | null>(null);
  // A checkout purchase waiting for the buyer to pay (spec 003). Checked by resubmitting the same
  // key, which is how the server is asked to look at the provider; never decided by this page.
  const [awaiting, setAwaiting] = useState<CreditPurchase | null>(null);
  const checkout = quote?.paymentPath === "Checkout";
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
    if (awaiting) return;
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
      setProblem(errorMessage(error, t("CREDITS_CARD.THE_CREDITS_COULD_NOT_BE_2")));
    }
  };

  const buy = async () => {
    setProblem(null);
    try {
      const result = await purchase.mutateAsync({ ...request(), idempotencyKey });

      if (result.status === "AwaitingPayment") {
        // Opened only the first time; a later check must not keep spawning tabs.
        if (!awaiting && result.checkoutUrl) {
          window.open(result.checkoutUrl, "_blank", "noopener");
        }
        setAwaiting(result);
        return;
      }

      toast({
        title: result.replayed ? t("CREDITS_CARD.ALREADY_BOUGHT") : t("CREDITS_CARD.CREDITS_BOUGHT"),
        description: t("CREDITS_CARD.CREDITS_FOR", { quantity: result.quote.quantity.toLocaleString(), totalMinor: formatMoney(result.quote.totalMinor, result.quote.currencyCode) }),
      });
      onClose();
    } catch (error) {
      setProblem(errorMessage(error, t("CREDITS_CARD.THE_CREDITS_COULD_NOT_BE_3")));

      // The server replays a failed purchase for its key, so a retry after a declined card needs a
      // new one. Kept whenever the charge may still have gone through, where a retry under the
      // same key is exactly what stops it being charged twice.
      if (isSettledPurchaseFailure(error)) {
        // An expired or declined checkout is over too: the next attempt is a new purchase.
        setAwaiting(null);
        setIdempotencyKey(crypto.randomUUID());
      }
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("CREDITS_CARD.BUY_CREDITS_2", { displayName: meter.displayName || meter.meterKey })}</DialogTitle>
          <DialogDescription>
            {(checkout ? t("CREDITS_CARD.BUY_CHECKOUT_TERMS", { min: terms.minQuantity.toLocaleString(), max: terms.maxQuantity.toLocaleString() }) : t("CREDITS_CARD.BUY_SAVED_CARD_TERMS", { min: terms.minQuantity.toLocaleString(), max: terms.maxQuantity.toLocaleString() }))}</DialogDescription>
        </DialogHeader>

        <div className="grid gap-3 sm:grid-cols-3">
          <div className="space-y-1">
            <Label htmlFor="credit-quantity">{t(CREDITS_CARD_CREDITS)}</Label>
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
            <Label htmlFor="credit-seat">{t("CREDITS_CARD.SEAT_OPTIONAL")}</Label>
            <Input
              id="credit-seat"
              type="number"
              min={1}
              placeholder={t(CREDITS_CARD_SHARED_POOL)}
              value={seat}
              onChange={(event) => edit(() => setSeat(event.target.value))}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="credit-code">{t("CREDITS_CARD.DISCOUNT_CODE")}</Label>
            <Input
              id="credit-code"
              value={code}
              onChange={(event) => edit(() => setCode(event.target.value))}
            />
          </div>
        </div>

        {quote && (
          <dl className="grid grid-cols-2 gap-1 rounded-md border p-3 text-sm" aria-label={t("CREDITS_CARD.QUOTE")}>
            <dt>{t("CREDITS_CARD.SUBTOTAL")}</dt>
            <dd className="text-right tabular-nums">{formatMoney(quote.grossMinor, quote.currencyCode)}</dd>
            {quote.discountMinor > 0 && (
              <>
                <dt>{t("CREDITS_CARD.DISCOUNT", { discountCode: (quote.discountCode) ?? "" })}</dt>
                <dd className="text-right tabular-nums">
                  −{formatMoney(quote.discountMinor, quote.currencyCode)}
                </dd>
              </>
            )}
            {quote.taxMinor > 0 && (
              <>
                <dt>{t("CREDITS_CARD.TAX")}</dt>
                <dd className="text-right tabular-nums">{formatMoney(quote.taxMinor, quote.currencyCode)}</dd>
              </>
            )}
            <dt className="font-semibold">{t("CREDITS_CARD.TOTAL")}</dt>
            <dd className="text-right font-semibold tabular-nums">
              {formatMoney(quote.totalMinor, quote.currencyCode)}
            </dd>
            <dt className="col-span-2 pt-1 text-xs text-muted-foreground">
              {quote.expiresAtUtc
                ? t("CREDITS_CARD.THESE_CREDITS_EXPIRE_ON", { date: new Date(quote.expiresAtUtc).toLocaleString() })
                : t("CREDITS_CARD.THESE_CREDITS", { expiry: expiryLabel(quote.expiryPolicy) })}
            </dt>
          </dl>
        )}

        {awaiting && (
          <div role="status" className="rounded-md border p-3 text-sm">
            <p className="font-medium">{t("CREDITS_CARD.WAITING_FOR_PAYMENT")}</p>
            <p className="text-muted-foreground">
              {t("CREDITS_CARD.CREDITS_ARE_ADDED_ONCE_THE")}{" "}
              {awaiting.checkoutUrl && (
                <a href={awaiting.checkoutUrl} target="_blank" rel="noopener noreferrer" className="underline">
                  {t("CREDITS_CARD.OPEN_CHECKOUT")}
                </a>
              )}
            </p>
          </div>
        )}

        {problem && (
          <p role="alert" className="text-sm text-destructive">
            {problem}
          </p>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t(COMMON_CANCEL)}
          </Button>
          {awaiting ? (
            <Button onClick={buy} disabled={purchase.isPending}>
              {purchase.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {t("CREDITS_CARD.CHECK_PAYMENT")}
            </Button>
          ) : quote ? (
            <Button onClick={buy} disabled={purchase.isPending}>
              {purchase.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {(checkout ? t("CREDITS_CARD.PAY_AT_CHECKOUT_FOR", { total: formatMoney(quote.totalMinor, quote.currencyCode) }) : t("CREDITS_CARD.BUY_FOR", { total: formatMoney(quote.totalMinor, quote.currencyCode) }))}
            </Button>
          ) : (
            <Button onClick={getQuote} disabled={preview.isPending}>
              {preview.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {t("CREDITS_CARD.GET_QUOTE")}
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
  const { t } = useT(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE);
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
      toast({ title: t("CREDITS_CARD.CREDITS_GRANTED"), description: t("CREDITS_CARD.CREDITS_NO_CHARGE", { quantity }) });
      onClose();
    } catch (error) {
      setProblem(errorMessage(error, t("CREDITS_CARD.THE_CREDITS_COULD_NOT_BE_4")));
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("CREDITS_CARD.GRANT_CREDITS", { displayName: meter.displayName || meter.meterKey })}</DialogTitle>
          <DialogDescription>
            {t("CREDITS_CARD.FREE_CREDITS_WITH_THE_METER")}
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1">
            <Label htmlFor="grant-quantity">{t(CREDITS_CARD_CREDITS)}</Label>
            <Input
              id="grant-quantity"
              type="number"
              min={1}
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="grant-seat">{t("CREDITS_CARD.SEAT_OPTIONAL")}</Label>
            <Input
              id="grant-seat"
              type="number"
              min={1}
              placeholder={t(CREDITS_CARD_SHARED_POOL)}
              value={seat}
              onChange={(event) => setSeat(event.target.value)}
            />
          </div>
        </div>
        <div className="space-y-1">
          <Label htmlFor="grant-reason">{t("CREDITS_CARD.REASON")}</Label>
          <Input
            id="grant-reason"
            value={reason}
            placeholder={t("CREDITS_CARD.OUTAGE_ON_12_OCTOBER")}
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
            {t(COMMON_CANCEL)}
          </Button>
          <Button onClick={submit} disabled={grant.isPending || !reason.trim()}>
            {grant.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("CREDITS_CARD.GRANT_CREDITS_2")}
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
  const { t } = useT(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE);
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
        title: t("CREDITS_CARD.CREDITS_MOVED"),
        description: t("CREDITS_CARD.CREDITS_FROM_TO", { quantity, from: bucketLabel(seatOf(from)).toLowerCase(), to: bucketLabel(seatOf(to)).toLowerCase() }),
      });
      onClose();
    } catch (error) {
      setProblem(errorMessage(error, t("CREDITS_CARD.THE_CREDITS_COULD_NOT_BE_5")));
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("CREDITS_CARD.MOVE_CREDITS", { displayName: meter.displayName || meter.meterKey })}</DialogTitle>
          <DialogDescription>
            {t("CREDITS_CARD.SEATED_PLANS_ONLY_THE_CREDITS")}
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-3">
          <div className="space-y-1">
            <Label htmlFor="move-quantity">{t(CREDITS_CARD_CREDITS)}</Label>
            <Input
              id="move-quantity"
              type="number"
              min={1}
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="move-from">{t("CREDITS_CARD.FROM_SEAT")}</Label>
            <Input
              id="move-from"
              type="number"
              min={1}
              placeholder={t(CREDITS_CARD_SHARED_POOL)}
              value={from}
              onChange={(event) => setFrom(event.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="move-to">{t("CREDITS_CARD.TO_SEAT")}</Label>
            <Input
              id="move-to"
              type="number"
              min={1}
              placeholder={t(CREDITS_CARD_SHARED_POOL)}
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
            {t(COMMON_CANCEL)}
          </Button>
          <Button onClick={submit} disabled={move.isPending || from === to}>
            {move.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("CREDITS_CARD.MOVE_CREDITS_2")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
