import { useT } from "@seliseblocks/genesis-os";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Loader2, Plus, Trash2 } from "lucide-react";
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
import { Switch } from "@/components/ui-kits/switch/switch";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { SUBSCRIPTION_CURRENCY_OPTIONS } from "../constants/subscription.constants";
import { CREDIT_OFFERS_QUERY_KEY, useCreditOffers, useMeterCatalogue } from "../hooks/use-meter-catalogue";
import { useOrganizationScope } from "../hooks/use-organization-scope";
import type {
  CreditExpiryDuration,
  CreditOffer,
  CreditOfferRequest,
  CreditPack,
  CreditSpendOrder,
} from "../models/meter-catalogue.model";
import { meterCatalogueService } from "../services/meter-catalogue.service";
import { describeDiscountAmountProblem } from "../utilities/discount-amount";
import { exampleMinorAmount, formatMoney, toMajorUnits, toMinorUnits } from "../utilities/subscription-format";

const MODULE = "utilities.subscription.pages";
const EXPIRY_UNITS = ["Hour", "Day", "Week", "Month", "Year"] as const;
const NEVER = "never";

/** A pack as the form edits it: priced in the draft's currency, other currencies kept from the stored pack. */
interface PackDraft {
  code: string;
  displayName: string;
  quantity: string;
  /** In the currency's own units, as typed: "0.10", not 10. */
  unitAmount: string;
}

interface Draft {
  meterKey: string;
  enabled: boolean;
  currencyCode: string;
  /** In the currency's own units, as typed. Converted to minor units on save. */
  unitAmount: string;
  minQuantity: string;
  maxQuantity: string;
  expiryCount: string;
  expiryUnit: string;
  taxRateBasisPoints: string;
  taxMode: "Exclusive" | "Inclusive";
  spendOrder: CreditSpendOrder;
  packs: PackDraft[];
}

const emptyDraft = (): Draft => ({
  meterKey: "",
  enabled: true,
  currencyCode: "USD",
  unitAmount: "0.10",
  minQuantity: "1",
  maxQuantity: "1000",
  expiryCount: "",
  expiryUnit: NEVER,
  taxRateBasisPoints: "",
  taxMode: "Exclusive",
  spendOrder: "SubscriptionFirst",
  packs: [],
});

const emptyPack = (): PackDraft => ({ code: "", displayName: "", quantity: "500", unitAmount: "" });

// ponytail: the dialog edits the first price only; an offer with several currencies keeps the
// others untouched. Add a price list editor when a second currency is authored from the console.
const draftOf = (offer: CreditOffer): Draft => ({
  meterKey: offer.meterKey,
  enabled: offer.enabled,
  currencyCode: offer.unitPrices[0]?.currencyCode ?? "USD",
  unitAmount: String(toMajorUnits(offer.unitPrices[0]?.unitAmountMinor ?? 0, offer.unitPrices[0]?.currencyCode ?? "USD")),
  minQuantity: String(offer.minQuantity),
  maxQuantity: String(offer.maxQuantity),
  expiryCount: offer.expiresAfter ? String(offer.expiresAfter.count) : "",
  expiryUnit: offer.expiresAfter?.unit ?? NEVER,
  taxRateBasisPoints: offer.taxRateBasisPoints == null ? "" : String(offer.taxRateBasisPoints),
  taxMode: offer.taxMode ?? "Exclusive",
  spendOrder: offer.spendOrder,
  packs: offer.packs.map((pack) => {
    const currency = offer.unitPrices[0]?.currencyCode ?? "USD";
    const price = pack.unitPrices.find((candidate) => candidate.currencyCode === currency) ?? pack.unitPrices[0];
    return {
      code: pack.code,
      displayName: pack.displayName,
      quantity: String(pack.quantity),
      unitAmount: price ? String(toMajorUnits(price.unitAmountMinor, price.currencyCode)) : "",
    };
  }),
});

