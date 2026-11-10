import { tStatic, useT } from "@seliseblocks/genesis-os";
import { useMutation } from "@tanstack/react-query";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { toast } from "@/hooks/use-toast";
import { useCreditOffers } from "../../subscription/hooks/use-meter-catalogue";
import type { CreditOffer } from "../../subscription/models/meter-catalogue.model";
import { formatMoney } from "../../subscription/utilities/subscription-format";
import {
  useGrantOrganizationCredits,
  useOrganizationCreditGrants,
  useOrganizationCreditPurchases,
  usePreviewOrganizationCreditPurchase,
  usePurchaseOrganizationCredits,
} from "../hooks/use-organization-credits";
import type { CreditPurchase, CreditQuote } from "../models/subscription-simulation.model";
import { subscriptionSimulationHarnessService } from "../services/subscription-simulation-harness.service";
import { SubscriptionOperationError } from "../services/subscription-simulation.service";

const MODULE = "utilities.subscription-simulation";
const CANCEL = "common:CANCEL";

const errorMessage = (error: unknown, fallback: string) =>
  error instanceof Error && error.message ? error.message : fallback;

const UNSETTLED_PURCHASE_CODES = new Set([
  "subscription_credits_purchase_in_flight",
  "subscription_credits_charge_unresolved",
  "subscription_credits_checkout_unresolved",
]);

/** As the subscription credits card decides it: a settled refusal means the next try is a new purchase. */
const isSettledPurchaseFailure = (error: unknown): boolean =>
  error instanceof SubscriptionOperationError &&
  error.code !== "unknown" &&
  !(error.status !== undefined && error.status >= 500) &&
  !UNSETTLED_PURCHASE_CODES.has(error.code);

const describeExpiry = (offer: CreditOffer): string =>
  offer.expiresAfter
    ? tStatic(MODULE, "ORGANIZATION_CREDITS_CARD.EXPIRES_AFTER", {
        count: offer.expiresAfter.count,
        unit: offer.expiresAfter.unit,
      })
    : tStatic(MODULE, "ORGANIZATION_CREDITS_CARD.NEVER_EXPIRE");

type Action = { kind: "buy" | "grant"; offer: CreditOffer };

const NO_PACK = "__none__";

/**
 * The standalone credits an organization holds outside any subscription (spec 005): the offers it
 * may buy from, what it holds, and what it bought. Shown with or without a subscription — that is
 * the point of standalone credits.
 */
