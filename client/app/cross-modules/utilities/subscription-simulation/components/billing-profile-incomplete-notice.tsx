import { AlertCircle, ArrowRight } from "lucide-react";
import { Link } from "react-router";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { useSubscriptionLink } from "../../subscription/hooks/use-subscription-link";
import type { BillingProfileGap } from "../../subscription/utilities/subscription-api-failure";
import { describeMissingProfileFields } from "../../subscription/utilities/financial-document-format";

/**
 * A refusal the reader can act on: what is missing, and the screen that collects it.
 *
 * This replaces printing the server's error envelope, which is what the dialogs used to do — a
 * subscriber saw a field list in JSON and no indication that the fix was one page away, or which
 * organization it was one page away for.
 *
 * The two halves are separated because they are somebody else's job each. The subscriber's own
 * details sit on their billing profile; a missing seller identity is the tenant's own configuration,
 * and a subscriber sent to fix that would be looking for a form that is not theirs.
 */
export const BillingProfileIncompleteNotice = ({
  gap,
  organizationId,
}: {
  gap: BillingProfileGap;
  /**
   * The organization the refused operation was for, so the link lands on the profile that was
   * actually refused rather than on whichever one the URL happens to be showing.
   */
  organizationId: string | undefined;
}) => {
  const subscriptionLink = useSubscriptionLink();

  return (
    <Card
      className="flex flex-col gap-3 border-amber-300 bg-amber-50 p-4"
      data-testid="billing-profile-incomplete"
    >
      <div className="flex items-start gap-3">
        <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
        <div className="space-y-1 text-sm">
          <p className="font-medium">This organization cannot be invoiced yet.</p>
          <p className="text-muted-foreground">
            A paid subscription is refused before anything is charged, because the invoice it would
            owe has to name who it is addressed to.
          </p>
        </div>
      </div>

      {gap.subscriberFields.length > 0 && (
        <div className="space-y-2 pl-8 text-sm">
          <p className="text-muted-foreground" data-testid="billing-profile-missing">
            {describeMissingProfileFields(gap.subscriberFields)}
          </p>
          <Button asChild size="sm" variant="outline">
            <Link to={subscriptionLink("billing-profile", organizationId)}>
              Complete the billing profile
              <ArrowRight className="ml-2 h-4 w-4" />
            </Link>
          </Button>
        </div>
      )}

      {gap.merchantMissing && (
        <div className="space-y-2 pl-8 text-sm">
          <p className="text-muted-foreground" data-testid="merchant-profile-missing">
            This tenant has not named itself as the seller, and an invoice has to state who issued
            it. That is set once, for the whole tenant, and not per organization.
          </p>
          <Button asChild size="sm" variant="outline">
            <Link to={subscriptionLink("merchant-profile", null)}>
              Name the seller
              <ArrowRight className="ml-2 h-4 w-4" />
            </Link>
          </Button>
        </div>
      )}
    </Card>
  );
};
