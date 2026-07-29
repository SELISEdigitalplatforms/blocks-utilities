import {
  AlertCircle,
  Ban,
  CheckCircle2,
  CircleDashed,
  CreditCard,
  HelpCircle,
  XCircle,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { Link, useParams, useSearchParams } from "react-router";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { cn } from "@/lib/utils";

type PaymentResultStatus =
  | "success"
  | "fail"
  | "cancelled"
  | "pending"
  | "unknown";

interface PaymentResultPresentation {
  title: string;
  description: string;
  Icon: LucideIcon;
  iconClassName: string;
  iconContainerClassName: string;
}

const resultPresentations: Record<
  PaymentResultStatus,
  PaymentResultPresentation
> = {
  success: {
    title: "Payment completed",
    description:
      "The hosted checkout completed successfully. Final confirmation may take a moment to appear in the payment list.",
    Icon: CheckCircle2,
    iconClassName: "text-green-700",
    iconContainerClassName: "border-green-200 bg-green-50",
  },
  fail: {
    title: "Payment failed",
    description:
      "The payment could not be completed. No sensitive provider details have been included in this redirect.",
    Icon: XCircle,
    iconClassName: "text-destructive",
    iconContainerClassName: "border-destructive/20 bg-destructive/5",
  },
  cancelled: {
    title: "Payment cancelled",
    description:
      "The checkout was cancelled before the payment completed. You can safely start a new payment.",
    Icon: Ban,
    iconClassName: "text-amber-700",
    iconContainerClassName: "border-amber-200 bg-amber-50",
  },
  pending: {
    title: "Payment is processing",
    description:
      "The final result is not available yet. Check the payment list again shortly for the authoritative status.",
    Icon: CircleDashed,
    iconClassName: "animate-spin text-blue-700",
    iconContainerClassName: "border-blue-200 bg-blue-50",
  },
  unknown: {
    title: "Payment result unavailable",
    description:
      "The redirect did not contain a recognized payment status. Check the payment list before trying again.",
    Icon: HelpCircle,
    iconClassName: "text-muted-foreground",
    iconContainerClassName: "border-border bg-muted",
  },
};

const normalizeStatus = (value: string | null): PaymentResultStatus => {
  const normalized = value?.trim().toLowerCase();

  if (normalized === "canceled") {
    return "cancelled";
  }

  return normalized === "success" ||
    normalized === "fail" ||
    normalized === "cancelled" ||
    normalized === "pending"
    ? normalized
    : "unknown";
};

export const PaymentResultPage = () => {
  const { itemId } = useParams();
  const [searchParameters] = useSearchParams();
  const status = normalizeStatus(searchParameters.get("status"));
  const paymentDetailId = searchParameters.get("paymentDetailId");
  const presentation = resultPresentations[status];
  const paymentBasePath = `/app/${itemId ?? ""}/payment`;
  const paymentListPath = `${paymentBasePath}/list`;
  const createPaymentPath = `${paymentBasePath}/create`;
  const Icon = presentation.Icon;

  return (
    <main className="grid min-h-[calc(100vh-10rem)] place-items-center p-4 sm:p-6 lg:p-8">
      <Card className="w-full max-w-2xl overflow-hidden rounded-2xl p-0 shadow-md">
        <div className="border-b bg-gradient-to-br from-blocks-primary-shades-100 via-card to-blocks-secondary-50 p-6 sm:p-8">
          <div className="flex items-center gap-3 text-sm font-medium text-muted-foreground">
            <CreditCard className="h-5 w-5 text-blocks-primary-600" />
            Hosted payment result
          </div>
        </div>

        <div className="px-6 py-10 text-center sm:px-10 sm:py-12">
          <span
            className={cn(
              "mx-auto grid h-20 w-20 place-items-center rounded-full border",
              presentation.iconContainerClassName,
            )}
          >
            <Icon
              className={cn("h-10 w-10", presentation.iconClassName)}
            />
          </span>

          <h1 className="mt-6 text-2xl font-bold tracking-tight sm:text-3xl">
            {presentation.title}
          </h1>
          <p className="mx-auto mt-3 max-w-lg text-sm leading-6 text-muted-foreground sm:text-base">
            {presentation.description}
          </p>

          {paymentDetailId && (
            <div className="mx-auto mt-6 max-w-md rounded-lg bg-muted/60 px-4 py-3">
              <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                Payment ID
              </p>
              <p className="mt-1 break-all font-mono text-xs">
                {paymentDetailId}
              </p>
            </div>
          )}

          {status === "unknown" && (
            <div
              role="alert"
              className="mx-auto mt-5 flex max-w-md items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-left text-sm text-amber-900"
            >
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
              Only success, fail, cancelled, or pending are recognized.
            </div>
          )}

          <div className="mt-8 flex flex-col justify-center gap-3 sm:flex-row">
            <Button asChild>
              <Link to={paymentListPath}>View payments</Link>
            </Button>
            {(status === "fail" ||
              status === "cancelled" ||
              status === "unknown") && (
              <Button asChild variant="outline">
                <Link to={createPaymentPath}>Create another payment</Link>
              </Button>
            )}
            <Button
              type="button"
              variant="ghost"
              onClick={() => window.close()}
            >
              Close this tab
            </Button>
          </div>
        </div>
      </Card>
    </main>
  );
};
