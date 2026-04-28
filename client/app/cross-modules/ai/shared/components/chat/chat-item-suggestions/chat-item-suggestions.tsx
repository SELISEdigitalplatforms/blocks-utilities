import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";

export const ChatItemSuggestions = ({
  suggestions,
  onSelect,
  className,
  itemClassName,
}: {
  suggestions: string[];
  onSelect?: (suggestion: string) => void;
  className?: string;
  itemClassName?: string;
}) => {
  if (suggestions.length === 0) return null;
  return (
    <div className={cn("mt-2 flex flex-col flex-wrap gap-2", className)}>
      {suggestions.map((suggestion, index) => (
        <Button
          type="button"
          variant="ghost"
          key={index}
          className={cn(
            "flex h-fit w-fit justify-start text-wrap text-left text-primary",
            itemClassName,
          )}
          onClick={() => {
            if (onSelect) onSelect(suggestion);
          }}
        >
          {suggestion}
        </Button>
      ))}
    </div>
  );
};