export const OrganizationCreditsCard = ({
  organizationId,
  showSweeps = false,
  purchaseRefused = false,
}: {
  organizationId: string | undefined;
  /**
   * The organization holds a live subscription paid by bank transfer, so buying is refused (spec
   * 006) and only the operator's free grant is offered.
   */
  purchaseRefused?: boolean;
  /** The harness's credit sweeps, for an organization with no subscription to run them from. */
  showSweeps?: boolean;
}) => {
  const { t } = useT(MODULE);
  const [action, setAction] = useState<Action | null>(null);
  const offers = useCreditOffers(organizationId);
  const grants = useOrganizationCreditGrants(organizationId);
  const purchases = useOrganizationCreditPurchases(organizationId);

  const sweeps = useMutation({
    mutationFn: () =>
      subscriptionSimulationHarnessService.runOrganizationDueJobs(organizationId ?? "", {
        organizationId,
        workTypes: ["CreditPurchaseRecovery", "CreditGrantMaintenance"],
      }),
    onSuccess: (result) => {
      grants.refetch();
      purchases.refetch();
      toast({
        variant: "success",
        title: t("ORGANIZATION_CREDITS_CARD.SWEEPS_RUN"),
        description: result.jobs.map((job) => `${job.workType}: ${job.status}`).join(" · "),
      });
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("ORGANIZATION_CREDITS_CARD.SWEEPS_FAILED"),
        description: errorMessage(error, ""),
      }),
  });

  return (
    <Card className="rounded-xl p-0">
      <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
        <div>
          <h2 className="font-semibold">{t("ORGANIZATION_CREDITS_CARD.TITLE")}</h2>
          <p className="text-xs text-muted-foreground">{t("ORGANIZATION_CREDITS_CARD.DESCRIPTION")}</p>
        </div>
        {showSweeps && organizationId && (
          <Button size="sm" variant="outline" disabled={sweeps.isPending} onClick={() => sweeps.mutate()}>
            {sweeps.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("ORGANIZATION_CREDITS_CARD.RUN_SWEEPS")}
          </Button>
        )}
      </div>

      {offers.isError ? (
        <p className="p-4 text-xs text-destructive">
          {errorMessage(offers.error, t("ORGANIZATION_CREDITS_CARD.OFFERS_FAILED"))}
        </p>
      ) : !offers.data?.length ? (
        <p className="p-4 text-xs text-muted-foreground">{t("ORGANIZATION_CREDITS_CARD.NO_OFFERS")}</p>
      ) : (
        <ul className="divide-y">
          {offers.data.map((offer) => (
            <li
              key={offer.offerId}
              className="flex flex-col gap-2 p-4 sm:flex-row sm:items-center sm:justify-between sm:px-5"
            >
              <div>
                <p className="text-sm font-medium">{offer.displayName || offer.meterKey}</p>
                <p className="text-xs text-muted-foreground">
                  {offer.unitPrices.map((price) => formatMoney(price.unitAmountMinor, price.currencyCode)).join(" / ")}{" "}
                  {t("ORGANIZATION_CREDITS_CARD.PER_CREDIT_BOUNDS", {
                    min: offer.minQuantity.toLocaleString(),
                    max: offer.maxQuantity.toLocaleString(),
                  })}{" "}
                  {describeExpiry(offer)}
                </p>
              </div>
              <div className="flex gap-2">
                {!purchaseRefused && (
                  <Button size="sm" disabled={!offer.enabled} onClick={() => setAction({ kind: "buy", offer })}>
                    {t("ORGANIZATION_CREDITS_CARD.BUY")}
                  </Button>
                )}
                <Button size="sm" variant="outline" onClick={() => setAction({ kind: "grant", offer })}>
                  {t("ORGANIZATION_CREDITS_CARD.GRANT")}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <div className="border-t p-4 sm:p-5">
        <h3 className="mb-2 text-sm font-semibold">{t("ORGANIZATION_CREDITS_CARD.GRANTS")}</h3>
        {grants.isLoading ? (
          <p className="text-xs text-muted-foreground">{t("ORGANIZATION_CREDITS_CARD.LOADING")}</p>
        ) : grants.isError ? (
          <p className="text-xs text-destructive">{errorMessage(grants.error, t("ORGANIZATION_CREDITS_CARD.GRANTS_FAILED"))}</p>
        ) : !grants.data?.length ? (
          <p className="text-xs text-muted-foreground">{t("ORGANIZATION_CREDITS_CARD.NO_GRANTS")}</p>
        ) : (
          <table className="w-full text-left text-xs">
            <thead className="text-muted-foreground">
              <tr>
                <th className="py-1 font-medium">{t("ORGANIZATION_CREDITS_CARD.METER")}</th>
                <th className="py-1 font-medium">{t("ORGANIZATION_CREDITS_CARD.SOURCE")}</th>
                <th className="py-1 text-right font-medium">{t("ORGANIZATION_CREDITS_CARD.REMAINING")}</th>
                <th className="py-1 font-medium">{t("ORGANIZATION_CREDITS_CARD.EXPIRES")}</th>
                <th className="py-1 font-medium">{t("common:STATUS")}</th>
              </tr>
            </thead>
            <tbody>
              {grants.data.map((grant) => (
                <tr key={grant.grantId} className="border-t">
                  <td className="py-1">{grant.meterKey}</td>
                  <td className="py-1">{grant.source}</td>
                  <td className="py-1 text-right tabular-nums">
                    {grant.remaining.toLocaleString()} / {grant.quantity.toLocaleString()}
                  </td>
                  <td className="py-1">
                    {grant.expiresAtUtc
                      ? new Date(grant.expiresAtUtc).toLocaleString()
                      : t("ORGANIZATION_CREDITS_CARD.NEVER_EXPIRE")}
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

      <div className="border-t p-4 sm:p-5">
        <h3 className="mb-2 text-sm font-semibold">{t("ORGANIZATION_CREDITS_CARD.PURCHASES")}</h3>
        {!purchases.data?.length ? (
          <p className="text-xs text-muted-foreground">{t("ORGANIZATION_CREDITS_CARD.NO_PURCHASES")}</p>
        ) : (
          <table className="w-full text-left text-xs">
            <thead className="text-muted-foreground">
              <tr>
                <th className="py-1 font-medium">{t("ORGANIZATION_CREDITS_CARD.METER")}</th>
                <th className="py-1 text-right font-medium">{t("ORGANIZATION_CREDITS_CARD.QUANTITY")}</th>
                <th className="py-1 text-right font-medium">{t("ORGANIZATION_CREDITS_CARD.TOTAL")}</th>
                <th className="py-1 font-medium">{t("common:STATUS")}</th>
                <th className="py-1 font-medium">{t("ORGANIZATION_CREDITS_CARD.CREATED")}</th>
              </tr>
            </thead>
            <tbody>
              {purchases.data.map((purchase) => (
                <tr key={purchase.purchaseId} className="border-t">
                  <td className="py-1">{purchase.quote.meterKey}</td>
                  <td className="py-1 text-right tabular-nums">{purchase.quote.quantity.toLocaleString()}</td>
                  <td className="py-1 text-right tabular-nums">
                    {formatMoney(purchase.quote.totalMinor, purchase.quote.currencyCode)}
                  </td>
                  <td className="py-1">
                    <Badge variant={purchase.status === "Finalized" ? "default" : "secondary"}>{purchase.status}</Badge>
                  </td>
                  <td className="py-1">{new Date(purchase.createdAtUtc).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {action?.kind === "buy" && (
        <BuyOrganizationCreditsDialog offer={action.offer} organizationId={organizationId} onClose={() => setAction(null)} />
      )}
      {action?.kind === "grant" && (
        <GrantOrganizationCreditsDialog offer={action.offer} organizationId={organizationId} onClose={() => setAction(null)} />
      )}
    </Card>
  );
};

const BuyOrganizationCreditsDialog = ({
  offer,
  organizationId,
  onClose,
}: {
  offer: CreditOffer;
  organizationId: string | undefined;
  onClose: () => void;
}) => {
  const { t } = useT(MODULE);
  const [quantity, setQuantity] = useState(String(Math.max(offer.minQuantity, 100)));
  const [packCode, setPackCode] = useState(NO_PACK);
  const [currencyCode, setCurrencyCode] = useState(offer.unitPrices[0]?.currencyCode ?? "");
  const pack = offer.packs.find((candidate) => candidate.code === packCode) ?? null;
  // A pack is priced on its own: the currencies on offer are the pack's, not the meter's.
  const prices = pack ? pack.unitPrices : offer.unitPrices;
  const [code, setCode] = useState("");
  const [quote, setQuote] = useState<CreditQuote | null>(null);
  // One key per quote: a retry of the same purchase reuses it and is never charged twice.
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());
  const [problem, setProblem] = useState<string | null>(null);
  const [awaiting, setAwaiting] = useState<CreditPurchase | null>(null);
  const checkout = quote?.paymentPath === "Checkout";
  const preview = usePreviewOrganizationCreditPurchase();
  const purchase = usePurchaseOrganizationCredits();

  const request = () => ({
    organizationId,
    meterKey: offer.meterKey,
    quantity: pack ? pack.quantity : Number(quantity),
    packCode: pack?.code,
    currencyCode,
    discountCode: code.trim() || undefined,
  });

  const choosePack = (value: string) =>
    edit(() => {
      setPackCode(value);
      const chosen = offer.packs.find((candidate) => candidate.code === value);
      const available = chosen ? chosen.unitPrices : offer.unitPrices;
      if (!available.some((price) => price.currencyCode === currencyCode)) {
        setCurrencyCode(available[0]?.currencyCode ?? "");
      }
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
      setProblem(errorMessage(error, t("ORGANIZATION_CREDITS_CARD.QUOTE_FAILED")));
    }
  };

  const buy = async () => {
    setProblem(null);
    try {
      const result = await purchase.mutateAsync({ ...request(), idempotencyKey });

      if (result.status === "AwaitingPayment") {
        if (!awaiting && result.checkoutUrl) {
          window.open(result.checkoutUrl, "_blank", "noopener");
        }
        setAwaiting(result);
        return;
      }

      toast({
        title: result.replayed ? t("ORGANIZATION_CREDITS_CARD.ALREADY_BOUGHT") : t("ORGANIZATION_CREDITS_CARD.BOUGHT"),
        description: t("ORGANIZATION_CREDITS_CARD.BOUGHT_FOR", {
          quantity: result.quote.quantity.toLocaleString(),
          total: formatMoney(result.quote.totalMinor, result.quote.currencyCode),
        }),
      });
      onClose();
    } catch (error) {
      setProblem(errorMessage(error, t("ORGANIZATION_CREDITS_CARD.PURCHASE_FAILED")));

      if (isSettledPurchaseFailure(error)) {
        setAwaiting(null);
        setIdempotencyKey(crypto.randomUUID());
      }
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("ORGANIZATION_CREDITS_CARD.BUY_TITLE", { name: offer.displayName || offer.meterKey })}</DialogTitle>
          <DialogDescription>
            {t("ORGANIZATION_CREDITS_CARD.BUY_HINT", {
              min: offer.minQuantity.toLocaleString(),
              max: offer.maxQuantity.toLocaleString(),
            })}
          </DialogDescription>
        </DialogHeader>

        {offer.packs.length > 0 && (
          <div className="space-y-1">
            <Label>{t("ORGANIZATION_CREDITS_CARD.PACK")}</Label>
            <Select value={packCode} onValueChange={choosePack}>
              <SelectTrigger aria-label={t("ORGANIZATION_CREDITS_CARD.PACK")}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_PACK}>{t("ORGANIZATION_CREDITS_CARD.NO_PACK")}</SelectItem>
                {offer.packs.map((candidate) => (
                  <SelectItem key={candidate.code} value={candidate.code}>
                    {candidate.displayName} · {candidate.quantity.toLocaleString()} ·{" "}
                    {candidate.unitPrices.map((price) => formatMoney(price.unitAmountMinor, price.currencyCode)).join(" / ")}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}

        <div className="grid gap-3 sm:grid-cols-3">
          <div className="space-y-1">
            <Label htmlFor="org-credit-quantity">{t("ORGANIZATION_CREDITS_CARD.CREDITS")}</Label>
            <Input
              id="org-credit-quantity"
              type="number"
              min={offer.minQuantity}
              max={offer.maxQuantity}
              value={pack ? String(pack.quantity) : quantity}
              disabled={pack !== null}
              onChange={(event) => edit(() => setQuantity(event.target.value))}
            />
          </div>
          <div className="space-y-1">
            <Label>{t("ORGANIZATION_CREDITS_CARD.CURRENCY")}</Label>
            <Select value={currencyCode} onValueChange={(value) => edit(() => setCurrencyCode(value))}>
              <SelectTrigger aria-label={t("ORGANIZATION_CREDITS_CARD.CURRENCY")}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {prices.map((price) => (
                  <SelectItem key={price.currencyCode} value={price.currencyCode}>
                    {price.currencyCode} · {formatMoney(price.unitAmountMinor, price.currencyCode)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="org-credit-code">{t("ORGANIZATION_CREDITS_CARD.DISCOUNT_CODE")}</Label>
            <Input id="org-credit-code" value={code} onChange={(event) => edit(() => setCode(event.target.value))} />
          </div>
        </div>

        {quote && (
          <dl className="grid grid-cols-2 gap-1 rounded-md border p-3 text-sm" aria-label={t("ORGANIZATION_CREDITS_CARD.QUOTE")}>
            <dt>{t("ORGANIZATION_CREDITS_CARD.SUBTOTAL")}</dt>
            <dd className="text-right tabular-nums">{formatMoney(quote.grossMinor, quote.currencyCode)}</dd>
            {quote.discountMinor > 0 && (
              <>
                <dt>{t("ORGANIZATION_CREDITS_CARD.DISCOUNT")}</dt>
                <dd className="text-right tabular-nums">−{formatMoney(quote.discountMinor, quote.currencyCode)}</dd>
              </>
            )}
            {quote.taxMinor > 0 && (
              <>
                <dt>{t("ORGANIZATION_CREDITS_CARD.TAX")}</dt>
                <dd className="text-right tabular-nums">{formatMoney(quote.taxMinor, quote.currencyCode)}</dd>
              </>
            )}
            <dt className="font-semibold">{t("ORGANIZATION_CREDITS_CARD.TOTAL")}</dt>
            <dd className="text-right font-semibold tabular-nums">{formatMoney(quote.totalMinor, quote.currencyCode)}</dd>
            <dt className="col-span-2 pt-1 text-xs text-muted-foreground">
              {quote.expiresAtUtc
                ? t("ORGANIZATION_CREDITS_CARD.EXPIRE_ON", { date: new Date(quote.expiresAtUtc).toLocaleString() })
                : t("ORGANIZATION_CREDITS_CARD.NEVER_EXPIRE")}
            </dt>
          </dl>
        )}

        {awaiting && (
          <div role="status" className="rounded-md border p-3 text-sm">
            <p className="font-medium">{t("ORGANIZATION_CREDITS_CARD.WAITING_FOR_PAYMENT")}</p>
            {awaiting.checkoutUrl && (
              <a href={awaiting.checkoutUrl} target="_blank" rel="noopener noreferrer" className="underline">
                {t("ORGANIZATION_CREDITS_CARD.OPEN_CHECKOUT")}
              </a>
            )}
          </div>
        )}

        {problem && (
          <p role="alert" className="text-sm text-destructive">
            {problem}
          </p>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t(CANCEL)}
          </Button>
          {awaiting ? (
            <Button onClick={buy} disabled={purchase.isPending}>
              {purchase.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {t("ORGANIZATION_CREDITS_CARD.CHECK_PAYMENT")}
            </Button>
          ) : quote ? (
            <Button onClick={buy} disabled={purchase.isPending}>
              {purchase.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {checkout
                ? t("ORGANIZATION_CREDITS_CARD.PAY_AT_CHECKOUT", { total: formatMoney(quote.totalMinor, quote.currencyCode) })
                : t("ORGANIZATION_CREDITS_CARD.BUY_FOR", { total: formatMoney(quote.totalMinor, quote.currencyCode) })}
            </Button>
          ) : (
            <Button onClick={getQuote} disabled={preview.isPending || !currencyCode}>
              {preview.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {t("ORGANIZATION_CREDITS_CARD.GET_QUOTE")}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

const GrantOrganizationCreditsDialog = ({
  offer,
  organizationId,
  onClose,
}: {
  offer: CreditOffer;
  organizationId: string | undefined;
  onClose: () => void;
}) => {
  const { t } = useT(MODULE);
  const [quantity, setQuantity] = useState("10");
  const [reason, setReason] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const grant = useGrantOrganizationCredits();

  const submit = async () => {
    setProblem(null);
    try {
      await grant.mutateAsync({ organizationId, meterKey: offer.meterKey, quantity: Number(quantity), reason: reason.trim() });
      toast({ title: t("ORGANIZATION_CREDITS_CARD.GRANTED"), description: t("ORGANIZATION_CREDITS_CARD.GRANTED_HINT", { quantity }) });
      onClose();
    } catch (error) {
      setProblem(errorMessage(error, t("ORGANIZATION_CREDITS_CARD.GRANT_FAILED")));
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("ORGANIZATION_CREDITS_CARD.GRANT_TITLE", { name: offer.displayName || offer.meterKey })}</DialogTitle>
          <DialogDescription>{t("ORGANIZATION_CREDITS_CARD.GRANT_HINT")}</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1">
            <Label htmlFor="org-grant-quantity">{t("ORGANIZATION_CREDITS_CARD.CREDITS")}</Label>
            <Input id="org-grant-quantity" type="number" min={1} value={quantity} onChange={(event) => setQuantity(event.target.value)} />
          </div>
          <div className="space-y-1 sm:col-span-2">
            <Label htmlFor="org-grant-reason">{t("ORGANIZATION_CREDITS_CARD.REASON")}</Label>
            <Input id="org-grant-reason" value={reason} onChange={(event) => setReason(event.target.value)} />
          </div>
        </div>
        {problem && (
          <p role="alert" className="text-sm text-destructive">
            {problem}
          </p>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t(CANCEL)}
          </Button>
          <Button onClick={submit} disabled={grant.isPending || !reason.trim()}>
            {grant.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("ORGANIZATION_CREDITS_CARD.GRANT")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
