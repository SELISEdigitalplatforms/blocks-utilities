import { useState } from "react";
import { AlertCircle, ExternalLink, UserMinus, UserPlus, Users } from "lucide-react";
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
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { toast } from "@/hooks/use-toast";
import {
  SUBSCRIBER_SCOPE,
  type SubscriptionPlan,
} from "../../subscription/models/subscription-plan.model";
import { describePaceWindow } from "../../subscription/utilities/member-plan-format";
import { useWithdrawCancellation } from "../hooks/use-cancel-subscription";
import { useCancelPendingPlanChange } from "../hooks/use-change-subscription-plan";
import {
  useAssignMembers,
  useMemberBasedSubscriptions,
  useMembers,
  useReleaseMember,
} from "../hooks/use-members";
import type {
  PlaceUsage,
  SimulatedSubscription,
  SubscriptionMemberAssignment,
} from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";
import { CancelSubscriptionDialog } from "./cancel-subscription-dialog";
import { ChangePlanDialog } from "./change-plan-dialog";
import { ChangeQuantityDialog } from "./change-quantity-dialog";
import { CloseUsagePeriodDialog } from "./close-usage-period-dialog";
import { RunDueJobsDialog } from "./run-due-jobs-dialog";
import { SubscriptionStatusBadge } from "./subscription-status-badge";
import { describePendingMove } from "../utilities/plan-change-label";

const LIVE_STATUSES = new Set(["Trialing", "Active", "PastDue"]);

/**
 * People on the organization's user-wise subscriptions. Harness-only: it demonstrates the member
 * API the way every other card here demonstrates its own, and names the calls it makes. Customers
 * build their own assignment screens against the same endpoints.
 *
 * Renders nothing while the organization holds no user-wise subscription.
 */
export const MembersCard = ({
  plans,
  organizationId,
}: {
  plans: SubscriptionPlan[] | undefined;
  organizationId: string | undefined;
}) => {
  const { data: subscriptions, isError, error, refetch } =
    useMemberBasedSubscriptions(organizationId);

  if (!isError && !subscriptions?.length) {
    return null;
  }

  return (
    <Card className="rounded-xl p-0">
      <div className="border-b p-4 sm:p-5">
        <h2 className="font-semibold">Members</h2>
        <p className="text-xs text-muted-foreground">
          Subscriptions sold to each person, from{" "}
          <code className="mx-1 rounded bg-muted px-1">GET /api/subscriptions/member-based</code>.
          Places are read with{" "}
          <code className="mx-1 rounded bg-muted px-1">GET …/{"{id}"}/members</code>, filled with{" "}
          <code className="mx-1 rounded bg-muted px-1">POST …/{"{id}"}/members</code> and emptied
          with <code className="mx-1 rounded bg-muted px-1">DELETE …/{"{id}"}/members/{"{userId}"}</code>.
        </p>
      </div>

      <div className="space-y-4 p-4 sm:p-5">
        {isError ? (
          <div className="flex flex-col items-start gap-2">
            <div className="flex items-center gap-2 text-destructive">
              <AlertCircle className="h-4 w-4" />
              <span className="font-medium">Member-based subscriptions could not be loaded</span>
            </div>
            <p className="text-sm text-muted-foreground">
              {error instanceof Error ? error.message : "Try again in a moment."}
            </p>
            <Button size="sm" variant="outline" onClick={() => refetch()}>
              Try again
            </Button>
          </div>
        ) : (
          subscriptions?.map((subscription) => (
            <SubscriptionPlaces
              key={subscription.subscriptionId}
              subscription={subscription}
              plan={plans?.find((plan) => plan.code === subscription.planCode)}
              plans={plans}
              organizationId={organizationId}
              onRefresh={() => refetch()}
            />
          ))
        )}
      </div>
    </Card>
  );
};

