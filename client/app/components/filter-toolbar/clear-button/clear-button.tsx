import { Button } from "@/components/ui-kits/button/button";
import { MouseEventHandler } from "react";

type ClearButtonProps = {
  onClear: MouseEventHandler<HTMLButtonElement>;
};

export const ClearButton = ({ onClear }: ClearButtonProps) => {
  return (
    <div className="p-1">
      <Button
        variant="ghost"
        className="h-auto w-full rounded-sm px-2 py-1.5 font-medium text-accent-foreground hover:bg-accent"
        onClick={onClear}
      >
        Clear
      </Button>
    </div>
  );
};