const toPacks = (draft: Draft, existing: CreditOffer | null): CreditPack[] =>
  draft.packs.map((pack) => {
    const stored = existing?.packs.find((candidate) => candidate.code.toLowerCase() === pack.code.trim().toLowerCase());
    const otherPrices = (stored?.unitPrices ?? []).filter((price) => price.currencyCode !== draft.currencyCode);
    return {
      code: pack.code.trim(),
      displayName: pack.displayName.trim(),
      quantity: Number(pack.quantity),
      unitPrices: [{ currencyCode: draft.currencyCode, unitAmountMinor: toMinorUnits(Number(pack.unitAmount), draft.currencyCode) }, ...otherPrices],
    };
  });

export const toOfferRequest = (draft: Draft, existing: CreditOffer | null, organizationId?: string): CreditOfferRequest => {
  const otherPrices = (existing?.unitPrices ?? []).filter((price) => price.currencyCode !== draft.currencyCode);
  const taxRate = draft.taxRateBasisPoints.trim() === "" ? null : Number(draft.taxRateBasisPoints);

  return {
    organizationId,
    meterKey: draft.meterKey.trim(),
    enabled: draft.enabled,
    unitPrices: [{ currencyCode: draft.currencyCode, unitAmountMinor: toMinorUnits(Number(draft.unitAmount), draft.currencyCode) }, ...otherPrices],
    minQuantity: Number(draft.minQuantity),
    maxQuantity: Number(draft.maxQuantity),
    expiresAfter:
      draft.expiryUnit === NEVER || draft.expiryCount.trim() === ""
        ? null
        : { count: Number(draft.expiryCount), unit: draft.expiryUnit as CreditExpiryDuration["unit"] },
    taxRateBasisPoints: taxRate,
    taxMode: taxRate && taxRate > 0 ? draft.taxMode : null,
    spendOrder: draft.spendOrder,
    packs: toPacks(draft, existing),
  };
};

/**
 * Why the draft's prices cannot be sent, or null when they can. The offer's price and every pack's
 * price are typed in the currency's own units, so the same rule as a fixed discount applies: more
 * than zero, and no finer than the currency's minor unit.
 */
export const describePriceProblem = (draft: Draft): string | null =>
  describeDiscountAmountProblem(draft.unitAmount, draft.currencyCode) ??
  draft.packs.map((pack) => describeDiscountAmountProblem(pack.unitAmount, draft.currencyCode)).find(Boolean) ??
  null;

const describeOffer = (offer: CreditOffer, t: (key: string, values?: Record<string, string | number>) => string) =>
  [
    offer.unitPrices.map((price) => formatMoney(price.unitAmountMinor, price.currencyCode)).join(" / "),
    t("CREDIT_OFFERS_PAGE.BOUNDS", { min: offer.minQuantity, max: offer.maxQuantity }),
    offer.expiresAfter
      ? t("CREDIT_OFFERS_PAGE.EXPIRES_AFTER", { count: offer.expiresAfter.count, unit: offer.expiresAfter.unit })
      : t("CREDIT_OFFERS_PAGE.NEVER_EXPIRES"),
    offer.spendOrder === "OrganizationCreditsFirst"
      ? t("CREDIT_OFFERS_PAGE.SPEND_ORGANIZATION_FIRST")
      : t("CREDIT_OFFERS_PAGE.SPEND_SUBSCRIPTION_FIRST"),
    ...(offer.packs.length ? [t("CREDIT_OFFERS_PAGE.PACK_COUNT", { count: offer.packs.length })] : []),
  ].join(" · ");