const SubscriptionPlaces = ({
  subscription,
  plan,
  plans,
  organizationId,
  onRefresh,
}: {
  subscription: SimulatedSubscription;
  plan: SubscriptionPlan | undefined;
  plans: SubscriptionPlan[] | undefined;
  organizationId: string | undefined;
  onRefresh: () => void;
}) => {
  const [isAssigning, setIsAssigning] = useState(false);
  const [isChangingPlaces, setIsChangingPlaces] = useState(false);
  // Here because current never returns a user-wise subscription, so the current subscription
  // card — the only other place cancel is offered — can never show one.
  const [isCancelling, setIsCancelling] = useState(false);
  // The rest of what the current subscription card offers, here because a user-wise subscription
  // is never the current one — without them none of it could be exercised on one.
  const [isChangingPlan, setIsChangingPlan] = useState(false);
  const [isClosingPeriod, setIsClosingPeriod] = useState(false);
  const [isRunningJobs, setIsRunningJobs] = useState(false);
  const withdrawCancellation = useWithdrawCancellation();
  const keepPlan = useCancelPendingPlanChange();
  const isLive = LIVE_STATUSES.has(subscription.status);
  const { data: members, isLoading, isError, error } = useMembers(subscription.subscriptionId);
  const release = useReleaseMember();

  // Places 1..N, each with its holder or empty. A holder above the count is still shown — a
  // decrease scheduled for period end leaves somebody there until it lands.
  const placeCount = Math.max(
    members?.purchased ?? 0,
    ...(members?.seats ?? []).map((seat) => seat.seatNumber ?? 0),
  );
  const places = Array.from({ length: placeCount }, (_, index) => ({
    number: index + 1,
    holder: members?.seats.find((seat) => seat.seatNumber === index + 1),
  }));

  // Places a scheduled decrease is taking away. Still paid for, so still listed — but not
  // fillable, and shown as empty they invited an assignment the server then refused.
  const scheduled = members?.scheduledPlaces ?? null;
  const cutOn = members?.scheduledAtUtc
    ? new Date(members.scheduledAtUtc).toLocaleDateString()
    : null;
  const isFull = members !== undefined && members.available <= 0;

  const releaseHolder = async (userId: string) => {
    try {
      await release.mutateAsync({ subscriptionId: subscription.subscriptionId, userId });
      toast({ variant: "success", title: "Place released", description: `${userId} is off it.` });
    } catch (failure) {
      toast({
        variant: "destructive",
        title: "Could not release",
        description: failure instanceof Error ? failure.message : "Try again.",
      });
    }
  };

  return (
    <div className="rounded-lg border">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b p-3">
        <div className="flex flex-wrap items-center gap-2">
          <Users className="h-4 w-4 text-blocks-primary-600" />
          <span className="font-medium">{subscription.planName}</span>
          <SubscriptionStatusBadge status={subscription.status} />
          {members ? (
            <span className="text-xs text-muted-foreground">
              {members.held} of {members.purchased} places held
              {scheduled !== null ? ` · drops to ${scheduled} on ${cutOn}` : ""}
            </span>
          ) : null}
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            variant="outline"
            disabled={!isLive}
            onClick={() => setIsChangingPlan(true)}
          >
            Change plan
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={!isLive}
            onClick={() => setIsChangingPlaces(true)}
          >
            Change places
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={!isLive}
            onClick={() => setIsCancelling(true)}
          >
            Cancel
          </Button>
          <Button
            size="sm"
            disabled={!isLive || isFull}
            title={isFull ? "Every place that can be filled is taken." : undefined}
            onClick={() => setIsAssigning(true)}
          >
            <UserPlus className="mr-1 h-4 w-4" />
            Assign
          </Button>
        </div>
      </div>

      {subscription.cancelAtPeriodEnd || subscription.pendingPlanChange ? (
        <div className="flex flex-wrap items-center justify-between gap-2 border-b p-3 text-xs">
          <span className="text-muted-foreground">
            {subscription.cancelAtPeriodEnd
              ? `Cancellation scheduled for ${new Date(subscription.currentPeriodEndUtc).toLocaleDateString()}.`
              : `Moving to ${describePendingMove(subscription, subscription.pendingPlanChange!)} on ${new Date(
                  subscription.pendingPlanChange!.effectiveAtUtc,
                ).toLocaleDateString()}.`}
          </span>
          <Button
            size="sm"
            variant="outline"
            disabled={withdrawCancellation.isPending || keepPlan.isPending}
            onClick={() =>
              (subscription.cancelAtPeriodEnd ? withdrawCancellation : keepPlan).mutate(
                { subscriptionId: subscription.subscriptionId, organizationId },
                {
                  onError: (failure) =>
                    toast({
                      variant: "destructive",
                      title: "Could not undo it",
                      description: failure instanceof Error ? failure.message : "Try again.",
                    }),
                },
              )
            }
          >
            {subscription.cancelAtPeriodEnd ? "Keep subscription" : "Keep current plan"}
          </Button>
        </div>
      ) : null}

      <div className="flex flex-wrap items-center gap-2 border-b px-3 py-2 text-xs text-muted-foreground">
        Harness:
        <Button size="sm" variant="ghost" onClick={() => setIsClosingPeriod(true)}>
          Close usage period
        </Button>
        <Button size="sm" variant="ghost" onClick={() => setIsRunningJobs(true)}>
          Run due jobs
        </Button>
      </div>

      {!isLive && subscription.checkoutUrl ? (
        <div className="flex flex-wrap items-center justify-between gap-2 border-b p-3 text-xs text-muted-foreground">
          <span>Waiting on its first payment — nobody can be given a place until it is paid.</span>
          <Button size="sm" asChild>
            <a href={subscription.checkoutUrl} target="_blank" rel="noreferrer">
              Continue checkout
              <ExternalLink className="ml-2 h-3.5 w-3.5" />
            </a>
          </Button>
        </div>
      ) : null}

      <div className="p-3">
        {isLoading ? (
          <Skeleton className="h-16 w-full rounded-md" />
        ) : isError ? (
          <p className="text-sm text-destructive">
            {error instanceof Error ? error.message : "The members could not be loaded."}
          </p>
        ) : places.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            This subscription has no places — a flat price with no maximum has no ceiling to give
            out.
          </p>
        ) : (
          <ul className="divide-y text-sm">
            {places.map(({ number, holder }) => (
              <li key={number} className="py-1.5">
                <div className="flex items-center justify-between gap-2">
                <span className="flex items-center gap-3">
                  <span className="w-16 text-xs text-muted-foreground">Place {number}</span>
                  {holder ? (
                    <span className="font-mono text-xs">{holder.userId}</span>
                  ) : scheduled !== null && number > scheduled ? (
                    <span className="text-xs italic text-muted-foreground">
                      Removed on {cutOn}
                    </span>
                  ) : (
                    <span className="text-xs italic text-muted-foreground">Empty</span>
                  )}
                </span>
                {holder ? (
                  <Button
                    size="sm"
                    variant="ghost"
                    disabled={release.isPending}
                    onClick={() => releaseHolder(holder.userId)}
                    aria-label={`Release ${holder.userId}`}
                  >
                    <UserMinus className="mr-1 h-4 w-4" />
                    Release
                  </Button>
                ) : null}
                </div>
                <PlaceUsageLines
                  usage={members?.usage?.filter((entry) => entry.seatNumber === number) ?? []}
                />
              </li>
            ))}
          </ul>
        )}
      </div>

      {isAssigning ? (
        <AssignMembersDialog
          subscriptionId={subscription.subscriptionId}
          open={isAssigning}
          onOpenChange={setIsAssigning}
        />
      ) : null}

      {isChangingPlaces ? (
        <ChangeQuantityDialog
          subscription={subscription}
          currentPlan={plan}
          organizationId={organizationId}
          open={isChangingPlaces}
          onOpenChange={setIsChangingPlaces}
          onRefresh={onRefresh}
        />
      ) : null}

      {isChangingPlan ? (
        <ChangePlanDialog
          subscription={subscription}
          currentPlan={plan}
          // A plan change cannot cross to a plan for the organization; the server refuses it.
          plans={(plans ?? []).filter((candidate) => candidate.subscriberScope === SUBSCRIBER_SCOPE.User)}
          organizationId={organizationId}
          open={isChangingPlan}
          onOpenChange={setIsChangingPlan}
        />
      ) : null}

      {isClosingPeriod ? (
        <CloseUsagePeriodDialog
          subscriptionId={subscription.subscriptionId}
          organizationId={organizationId}
          open={isClosingPeriod}
          onOpenChange={setIsClosingPeriod}
          onResult={harnessToast}
        />
      ) : null}

      {isRunningJobs ? (
        <RunDueJobsDialog
          subscriptionId={subscription.subscriptionId}
          organizationId={organizationId}
          open={isRunningJobs}
          onOpenChange={setIsRunningJobs}
          onResult={harnessToast}
        />
      ) : null}

      {isCancelling ? (
        <CancelSubscriptionDialog
          subscription={subscription}
          organizationId={organizationId}
          open={isCancelling}
          onOpenChange={setIsCancelling}
          onCanceled={reportPlacesAfterCancel}
        />
      ) : null}
    </div>
  );
};

