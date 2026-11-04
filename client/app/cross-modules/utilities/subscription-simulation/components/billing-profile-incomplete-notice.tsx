import { AlertCircle, ArrowRight } from "lucide-react";
import { Link } from "react-router";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { useSubscriptionLink } from "../../subscription/hooks/use-subscription-link";
import type { BillingProfileGap } from "../../subscription/utilities/subscription-api-failure";
import { describeMissingProfileFields } from "../../subscription/utilities/financial-document-format";
import { useT } from "@seliseblocks/genesis-os";

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
  message,
}: {
  gap: BillingProfileGap;
  /**
   * The organization the refused operation was for, so the link lands on the profile that was
   * actually refused rather than on whichever one the URL happens to be showing.
   */
  organizationId: string | undefined;
  /**
   * The server's own sentence, where there is one to show.
   *
   * A preview reports this as a blocker with its own wording, and a refused subscribe carries it on
   * the error. Shown in place of the line below rather than alongside it, because saying the same
   * thing twice in two voices reads as two problems.
   */
  message?: string;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const subscriptionLink = useSubscriptionLink();

  return (
    <Card
      className="flex flex-col gap-3 border-amber-300 bg-amber-50 p-4"
      data-testid="billing-profile-incomplete"
    >
      <div className="flex items-start gap-3">
        <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
        <div className="space-y-1 text-sm">
          <p className="font-medium">{t("BILLING_PROFILE_INCOMPLETE_NOTICE.THIS_ORGANIZATION_CANNOT_BE")}</p>
          <p className="text-muted-foreground">
            {message ??
              t("BILLING_PROFILE_INCOMPLETE_NOTICE.A_PAID_SUBSCRIPTION_IS_REFUSED")}
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
              {t("BILLING_PROFILE_INCOMPLETE_NOTICE.COMPLETE_THE_BILLING_PROFILE")}
              <ArrowRight className="ml-2 h-4 w-4" />
            </Link>
          </Button>
        </div>
      )}

      {gap.merchantMissing && (
        <div className="space-y-2 pl-8 text-sm">
          <p className="text-muted-foreground" data-testid="merchant-profile-missing">
            {t("BILLING_PROFILE_INCOMPLETE_NOTICE.THIS_TENANT_HAS_NOT_NAMED")}
          </p>
          <Button asChild size="sm" variant="outline">
            <Link to={subscriptionLink("merchant-profile", null)}>
              {t("BILLING_PROFILE_INCOMPLETE_NOTICE.NAME_THE_SELLER")}
              <ArrowRight className="ml-2 h-4 w-4" />
            </Link>
          </Button>
        </div>
      )}
    </Card>
  );
};
