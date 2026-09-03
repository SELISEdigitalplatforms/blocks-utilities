import { Plus, X } from "lucide-react";
import type { ReactNode } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";

/**
 * A list of item cards plus a dashed "add another" card, shared visual language across quantity
 * items, meters, entitlements and trial grants so a returning admin recognises the shape instead
 * of learning a new layout per section.
 */
export const CardListShell = ({
  children,
  onAdd,
  addLabel,
}: {
  children: ReactNode;
  onAdd: () => void;
  addLabel: string;
}) => (
  <div className="grid gap-3 sm:grid-cols-2">
    {children}
    <button
      type="button"
      onClick={onAdd}
      className="group flex min-h-[7rem] flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-border-default bg-blocks-primary-shades-100/40 p-4 text-sm font-medium text-muted-foreground transition-all duration-200 hover:-translate-y-0.5 hover:border-blocks-primary-300 hover:bg-blocks-primary-shades-200 hover:text-blocks-primary-600 hover:shadow-[0_12px_28px_-20px_hsl(var(--blocks-primary-700)/0.6)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
    >
      <span className="flex h-9 w-9 items-center justify-center rounded-full bg-card text-muted-foreground shadow-sm transition-all duration-200 group-hover:bg-blocks-primary-600 group-hover:text-white group-hover:shadow-[0_8px_18px_-8px_hsl(var(--blocks-primary-700)/0.8)]">
        <Plus className="h-4 w-4 transition-transform duration-300 group-hover:rotate-90" />
      </span>
      {addLabel}
    </button>
  </div>
);

export const CardListItem = ({
  children,
  onRemove,
}: {
  children: ReactNode;
  onRemove: () => void;
}) => (
  <Card className="group/card relative rounded-xl border-border/70 pr-9 transition-all duration-200 hover:-translate-y-0.5 hover:border-blocks-primary-200 hover:shadow-[0_16px_34px_-24px_hsl(var(--blocks-primary-700)/0.55)]">
    {/*
      A tint that only exists on hover, so a card being edited separates from the ones beside it
      without every card in a long list shouting for attention at rest.
    */}
    <span
      aria-hidden="true"
      className="pointer-events-none absolute inset-0 rounded-xl bg-gradient-to-br from-blocks-primary-shades-100/0 via-blocks-primary-shades-100/0 to-blocks-secondary-50/0 opacity-0 transition-opacity duration-300 group-hover/card:opacity-100 group-hover/card:to-blocks-secondary-50/60"
    />
    <Button
      type="button"
      variant="ghost"
      size="icon"
      onClick={onRemove}
      className="absolute right-1.5 top-1.5 z-10 h-7 w-7 rounded-lg text-muted-foreground opacity-60 transition-all duration-200 hover:bg-destructive/10 hover:text-destructive hover:opacity-100 group-hover/card:opacity-100"
      aria-label="Remove"
    >
      <X className="h-4 w-4" />
    </Button>
    <div className="relative space-y-3">{children}</div>
  </Card>
);
