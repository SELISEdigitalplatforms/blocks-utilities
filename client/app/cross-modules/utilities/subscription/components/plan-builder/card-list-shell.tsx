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
      className="flex min-h-[7rem] flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-muted-foreground/30 p-4 text-sm text-muted-foreground transition hover:border-blocks-primary-400 hover:text-blocks-primary-600"
    >
      <Plus className="h-5 w-5" />
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
  <Card className="relative rounded-lg pr-9">
    <Button
      type="button"
      variant="ghost"
      size="icon"
      onClick={onRemove}
      className="absolute right-1.5 top-1.5 h-7 w-7 text-muted-foreground hover:text-destructive"
      aria-label="Remove"
    >
      <X className="h-4 w-4" />
    </Button>
    <div className="space-y-3">{children}</div>
  </Card>
);
