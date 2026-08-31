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
import { Badge } from "@/components/ui-kits/badge/badge";
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
      className={`flex h-full flex-col rounded-xl transition ${
        isArchived
          ? // Muted, not faded away: an archived plan is still read, and text at 60% opacity on a
            // muted panel fails contrast. The tint carries the state, the text keeps its colour.
            "border-dashed bg-muted/40"
          : "hover:border-blocks-primary-300 hover:shadow-md"
      }`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="truncate font-semibold">
            <Link
              to={detailPath}
              className="rounded outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring"
            >
              {plan.displayName}
            </Link>
          </h3>
          <p className="truncate font-mono text-xs text-muted-foreground">
            {plan.code}
          </p>
        </div>

        {hasMenu ? (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                className="h-8 w-8 shrink-0"
                aria-label={`Actions for ${plan.displayName}`}
              >
                <MoreVertical className="h-4 w-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem asChild>
                <Link to={detailPath}>View details</Link>
              </DropdownMenuItem>
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
        <Badge variant={isArchived ? "secondary" : "success"} className="font-normal">
          {isArchived ? "Archived" : "Active"}
        </Badge>
        <Badge variant="outline" className="font-normal">
          {organizationLabel}
        </Badge>
        {plan.familyCode ? (
          <Badge variant="outline" className="font-normal">
            {plan.familyCode}
            {typeof plan.familyRank === "number" ? ` · level ${plan.familyRank}` : ""}
          </Badge>
        ) : null}
        {trialLabel ? (
          <Badge variant="info" className="font-normal">
            {trialLabel}
          </Badge>
        ) : null}
      </div>

      {plan.description ? (
        // Two lines, then clipped. A description is authored freely and a card that grows to fit
        // one breaks the grid for every card beside it.
        <p className="mt-3 line-clamp-2 text-sm text-muted-foreground">
          {plan.description}
        </p>
      ) : null}

      <div className="mt-3 space-y-1">
        <p className="text-sm font-medium">
          {cheapestPrice
            ? `From ${formatPrice(cheapestPrice)}`
            : "No price configured yet"}
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
        <div className="mt-3 flex flex-wrap gap-1.5">
          {counts.map((entry) => (
            <Badge
              key={entry.label}
              variant="secondary"
              className="gap-1 font-normal"
            >
              <entry.icon className="h-3 w-3" />
              {entry.value} {entry.label}
              {entry.value === 1 ? "" : "s"}
            </Badge>
          ))}
        </div>
      ) : null}

      {/* Pushed to the foot so every card in a row ends with its action on the same line, however
          much description or how many badges the plans above differ by. */}
      <div className="mt-auto pt-4">
        <Button variant="outline" size="sm" className="w-full" asChild>
          <Link to={detailPath}>
            View details
            <ArrowRight className="ml-2 h-4 w-4" />
          </Link>
        </Button>
      </div>
    </Card>
  );
};
