import { AlertCircle, RefreshCw, Settings2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useT } from "@seliseblocks/genesis-os";

export const PaymentProviderPageSkeleton = () => {
  const { t } = useT("utilities.payment");
  return (
  <div className="space-y-5" aria-label={t("PAYMENT_PROVIDER_STATE.LOADING_PAYMENT_PROVIDER")}>
    <Skeleton className="h-40 rounded-2xl" />
    <Card className="space-y-5 rounded-xl p-6">
      <Skeleton className="h-7 w-56" />
      <div className="grid gap-5 sm:grid-cols-2">
        {Array.from({ length: 6 }, (_, index) => (
          <div key={index} className="space-y-2">
            <Skeleton className="h-4 w-28" />
            <Skeleton className="h-10 w-full" />
          </div>
        ))}
      </div>
    </Card>
  </div>
);
};

interface PaymentProviderLoadErrorProps {
  message: string;
  onRetry: () => void;
}

export const PaymentProviderLoadError = ({
  message,
  onRetry,
}: PaymentProviderLoadErrorProps) => {
  const { t } = useT("utilities.payment");
  return (
  <Card className="flex min-h-80 flex-col items-center justify-center rounded-xl px-6 text-center">
    <span className="rounded-full bg-destructive/10 p-4 text-destructive">
      <AlertCircle className="h-7 w-7" />
    </span>
    <h2 className="mt-4 text-lg font-semibold">
      {t("PAYMENT_PROVIDER_STATE.PAYMENT_PROVIDER_UNAVAILABLE")}
    </h2>
    <p className="mt-1 max-w-md text-sm text-muted-foreground">
      {message}
    </p>
    <Button className="mt-5" variant="outline" onClick={onRetry}>
      <RefreshCw className="mr-2 h-4 w-4" />
      {t("PAYMENT_PROVIDER_STATE.TRY_AGAIN")}
    </Button>
  </Card>
);
};

export const PaymentProviderNotFound = () => {
  const { t } = useT("utilities.payment");
  return (
  <Card className="flex min-h-80 flex-col items-center justify-center rounded-xl px-6 text-center">
    <span className="rounded-full bg-muted p-4 text-muted-foreground">
      <Settings2 className="h-7 w-7" />
    </span>
    <h2 className="mt-4 text-lg font-semibold">
      {t("PAYMENT_PROVIDER_STATE.PAYMENT_PROVIDER_NOT_FOUND")}
    </h2>
    <p className="mt-1 max-w-md text-sm text-muted-foreground">
      {t("PAYMENT_PROVIDER_STATE.IT_MAY_HAVE_BEEN_REMOVED")}
    </p>
  </Card>
);
};