/**
 * What a place has spent of each meter and each pace, from the usage projection. The place's own
 * figures: they include whatever an earlier holder spent in the same window.
 */
const PlaceUsageLines = ({ usage }: { usage: PlaceUsage[] }) =>
  usage.length === 0 ? null : (
    <ul className="ml-[4.75rem] mt-1 space-y-0.5 text-xs text-muted-foreground">
      {usage.map((entry) => (
        <li key={entry.meterKey}>
          <span className="font-medium text-foreground">{entry.meterKey}</span>{" "}
          {entry.used.toLocaleString()} / {entry.included.toLocaleString()} {entry.unitLabel}
          {entry.subLimits.map((pace) => (
            <span
              key={`${pace.window}-${pace.windowCount}-${pace.rolling}`}
              className={pace.exceeded ? "text-destructive" : undefined}
            >
              {" · "}
              {pace.used.toLocaleString()} / {pace.quantity.toLocaleString()}{" "}
              {describePaceWindow(pace.window, pace.windowCount, pace.rolling)}
            </span>
          ))}
        </li>
      ))}
    </ul>
  );

/**
 * Reads the roster back once a cancellation has ended a subscription, so whether its places were
 * released can be seen: an ended subscription drops out of this card, and nothing else here would
 * show its members again.
 */
