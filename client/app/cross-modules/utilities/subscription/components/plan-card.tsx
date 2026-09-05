import {
  Archive,
  ArrowRight,
  Copy,
  Gauge,
  Layers,
  MoreVertical,
  Pencil,
  Tag,
  Ticket,
} from "lucide-react";
import { Link } from "react-router";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { formatInterval, formatPrice } from "../utilities/subscription-format";
import { describeTrialDuration } from "../utilities/trial-duration-label";

/**
 * A small tinted pill with a status dot, used for Active/Archived rather than a plain solid
 * badge — the dot carries the state at a glance, the tint keeps it legible without shouting.
 */
const StatusPill = ({ isArchived }: { isArchived: boolean }) => (
  <span
    className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-semibold tracking-wide ${
      isArchived
        ? "border-border-default bg-muted text-muted-foreground"
        : "border-success/20 bg-success/10 text-success"
    }`}
  >
    <span
      className={`h-1.5 w-1.5 rounded-full ${
        isArchived ? "bg-muted-foreground" : "bg-success"
      }`}
    />
    {isArchived ? "Archived" : "Active"}
  </span>
);

/** One badge shape used for every other tag on the card, so the eye reads them as one family. */
const Pill = ({
  children,
  tone = "neutral",
}: {
  children: React.ReactNode;
  tone?: "neutral" | "accent";
}) => (
  <span
    className={`inline-flex items-center rounded-full border px-2.5 py-1 text-[11px] font-medium tracking-wide ${
      tone === "accent"
        ? "border-blocks-secondary-200 bg-blocks-secondary-50 text-blocks-secondary-700"
        : "border-border-default bg-muted/60 text-muted-foreground"
    }`}
  >
    {children}
  </span>
);

/**
 * One plan in the catalogue.
 *
 * The whole card was a link before, which left nowhere to put an action: a menu inside an anchor
 * either navigates when it opens or has to fight the anchor to stop it. The card is a plain
 * container now, with the name as the link and an explicit action at the foot, so the menu can sit
 * beside them without either interfering with the other.
 */
export const PlanCard = ({
  plan,
  organizationLabel,
  detailPath,
  editPath,
  duplicatePath,
  onArchive,
}: {
  plan: SubscriptionPlan;
  organizationLabel: string;
  detailPath: string;
  editPath?: string;
  /**
   * The create page. Duplication carries the whole plan in router state, the way the detail page
   * already does it — the builder reads it from there and seeds itself, so there is no
   * duplicate-specific route to point at.
   */
  duplicatePath?: string;
  /** Absent for an archived plan, and for a caller that offers no archiving at all. */
  onArchive?: (plan: SubscriptionPlan) => void;
}) => {
  const isArchived = plan.status === "Archived";
  const cheapestPrice = plan.prices.reduce<SubscriptionPlan["prices"][number] | null>(
    (cheapest, price) =>
      cheapest === null || price.unitAmountMinor < cheapest.unitAmountMinor
        ? price
        : cheapest,
    null,
  );
  const trialLabel = describeTrialDuration(plan);

  // One entry per distinct cadence rather than one per price: three currencies billed monthly is
  // one choice of cadence, and listing it three times says something untrue about the plan.
  const cadences = [
    ...new Map(
      plan.prices.map((price) => [
        `${price.interval}:${price.intervalCount}`,
        formatInterval(price.interval, price.intervalCount),
      ]),
    ).values(),
  ];

  const meteredWithOverage = plan.meters.filter(
    (meter) => meter.overageAllowed,
  ).length;

  const counts = [
    { label: "price", value: plan.prices.length, icon: Tag },
    { label: "quantity item", value: plan.quantityItems.length, icon: Layers },
    { label: "meter", value: plan.meters.length, icon: Gauge },
    { label: "entitlement", value: plan.entitlements.length, icon: Ticket },
  ].filter((entry) => entry.value > 0);

  const hasMenu = Boolean(editPath || duplicatePath || onArchive);

  return (
    <Card
      className={`group relative flex h-full flex-col overflow-hidden rounded-2xl p-5 transition-all duration-300 ease-out ${
        isArchived
          ? // Muted, not faded away: an archived plan is still read, and text at 60% opacity on a
            // muted panel fails contrast. The tint carries the state, the text keeps its colour.
            "border-dashed bg-muted/40"
          : "hover:-translate-y-1 hover:border-blocks-primary-200 hover:shadow-xl hover:shadow-blocks-primary-100/60 focus-within:-translate-y-1 focus-within:border-blocks-primary-200 focus-within:shadow-xl focus-within:shadow-blocks-primary-100/60 motion-reduce:hover:translate-y-0 motion-reduce:focus-within:translate-y-0"
      }`}
    >
      {/* A quiet accent bar along the top edge, brightened on hover — the card's own signature
          rather than a border colour change alone. */}
      {!isArchived && (
        <span
          aria-hidden
          className="absolute inset-x-0 top-0 h-1 origin-left scale-x-0 bg-gradient-to-r from-blocks-primary-500 to-blocks-secondary-500 transition-transform duration-300 ease-out group-hover:scale-x-100"
        />
      )}

      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="truncate text-[15px] font-semibold tracking-tight">
            <Link
              to={detailPath}
              className="rounded outline-none transition-colors hover:text-blocks-primary-600 focus-visible:ring-2 focus-visible:ring-ring"
            >
              {plan.displayName}
            </Link>
          </h3>
          <p className="mt-0.5 truncate font-mono text-[11px] uppercase tracking-wider text-muted-foreground/80">
            {plan.code}
          </p>
        </div>

        {hasMenu ? (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                className="h-8 w-8 shrink-0 rounded-full text-muted-foreground hover:bg-blocks-primary-50 hover:text-blocks-primary-700"
                aria-label={`Actions for ${plan.displayName}`}
              >
                <MoreVertical className="h-4 w-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              {/* View details already lives on the title and the footer button — a third
                  identical link here would just be another way to the same place. */}
              {/* Absent rather than disabled for an archived plan: a greyed-out Edit invites a
                  second click and an explanation, where nothing at all says it is not on offer. */}
              {editPath && !isArchived ? (
                <DropdownMenuItem asChild>
                  <Link to={editPath}>
                    <Pencil className="mr-2 h-4 w-4" />
                    Edit
                  </Link>
                </DropdownMenuItem>
              ) : null}
              {duplicatePath ? (
                <DropdownMenuItem asChild>
                  <Link to={duplicatePath} state={{ duplicatePlan: plan }}>
                    <Copy className="mr-2 h-4 w-4" />
                    {isArchived ? "Duplicate as new plan" : "Duplicate"}
                  </Link>
                </DropdownMenuItem>
              ) : null}
              {onArchive && !isArchived ? (
                <>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem
                    className="text-destructive focus:text-destructive"
                    onSelect={() => onArchive(plan)}
                  >
                    <Archive className="mr-2 h-4 w-4" />
                    Archive
                  </DropdownMenuItem>
                </>
              ) : null}
            </DropdownMenuContent>
          </DropdownMenu>
        ) : null}
      </div>

      <div className="mt-3 flex flex-wrap gap-1.5">
        <StatusPill isArchived={isArchived} />
        <Pill>{organizationLabel}</Pill>
        {plan.familyCode ? (
          <Pill tone="accent">
            <span className="break-words">
              {plan.familyCode}
              {typeof plan.familyRank === "number" ? ` · level ${plan.familyRank}` : ""}
            </span>
          </Pill>
        ) : null}
        {trialLabel ? (
          <span className="inline-flex items-center rounded-full border border-blocks-primary-200 bg-blocks-primary-50 px-2.5 py-1 text-[11px] font-medium tracking-wide text-blocks-primary-700">
            {trialLabel}
          </span>
        ) : null}
      </div>

      {plan.description ? (
        // Two lines, then clipped. A description is authored freely and a card that grows to fit
        // one breaks the grid for every card beside it.
        <p className="mt-3 line-clamp-2 text-sm leading-relaxed text-muted-foreground">
          {plan.description}
        </p>
      ) : null}

      <div className="mt-4 space-y-1 border-t border-dashed border-border/70 pt-3">
        <p className="text-xl font-bold tracking-tight">
          {cheapestPrice ? (
            <>
              <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                From{" "}
              </span>
              <span className="bg-gradient-to-r from-blocks-primary-600 to-blocks-secondary-600 bg-clip-text text-transparent">
                {formatPrice(cheapestPrice)}
              </span>
            </>
          ) : (
            <span className="text-sm font-medium text-muted-foreground">
              No price configured yet
            </span>
          )}
        </p>
        {cadences.length > 0 ? (
          <p className="text-xs text-muted-foreground">
            Billed {cadences.join(", ")}
          </p>
        ) : null}
        {meteredWithOverage > 0 ? (
          <p className="text-xs text-muted-foreground">
            {meteredWithOverage} metered{" "}
            {meteredWithOverage === 1 ? "allowance" : "allowances"} with overage
            pricing
          </p>
        ) : null}
      </div>

      {counts.length > 0 ? (
        <div className="mt-3 flex flex-wrap gap-2">
          {counts.map((entry) => (
            <span
              key={entry.label}
              className="inline-flex items-center gap-1.5 rounded-full border border-border/60 bg-muted/40 py-1 pl-1 pr-2.5 text-[11px] font-medium text-muted-foreground transition-colors group-hover:border-blocks-primary-100"
            >
              <span className="flex h-4 w-4 items-center justify-center rounded-full bg-blocks-primary-50 text-blocks-primary-600">
                <entry.icon className="h-2.5 w-2.5" />
              </span>
              {entry.value} {entry.label}
              {entry.value === 1 ? "" : "s"}
            </span>
          ))}
        </div>
      ) : null}

      {/* Pushed to the foot so every card in a row ends with its action on the same line, however
          much description or how many badges the plans above differ by. */}
      <div className="mt-auto pt-4">
        <Button
          variant="outline"
          size="sm"
          className="group/cta relative w-full overflow-hidden border-blocks-primary-200 font-medium text-blocks-primary-700 transition-colors duration-300 hover:border-transparent hover:text-primary-foreground"
          asChild
        >
          <Link to={detailPath}>
            <span
              aria-hidden
              className="absolute inset-0 -z-10 origin-left scale-x-0 bg-gradient-to-r from-blocks-primary-600 to-blocks-secondary-600 transition-transform duration-300 ease-out group-hover/cta:scale-x-100"
            />
            View details
            <ArrowRight className="ml-2 h-4 w-4 transition-transform duration-300 group-hover/cta:translate-x-1" />
          </Link>
        </Button>
      </div>
    </Card>
  );
};
