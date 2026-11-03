import { CirclePlus, WalletCards } from "lucide-react";
import { Link, useParams } from "react-router";
import { Button } from "@/components/ui-kits/button/button";
import { StoredPaymentMethodsSection } from "../components/stored-payment-methods-section";
import { useT } from "@seliseblocks/genesis-os";

export const StoredPaymentMethodsPage = () => {
  const { t } = useT("utilities.payment");
  const { itemId } = useParams();
  const createPaymentPath = `/app/${itemId ?? ""}/payment/create`;

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <section className="relative overflow-hidden rounded-2xl border bg-gradient-to-br from-blocks-primary-shades-100 via-card to-blocks-secondary-50 p-5 shadow-sm sm:p-7">
        <div className="absolute -right-16 -top-20 h-52 w-52 rounded-full bg-blocks-primary-100/30 blur-3xl" />
        <div className="relative flex flex-col justify-between gap-5 sm:flex-row sm:items-center">
          <div className="flex items-start gap-4">
            <div className="rounded-xl bg-blocks-primary-600 p-3 text-white shadow-sm">
              <WalletCards className="h-6 w-6" />
            </div>
            <div>
              <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">
                {t("STORED_PAYMENT_METHODS_PAGE.SAVED_CARDS")}
              </h1>
              <p className="mt-1 max-w-2xl text-sm text-muted-foreground sm:text-base">
                {t("STORED_PAYMENT_METHODS_PAGE.VIEW_AND_REMOVE_PAYMENT_METHODS")}
              </p>
            </div>
          </div>

          <Button asChild className="self-start sm:self-center">
            <Link to={createPaymentPath}>
              <CirclePlus className="mr-2 h-4 w-4" />
              {t("STORED_PAYMENT_METHODS_PAGE.CREATE_PAYMENT")}
            </Link>
          </Button>
        </div>
      </section>

      <StoredPaymentMethodsSection />
    </main>
  );
};