const reportPlacesAfterCancel = async (canceled: SimulatedSubscription) => {
  if (canceled.status !== "Canceled") {
    return;
  }

  try {
    const members = await subscriptionSimulationService.listMembers(canceled.subscriptionId);

    toast(
      members.held === 0
        ? {
            variant: "success",
            title: "Every place released",
            description: `${canceled.planName} has ended and nobody holds a place on it.`,
          }
        : {
            variant: "destructive",
            title: `${members.held} place${members.held === 1 ? "" : "s"} still held`,
            description: `${canceled.planName} has ended but still lists ${members.seats
              .map((seat) => seat.userId)
              .join(", ")}.`,
          },
    );
  } catch (failure) {
    toast({
      variant: "destructive",
      title: "Could not read the places back",
      description: failure instanceof Error ? failure.message : "Try again.",
    });
  }
};

/** The current subscription card shows a harness run in full; here a line is enough. */
const harnessToast = () =>
  toast({ variant: "success", title: "Harness run finished", description: "Re-read the card for its effect." });

/** One name per line; commas and spaces also separate, since ids are pasted from anywhere. */
const parseUserIds = (text: string): string[] => [
  // Each name once: the server places a repeated name once too, and a button saying "Assign 5
  // people" for four distinct names promised a place that was never going to be handed out.
  ...new Set(
    text
      .split(/[\s,]+/)
      .map((userId) => userId.trim())
      .filter(Boolean),
  ),
];

