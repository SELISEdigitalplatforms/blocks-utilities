import type { ReactNode } from "react";

interface SmsPageHeaderProps {
  title: string;
  description: string;
  icon: ReactNode;
  actions?: ReactNode;
}

/** Same header treatment as the payment provider pages, so the utilities read as one product. */
export const SmsPageHeader = ({ title, description, icon, actions }: SmsPageHeaderProps) => (
  <section className="relative overflow-hidden rounded-2xl border bg-gradient-to-br from-blocks-primary-shades-100 via-card to-blocks-secondary-50 p-5 shadow-sm sm:p-7">
    <div className="absolute -right-16 -top-20 h-52 w-52 rounded-full bg-blocks-primary-100/30 blur-3xl" />
    <div className="relative flex flex-col justify-between gap-5 sm:flex-row sm:items-center">
      <div className="flex items-start gap-4">
        <div className="rounded-xl bg-blocks-primary-600 p-3 text-white shadow-sm">{icon}</div>
        <div>
          <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">{title}</h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground sm:text-base">{description}</p>
        </div>
      </div>
      {actions && <div className="flex flex-wrap gap-2 self-start sm:self-center">{actions}</div>}
    </div>
  </section>
);

interface SmsSectionProps {
  title: string;
  description?: string;
  children: ReactNode;
}

export const SmsFormSection = ({ title, description, children }: SmsSectionProps) => (
  <section className="space-y-5">
    <div>
      <h2 className="text-lg font-semibold">{title}</h2>
      {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
    </div>
    {children}
  </section>
);