/** Standalone credit offers (spec 005): one per meter per scope, priced and expired on their own terms. */
export const CreditOffersPage = () => {
  const { t } = useT(MODULE);
  const { itemId } = useParams();
  const organizationId = useOrganizationScope();
  const queryClient = useQueryClient();
  const offers = useCreditOffers(organizationId);
  const catalogue = useMeterCatalogue(organizationId);
  const [editing, setEditing] = useState<{ offer: CreditOffer | null; draft: Draft } | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: [CREDIT_OFFERS_QUERY_KEY] });

  const save = useMutation({
    mutationFn: ({ offer, draft }: { offer: CreditOffer | null; draft: Draft }) =>
      offer
        ? meterCatalogueService.updateOffer(offer.meterKey, toOfferRequest(draft, offer), offer.organizationId ?? undefined)
        : meterCatalogueService.createOffer(toOfferRequest(draft, null, organizationId)),
    onSuccess: async () => {
      setEditing(null);
      setProblem(null);
      await invalidate();
    },
    onError: (error) => setProblem(error instanceof Error ? error.message : t("CREDIT_OFFERS_PAGE.SAVE_FAILED")),
  });

  const toggle = useMutation({
    mutationFn: (offer: CreditOffer) =>
      meterCatalogueService.updateOffer(
        offer.meterKey,
        { ...toOfferRequest(draftOf(offer), offer), enabled: !offer.enabled },
        offer.organizationId ?? undefined,
      ),
    onSuccess: invalidate,
  });

  const draft = editing?.draft;
  const priceProblem = draft ? describePriceProblem(draft) : null;
  const update = (patch: Partial<Draft>) =>
    setEditing((current) => (current ? { ...current, draft: { ...current.draft, ...patch } } : current));
  const activeMeters = (catalogue.data ?? []).filter((entry) => entry.status === "Active");

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title={t("CREDIT_OFFERS_PAGE.TITLE")}
        description={t("CREDIT_OFFERS_PAGE.DESCRIPTION")}
        backTo={`/app/${itemId ?? ""}/subscription/plans`}
        actions={
          <Button onClick={() => setEditing({ offer: null, draft: emptyDraft() })}>
            {t("CREDIT_OFFERS_PAGE.NEW_OFFER")}
          </Button>
        }
      />

      <Card className="rounded-xl p-0">
        <div className="border-b p-4 font-semibold">{t("CREDIT_OFFERS_PAGE.OFFERS")}</div>
        {offers.isLoading ? (
          <p className="p-4 text-sm text-muted-foreground">{t("CREDIT_OFFERS_PAGE.LOADING")}</p>
        ) : offers.isError ? (
          <p className="p-4 text-sm text-destructive">
            {offers.error instanceof Error ? offers.error.message : t("CREDIT_OFFERS_PAGE.LOAD_FAILED")}
          </p>
        ) : !offers.data?.length ? (
          <p className="p-4 text-sm text-muted-foreground">{t("CREDIT_OFFERS_PAGE.EMPTY")}</p>
        ) : (
          <div className="divide-y">
            {offers.data.map((offer) => (
              <div key={offer.offerId} className="flex items-center justify-between gap-4 p-4">
                <div>
                  <p className="flex items-center gap-2 font-medium">
                    {offer.displayName} <code className="text-xs">{offer.meterKey}</code>
                    <Badge variant={offer.enabled ? "default" : "outline"}>
                      {offer.enabled ? t("CREDIT_OFFERS_PAGE.ON_SALE") : t("CREDIT_OFFERS_PAGE.PAUSED")}
                    </Badge>
                    {offer.organizationId && <Badge variant="secondary">{t("CREDIT_OFFERS_PAGE.OVERRIDE")}</Badge>}
                  </p>
                  <p className="text-xs text-muted-foreground">{describeOffer(offer, t)}</p>
                </div>
                <div className="flex items-center gap-3">
                  <Switch
                    aria-label={t("CREDIT_OFFERS_PAGE.ENABLED")}
                    checked={offer.enabled}
                    disabled={toggle.isPending}
                    onCheckedChange={() => toggle.mutate(offer)}
                  />
                  <Button size="sm" variant="outline" onClick={() => setEditing({ offer, draft: draftOf(offer) })}>
                    {t("common:EDIT")}
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
              <DialogTitle>{editing.offer ? t("CREDIT_OFFERS_PAGE.EDIT_OFFER") : t("CREDIT_OFFERS_PAGE.NEW_OFFER")}</DialogTitle>
              <DialogDescription>{t("CREDIT_OFFERS_PAGE.FORM_HINT")}</DialogDescription>
            </DialogHeader>
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1 sm:col-span-2">
                <Label>{t("CREDIT_OFFERS_PAGE.METER")}</Label>
                {editing.offer ? (
                  <Input value={draft.meterKey} disabled />
                ) : (
                  <Select value={draft.meterKey} onValueChange={(value) => update({ meterKey: value })}>
                    <SelectTrigger aria-label={t("CREDIT_OFFERS_PAGE.METER")}>
                      <SelectValue placeholder={t("CREDIT_OFFERS_PAGE.PICK_METER")} />
                    </SelectTrigger>
                    <SelectContent>
                      {activeMeters.map((entry) => (
                        <SelectItem key={entry.entryId} value={entry.meterKey}>
                          {entry.displayName} ({entry.meterKey})
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </div>
              <div className="space-y-1">
                <Label>{t("CREDIT_OFFERS_PAGE.CURRENCY")}</Label>
                <Select value={draft.currencyCode} onValueChange={(value) => update({ currencyCode: value })}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {SUBSCRIPTION_CURRENCY_OPTIONS.map((option) => (
                      <SelectItem key={option.code} value={option.code}>
                        {option.code} · {option.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1">
                <Label htmlFor="offer-unit-amount">{t("CREDIT_OFFERS_PAGE.UNIT_AMOUNT", { currency: draft.currencyCode })}</Label>
                <Input
                  id="offer-unit-amount"
                  type="number"
                  min={0}
                  step="any"
                  placeholder={exampleMinorAmount(draft.currencyCode)}
                  value={draft.unitAmount}
                  onChange={(event) => update({ unitAmount: event.target.value })}
                />
              </div>
              <div className="space-y-1">
                <Label htmlFor="offer-min">{t("CREDIT_OFFERS_PAGE.MIN_QUANTITY")}</Label>
                <Input id="offer-min" type="number" min={1} value={draft.minQuantity} onChange={(event) => update({ minQuantity: event.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="offer-max">{t("CREDIT_OFFERS_PAGE.MAX_QUANTITY")}</Label>
                <Input id="offer-max" type="number" min={1} value={draft.maxQuantity} onChange={(event) => update({ maxQuantity: event.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="offer-expiry-count">{t("CREDIT_OFFERS_PAGE.EXPIRES_AFTER_LABEL")}</Label>
                <Input
                  id="offer-expiry-count"
                  type="number"
                  min={1}
                  disabled={draft.expiryUnit === NEVER}
                  value={draft.expiryCount}
                  onChange={(event) => update({ expiryCount: event.target.value })}
                />
              </div>
              <div className="space-y-1">
                <Label>{t("CREDIT_OFFERS_PAGE.EXPIRY_UNIT")}</Label>
                <Select value={draft.expiryUnit} onValueChange={(value) => update({ expiryUnit: value })}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={NEVER}>{t("CREDIT_OFFERS_PAGE.NEVER_EXPIRES")}</SelectItem>
                    {EXPIRY_UNITS.map((unit) => (
                      <SelectItem key={unit} value={unit}>
                        {unit}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1">
                <Label htmlFor="offer-tax">{t("CREDIT_OFFERS_PAGE.TAX_RATE_BASIS_POINTS")}</Label>
                <Input id="offer-tax" type="number" min={0} max={10000} value={draft.taxRateBasisPoints} onChange={(event) => update({ taxRateBasisPoints: event.target.value })} />
              </div>
              <div className="space-y-1">
                <Label>{t("CREDIT_OFFERS_PAGE.TAX_MODE")}</Label>
                <Select value={draft.taxMode} onValueChange={(value) => update({ taxMode: value as Draft["taxMode"] })}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Exclusive">{t("CREDIT_OFFERS_PAGE.TAX_EXCLUSIVE")}</SelectItem>
                    <SelectItem value="Inclusive">{t("CREDIT_OFFERS_PAGE.TAX_INCLUSIVE")}</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1 sm:col-span-2">
                <Label>{t("CREDIT_OFFERS_PAGE.SPEND_ORDER")}</Label>
                <Select value={draft.spendOrder} onValueChange={(value) => update({ spendOrder: value as CreditSpendOrder })}>
                  <SelectTrigger aria-label={t("CREDIT_OFFERS_PAGE.SPEND_ORDER")}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="SubscriptionFirst">{t("CREDIT_OFFERS_PAGE.SPEND_SUBSCRIPTION_FIRST")}</SelectItem>
                    <SelectItem value="OrganizationCreditsFirst">{t("CREDIT_OFFERS_PAGE.SPEND_ORGANIZATION_FIRST")}</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="flex items-center gap-2 sm:col-span-2">
                <Switch id="offer-enabled" checked={draft.enabled} onCheckedChange={(checked) => update({ enabled: checked })} />
                <Label htmlFor="offer-enabled">{t("CREDIT_OFFERS_PAGE.ENABLED")}</Label>
              </div>
              <div className="space-y-2 sm:col-span-2">
                <div className="flex items-center justify-between">
                  <Label>{t("CREDIT_OFFERS_PAGE.PACKS")}</Label>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() => update({ packs: [...draft.packs, emptyPack()] })}
                  >
                    <Plus className="mr-1 h-3.5 w-3.5" />
                    {t("CREDIT_OFFERS_PAGE.ADD_PACK")}
                  </Button>
                </div>
                <p className="text-xs text-muted-foreground">{t("CREDIT_OFFERS_PAGE.PACKS_HINT", { currency: draft.currencyCode })}</p>
                {draft.packs.map((pack, index) => {
                  const set = (patch: Partial<PackDraft>) =>
                    update({ packs: draft.packs.map((row, at) => (at === index ? { ...row, ...patch } : row)) });
                  return (
                    <div key={index} className="grid gap-2 rounded-md border p-2 sm:grid-cols-[1fr_1fr_1fr_1fr_auto]">
                      <Input
                        aria-label={t("CREDIT_OFFERS_PAGE.PACK_CODE")}
                        placeholder={t("CREDIT_OFFERS_PAGE.PACK_CODE")}
                        value={pack.code}
                        onChange={(event) => set({ code: event.target.value })}
                      />
                      <Input
                        aria-label={t("CREDIT_OFFERS_PAGE.PACK_NAME")}
                        placeholder={t("CREDIT_OFFERS_PAGE.PACK_NAME")}
                        value={pack.displayName}
                        onChange={(event) => set({ displayName: event.target.value })}
                      />
                      <Input
                        aria-label={t("CREDIT_OFFERS_PAGE.PACK_QUANTITY")}
                        placeholder={t("CREDIT_OFFERS_PAGE.PACK_QUANTITY")}
                        type="number"
                        min={1}
                        value={pack.quantity}
                        onChange={(event) => set({ quantity: event.target.value })}
                      />
                      <Input
                        aria-label={t("CREDIT_OFFERS_PAGE.PACK_UNIT_AMOUNT", { currency: draft.currencyCode })}
                        placeholder={exampleMinorAmount(draft.currencyCode)}
                        type="number"
                        min={0}
                        step="any"
                        value={pack.unitAmount}
                        onChange={(event) => set({ unitAmount: event.target.value })}
                      />
                      <Button
                        type="button"
                        size="icon"
                        variant="ghost"
                        aria-label={t("CREDIT_OFFERS_PAGE.REMOVE_PACK")}
                        onClick={() => update({ packs: draft.packs.filter((_, at) => at !== index) })}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                  );
                })}
              </div>
            </div>
            {(problem ?? priceProblem) && (
              <p role="alert" className="text-sm text-destructive">
                {problem ?? priceProblem}
              </p>
            )}
            <DialogFooter>
              <Button variant="outline" onClick={() => setEditing(null)}>
                {t("common:CANCEL")}
              </Button>
              <Button disabled={save.isPending || !draft.meterKey || priceProblem !== null} onClick={() => save.mutate({ offer: editing.offer, draft })}>
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