/**
 * Assigns a batch in one call and shows what became of every name. Kept open on success, because
 * the answer is per person: a batch that seats two and refuses eight is a success, and collapsing
 * it to a toast would hide the eight.
 */
export const AssignMembersDialog = ({
  subscriptionId,
  open,
  onOpenChange,
}: {
  subscriptionId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) => {
  const [text, setText] = useState("");
  const [outcome, setOutcome] = useState<SubscriptionMemberAssignment | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const assign = useAssignMembers();
  const userIds = parseUserIds(text);

  const submit = async () => {
    setFailure(null);

    try {
      setOutcome(await assign.mutateAsync({ subscriptionId, userIds }));
    } catch (error) {
      // Only a refusal of the whole call lands here — a subscription that stopped granting, say.
      setFailure(error instanceof Error ? error.message : "Nobody could be assigned.");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(next) => !assign.isPending && onOpenChange(next)}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Assign people</DialogTitle>
          <DialogDescription>
            One user id per line, sent as a single{" "}
            <code className="rounded bg-muted px-1">POST …/members</code>. Each person gets the free
            place with the most allowance left.
          </DialogDescription>
        </DialogHeader>

        {outcome ? (
          <AssignmentOutcome outcome={outcome} />
        ) : (
          <Textarea
            value={text}
            onChange={(event) => setText(event.target.value)}
            rows={6}
            placeholder={"user-1\nuser-2"}
            spellCheck={false}
            className="font-mono text-xs"
            aria-label="User ids"
          />
        )}

        {failure ? (
          <p role="alert" className="text-sm text-destructive">
            {failure}
          </p>
        ) : null}

        <DialogFooter>
          {outcome ? (
            <>
              <Button
                variant="outline"
                onClick={() => {
                  setOutcome(null);
                  setText("");
                }}
              >
                Assign more
              </Button>
              <Button onClick={() => onOpenChange(false)}>Done</Button>
            </>
          ) : (
            <Button onClick={submit} disabled={userIds.length === 0 || assign.isPending}>
              {assign.isPending
                ? "Assigning…"
                : `Assign ${userIds.length ? `${userIds.length} ` : ""}${userIds.length === 1 ? "person" : "people"}`}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

const AssignmentOutcome = ({ outcome }: { outcome: SubscriptionMemberAssignment }) => (
  <div className="space-y-3 text-sm">
    <section aria-label="Assigned">
      <h3 className="font-medium">Assigned ({outcome.assigned.length})</h3>
      {outcome.assigned.length ? (
        <ul className="mt-1 space-y-0.5">
          {outcome.assigned.map((member) => (
            <li key={member.userId} className="font-mono text-xs">
              {member.userId}
              {member.seatNumber ? (
                <span className="ml-2 font-sans text-muted-foreground">
                  place {member.seatNumber}
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-xs text-muted-foreground">Nobody.</p>
      )}
    </section>

    {outcome.refused.length ? (
      <section aria-label="Refused">
        <h3 className="font-medium text-destructive">Refused ({outcome.refused.length})</h3>
        <ul className="mt-1 space-y-1">
          {outcome.refused.map((refusal) => (
            <li key={refusal.userId} className="text-xs">
              <span className="font-mono">{refusal.userId}</span>{" "}
              <code className="rounded bg-muted px-1">{refusal.reasonCode}</code>
              <span className="block text-muted-foreground">{refusal.reason}</span>
            </li>
          ))}
        </ul>
      </section>
    ) : null}
  </div>
);
