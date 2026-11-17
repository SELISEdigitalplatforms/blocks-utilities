import { useT } from "@seliseblocks/genesis-os";
import { WalletCards } from "lucide-react";
import { useParams, useSearchParams } from "react-router";
import { Card } from "@/components/ui-kits/card/card";
import { SubscriptionPlanPageHeader } from "../../subscription/components/subscription-plan-page-header";
import { ORGANIZATION_QUERY_PARAM } from "../../subscription/constants/subscription.constants";
import { DocumentRow } from "../../subscription/pages/subscription-invoices-page";
import { OrganizationCreditsCard } from "../components/organization-credits-card";
import { useOrganizationCreditInvoices } from "../hooks/use-organization-credits";

/**
 * An organization's standalone credits (spec 005): buy, grant, what it holds, and the invoices.
 * The organization comes from the URL like every other subscription page, so the console can look
 * at any organization and everyone else sees their own.
 */
export const OrganizationCreditsPage = () => {
  const { t } = useT("utilities.subscription-simulation");
  const { itemId } = useParams();
  const [searchParams] = useSearchParams();
  const organizationId = searchParams.get(ORGANIZATION_QUERY_PARAM) ?? undefined;
  const invoices = useOrganizationCreditInvoices(organizationId);

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title={t("ORGANIZATION_CREDITS_PAGE.TITLE")}
        description={t("ORGANIZATION_CREDITS_PAGE.DESCRIPTION")}
        icon={<WalletCards className="h-6 w-6" />}
        backTo={`/app/${itemId ?? ""}/subscription/plans`}
      />

      <OrganizationCreditsCard organizationId={organizationId} showSweeps={Boolean(organizationId)} />

      <Card className="rounded-xl p-0">
        <div className="border-b p-4 font-semibold sm:p-5">{t("ORGANIZATION_CREDITS_PAGE.INVOICES")}</div>
        <div className="space-y-3 p-4 sm:p-5">
          {invoices.isError ? (
            <p className="text-sm text-destructive">
              {invoices.error instanceof Error ? invoices.error.message : t("ORGANIZATION_CREDITS_PAGE.INVOICES_FAILED")}
            </p>
          ) : !invoices.data?.items.length ? (
            <p className="text-sm text-muted-foreground">{t("ORGANIZATION_CREDITS_PAGE.NO_INVOICES")}</p>
          ) : (
            invoices.data.items.map((document) => (
              <DocumentRow key={document.documentId} document={document} organizationId={organizationId} />
            ))
          )}
        </div>
      </Card>
    </main>
  );
};
