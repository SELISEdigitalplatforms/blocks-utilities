import type { ReactNode } from "react";
import { ArrowLeft, Layers } from "lucide-react";
import { Link } from "react-router";
import { Button } from "@/components/ui-kits/button/button";

interface SubscriptionPlanPageHeaderProps {
  title: string;
  description: string;
  backTo?: string;
  backLabel?: string;
  actions?: ReactNode;
  icon?: ReactNode;
}

export const SubscriptionPlanPageHeader = ({
  title,
  description,
  backTo,
  backLabel = "Back to plans",
  actions,
  icon,
}: SubscriptionPlanPageHeaderProps) => (
  <section className="relative overflow-hidden rounded-2xl border bg-gradient-to-br from-blocks-primary-shades-100 via-card to-blocks-secondary-50 p-5 shadow-sm sm:p-7">
    <div className="absolute -right-16 -top-20 h-52 w-52 rounded-full bg-blocks-primary-100/30 blur-3xl" />
    <div className="relative">
      {backTo && (
        <Button asChild variant="ghost" size="sm" className="-ml-3 mb-3">
          <Link to={backTo}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            {backLabel}
          </Link>
        </Button>
      )}

      <div className="flex flex-col justify-between gap-5 sm:flex-row sm:items-center">
        <div className="flex items-start gap-4">
          <div className="rounded-xl bg-blocks-primary-600 p-3 text-white shadow-sm">
            {icon ?? <Layers className="h-6 w-6" />}
          </div>
          <div>
            <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">{title}</h1>
            <p className="mt-1 max-w-2xl text-sm text-muted-foreground sm:text-base">
              {description}
            </p>
          </div>
        </div>

        {actions && (
          <div className="flex flex-wrap gap-2 self-start sm:self-center">{actions}</div>
        )}
      </div>
    </div>
  </section>
);
