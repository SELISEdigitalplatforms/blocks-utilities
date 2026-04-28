import { Button } from "@/components/ui-kits/button/button";
import { Shield, ShieldCheck, X } from "lucide-react";
import { cn } from "@/lib/utils";

type BulkActionBarProps = {
  selectedCount: number;
  onEnableMfa: () => void;
  onEnableCaptcha: () => void;
  onClear: () => void;
};

export const BulkActionBar = ({
  selectedCount,
  onEnableMfa,
  onEnableCaptcha,
  onClear,
}: BulkActionBarProps) => {
  return (
    <div
      className={cn(
        "fixed bottom-6 left-1/2 z-50 -translate-x-1/2 transition-all duration-300",
        selectedCount > 0
          ? "translate-y-0 opacity-100"
          : "pointer-events-none translate-y-4 opacity-0",
      )}
    >
      <div className="flex max-w-[calc(100vw-2rem)] items-center gap-2 rounded-2xl border border-border bg-card/95 px-3 py-2.5 shadow-xl backdrop-blur-sm sm:gap-3 sm:px-5 sm:py-3">
        {/* Selection count */}
        <div className="flex shrink-0 items-center gap-2">
          <span className="flex h-6 w-6 items-center justify-center rounded-full bg-primary text-xs font-bold text-primary-foreground">
            {selectedCount}
          </span>
          <span className="hidden text-xs font-medium uppercase tracking-wider text-muted-foreground sm:block">
            Selected
          </span>
        </div>

        <div className="mx-1 h-5 w-px bg-border sm:mx-2" />

        <Button
          variant="ghost"
          size="sm"
          className="gap-1.5 px-2 sm:px-3"
          onClick={onEnableMfa}
        >
          <ShieldCheck className="h-4 w-4 shrink-0" />
          <span className="hidden sm:inline">Enable MFA</span>
        </Button>

        <Button
          variant="ghost"
          size="sm"
          className="gap-1.5 px-2 sm:px-3"
          onClick={onEnableCaptcha}
        >
          <Shield className="h-4 w-4 shrink-0" />
          <span className="hidden sm:inline">Enable Captcha</span>
        </Button>

        <div className="mx-1 h-5 w-px bg-border" />

        <button
          className="rounded-lg p-1.5 transition-colors hover:bg-accent"
          onClick={onClear}
          title="Clear selection"
        >
          <X className="h-4 w-4 text-muted-foreground" />
        </button>
      </div>
    </div>
  );
};
